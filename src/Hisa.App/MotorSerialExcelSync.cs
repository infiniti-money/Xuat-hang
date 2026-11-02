using System.IO;
using System.Text;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Commit user-typed motor serials back to the original production workbook,
/// column I only. Match contract numbers from column D, without requiring
/// matching model, project or company text. Only an exact count of distinct
/// empty serial cells and new serial values may be mapped; show the actual
/// destination Excel rows for explicit user approval. Never overwrite a
/// source serial, formula, status cell or ship goods implicitly.
/// </summary>
public static class MotorSerialExcelSync
{
    public sealed record Assignment(int SourceRow,string Contract,string Project,
        string Company,string Model,string Serial);
    public sealed record Outcome(int Written,int AlreadyPresent,
        IReadOnlyList<Assignment> Proposed,IReadOnlyList<string> Warnings);

    private const int SerialColumn=9;
    private static readonly object Gate=new();

    private static (List<Assignment>,int,List<string>) BuildPlan(
        Slip slip,XLWorkbook book,string source)
    {
        var warnings=new List<string>();
        var changes=new List<Assignment>();
        var already=0;
        MotorSerials.Validate(slip,requireAll:false);
        var sheet=book.Worksheets.FirstOrDefault()??
            throw new InvalidDataException("MÁY KÉO.xlsx không có sheet nguồn.");
        if(Rules.Code(sheet.Cell(2,4).GetString())!="HD")
            throw new InvalidDataException("MÁY KÉO.xlsx: cột D dòng 2 không phải HĐ. Không ghi file.");
        var header=Rules.Code(sheet.Cell(2,SerialColumn).GetString());
        if(header.Length>0 && !header.Contains("SERI",StringComparison.Ordinal) &&
            header!="SN" && header!="SERIALNO")
            throw new InvalidDataException("Cột I không có tiêu đề Serial/Seri. Không sửa file.");
        // The same reader as the application uses for automatic serial lookup.
        var rows=EquipmentListReader.Read(source,"Máy kéo");
        var filled=MotorSerials.BuildRows(slip)
            .Where(x=>!string.IsNullOrWhiteSpace(x.SerialNumber)).ToList();
        var dup=rows.Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
            .GroupBy(x=>Rules.Code(x.Serial)).Where(g=>g.Count()>1)
            .Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        var global=rows.Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
            .GroupBy(x=>Rules.Code(x.Serial),StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.Ordinal);
        // Contract number from production column D is the sole lookup key.
        // We do not require project, company or machine-model text to match
        // between the slip and MÁY KÉO.xlsx. The preview shows actual source
        // rows, and writing is permitted only for a complete 1:1 set of
        // empty column-I cells for this contract (never a guessed subset).
        foreach(var group in filled.GroupBy(x=>Rules.Contract(x.Contract),StringComparer.Ordinal))
        {
            var first=group.First();
            var sourceGroup=rows.Where(x=>Rules.Contract(x.Contract)==group.Key)
                .OrderBy(x=>x.SourceRow).ToList();
            if(sourceGroup.Count==0)
            {
                warnings.Add("HĐ "+first.Contract+
                    ": không tìm thấy số hợp đồng trong cột D MÁY KÉO.xlsx. "+
                    "Hãy kiểm tra lại số HĐ và file đang liên kết.");
                continue;
            }
            var wanted=group.OrderBy(x=>x.LineId,StringComparer.Ordinal)
                .ThenBy(x=>x.UnitIndex).ToList();
            var absent=new List<MotorSerialEntry>();
            var conflict=false;
            foreach(var unit in wanted)
            {
                var key=Rules.Code(unit.SerialNumber);
                if(key.Length==0||key.Length>200||
                    unit.SerialNumber.Contains('\r')||unit.SerialNumber.Contains('\n'))
                {
                    warnings.Add("HĐ "+first.Contract+": serial nhập tay không hợp lệ.");
                    conflict=true;break;
                }
                if(dup.Contains(key))
                {
                    warnings.Add("Serial "+unit.SerialNumber+
                        " bị trùng nhiều dòng trong MÁY KÉO.xlsx. Không tự ghi.");
                    conflict=true;break;
                }
                if(global.TryGetValue(key,out var existing))
                {
                    if(!sourceGroup.Contains(existing.Single()))
                    {
                        warnings.Add("Serial "+unit.SerialNumber+
                            " đã thuộc số hợp đồng khác trong MÁY KÉO.xlsx.");
                        conflict=true;break;
                    }
                    already++;
                }
                else absent.Add(unit);
            }
            if(conflict||absent.Count==0)continue;
            var blanks=sourceGroup.Where(x=>string.IsNullOrWhiteSpace(x.Serial)).ToList();
            if(blanks.Count!=absent.Count ||
                blanks.Any(x=>x.Quantity.HasValue && x.Quantity!=1))
            {
                warnings.Add("HĐ "+first.Contract+": cột I có "+blanks.Count+
                    " dòng serial trống nhưng phiếu có "+absent.Count+
                    " serial mới cần ghi. Chỉ tự ghép khi mỗi dòng là một "+
                    "máy và số dòng trống bằng số serial. Hãy kiểm tra "+
                    "danh sách dòng máy kéo theo HĐ để tránh phân bổ nhầm.");
                continue;
            }
            for(int i=0;i<blanks.Count;i++)
            {
                var row=blanks[i];
                var cell=sheet.Cell(row.SourceRow,SerialColumn);
                if(cell.HasFormula||!string.IsNullOrWhiteSpace(cell.GetString()))
                {
                    warnings.Add("Cột I dòng "+row.SourceRow+
                        " chứa công thức hoặc dữ liệu khác. Không ghi đè.");
                    conflict=true;break;
                }
                changes.Add(new Assignment(row.SourceRow,row.Contract,row.Project,
                    row.Company,row.Material,absent[i].SerialNumber.Trim()));
            }
            if(conflict)changes.RemoveAll(x=>blanks.Any(y=>y.SourceRow==x.SourceRow));
        }
        return (changes,already,warnings);
    }

    public static Outcome Preview(AppSettings settings,Slip slip)
    {
        var path=settings.TractionMachineListPath;
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
            throw new FileNotFoundException("Chưa liên kết file MÁY KÉO.xlsx.",path);
        lock(Gate)
        {
            using var book=new XLWorkbook(path);
            var (actions,already,warnings)=BuildPlan(slip,book,path);
            return new Outcome(0,already,actions,warnings);
        }
    }

    public static Outcome Apply(AppSettings settings,Slip slip,string backupFolder,
        IReadOnlyList<Assignment>? approvedAssignments=null)
    {
        var path=settings.TractionMachineListPath;
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
            throw new FileNotFoundException("Chưa liên kết file MÁY KÉO.xlsx.",path);
        lock(Gate)
        {
            // The plan is rebuilt at the moment of writing; a stale preview
            // cannot authorize overwriting changes made in Excel meanwhile.
            var stamp=File.GetLastWriteTimeUtc(path);
            var length=new FileInfo(path).Length;
            using var book=new XLWorkbook(path);
            var (actions,already,warnings)=BuildPlan(slip,book,path);
            if(approvedAssignments!=null)
            {
                // The physical Excel rows were displayed in the approval
                // dialog. If they changed after preview, a new confirmation
                // is mandatory rather than silently choosing other rows.
                var planned=actions.OrderBy(x=>x.SourceRow)
                    .Select(x=>(x.SourceRow,x.Contract,x.Serial)).ToArray();
                var approved=approvedAssignments.OrderBy(x=>x.SourceRow)
                    .Select(x=>(x.SourceRow,x.Contract,x.Serial)).ToArray();
                if(!planned.SequenceEqual(approved))
                    throw new IOException("Danh sách dòng serial trong MÁY KÉO.xlsx "+
                        "đã khác bản xem trước. Chưa ghi dữ liệu; hãy tải lại "+
                        "và xác nhận các dòng cột I lần nữa.");
            }
            if(actions.Count==0)return new Outcome(0,already,[],warnings);
            var sheet=book.Worksheets.First();
            foreach(var action in actions)
            {
                var cell=sheet.Cell(action.SourceRow,SerialColumn);
                if(cell.HasFormula||!string.IsNullOrWhiteSpace(cell.GetString()))
                    throw new InvalidDataException("Cột I đã thay đổi, không ghi đè serial.");
                cell.Style.NumberFormat.Format="@";
                cell.Value=action.Serial;
            }
            var full=Path.GetFullPath(path);
            var tmp=Path.Combine(Path.GetDirectoryName(full)!,"."+
                Path.GetFileNameWithoutExtension(full)+"-serial-"+
                Guid.NewGuid().ToString("N")+".tmp.xlsx");
            try
            {
                using(var guard=new FileStream(full,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {
                    if(guard.Length!=length||File.GetLastWriteTimeUtc(full)!=stamp)
                        throw new IOException("File Máy kéo vừa thay đổi. Làm mới, đối chiếu lại rồi xác nhận.");
                    Directory.CreateDirectory(backupFolder);
                    var backup=Path.Combine(backupFolder,"MAY-KEO-truoc-ghi-serial-"+
                        DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+
                        Guid.NewGuid().ToString("N")[..8]+".xlsx");
                    using(var backupStream=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {
                        guard.Position=0;
                        guard.CopyTo(backupStream);
                    }
                }
                // ClosedXML may re-open the original internally when saving a
                // workbook to a new path; do not retain an exclusive source
                // handle while calling SaveAs. We verify the source again
                // afterwards before replacing it.
                book.SaveAs(tmp,new SaveOptions{
                    EvaluateFormulasBeforeSaving=false,GenerateCalculationChain=false});
                // Never replace the original if Excel or another writer has
                // opened it after we released the verification lock.
                using(var guard=new FileStream(full,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {
                    if(guard.Length!=length||File.GetLastWriteTimeUtc(full)!=stamp)
                        throw new IOException("Excel đã thay đổi trong lúc ghi; không thay thế file nguồn.");
                }
                File.Move(tmp,full,true);
                return new Outcome(actions.Count,already,actions,warnings);
            }
            catch(IOException e)
            {
                throw new IOException("Không thể ghi serial vào MÁY KÉO.xlsx. "+
                    "Hãy lưu và đóng Excel nguồn rồi thử lại. "+e.Message,e);
            }
            finally {if(File.Exists(tmp))File.Delete(tmp);}
        }
    }
}
