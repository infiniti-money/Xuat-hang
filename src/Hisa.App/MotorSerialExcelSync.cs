using System.IO;
using System.Text;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Commit user-typed motor serials back to the original production workbook,
/// column I only. Exact HĐ + project + company is mandatory. When the
/// description of a motor model differs between the slip and the Excel source,
/// an unambiguous single-model / one-machine-per-row fallback is permitted,
/// using the SAME strict scope as automatic serial lookup. No contract-only
/// fallback, formula edits, fabricated rows or shipment/status changes.
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
        foreach(var group in filled.GroupBy(x=>
            Rules.Identity(x.Contract,x.Project,x.Company)+"|"+Rules.Code(x.Material)))
        {
            var first=group.First();
            if(string.IsNullOrWhiteSpace(first.Project)||
               string.IsNullOrWhiteSpace(first.Company))
            {
                warnings.Add("HĐ "+first.Contract+": thiếu công trình/đơn vị, chưa thể ghi serial.");
                continue;
            }
            var wanted=group.OrderBy(x=>x.LineId,StringComparer.Ordinal)
                .ThenBy(x=>x.UnitIndex).ToList();
            var identity=Rules.Identity(first.Contract,first.Project,first.Company);
            var byContract=rows.Where(x=>
                Rules.Contract(x.Contract)==Rules.Contract(first.Contract)).ToList();
            var sameIdentity=byContract.Where(x=>
                Rules.Identity(x.Contract,x.Project,x.Company)==identity).ToList();
            var sourceGroup=sameIdentity.Where(x=>
                Rules.Code(x.Material)==Rules.Code(first.Material)).ToList();
            if(sourceGroup.Count==0)
            {
                // The incoming material name and the production-list model
                // name can differ for the SAME actual machine. AutoFill uses
                // the same safe rule: only one motor slip line in this exact
                // contract/project/company, a single source model, and one
                // physical source row for every machine in the line.
                var motorLines=slip.Lines.Where(MotorSerials.IsMotor &&
                    Rules.Identity(x.Contract,x.Project,x.Company)==identity).ToList();
                var models=sameIdentity.Select(x=>Rules.Code(x.Material))
                    .Distinct(StringComparer.Ordinal).ToList();
                if(motorLines.Count==1 && models.Count==1 &&
                   sameIdentity.Count==wanted.Count &&
                   sameIdentity.All(x=>x.Quantity==1))
                {
                    sourceGroup=sameIdentity;
                }
                else if(byContract.Count==0)
                {
                    warnings.Add("HĐ "+first.Contract+
                        ": không tìm thấy mã HĐ trong cột D file MÁY KÉO.xlsx. "+
                        "Kiểm tra đúng file nguồn và tải lại danh sách máy.");
                    continue;
                }
                else if(sameIdentity.Count==0)
                {
                    var available=string.Join("; ",byContract
                        .Select(x=>x.Project+" / "+x.Company)
                        .Distinct(StringComparer.Ordinal).Take(3));
                    warnings.Add("HĐ "+first.Contract+
                        ": công trình/đơn vị trong MÁY KÉO.xlsx không khớp phiếu. "+
                        "Phiếu: "+first.Project+" / "+first.Company+
                        "; file: "+available+
                        ". Không tự ghép chỉ theo số HĐ để tránh nhầm T1/T2.");
                    continue;
                }
                else
                {
                    var available=string.Join("; ",sameIdentity
                        .Select(x=>x.Material).Distinct(StringComparer.Ordinal).Take(3));
                    warnings.Add("HĐ "+first.Contract+" · "+first.Project+
                        ": model trên phiếu '"+first.Material+
                        "' khác file MÁY KÉO.xlsx ('"+available+
                        "'). Có "+sameIdentity.Count+" dòng máy trong file cho "+
                        wanted.Count+" máy trên phiếu. "+
                        "Chỉ tự ghi khi duy nhất một model và mỗi máy có một dòng (SL=1); "+
                        "nếu có nhiều model, cần đối chiếu lại để tránh ghi sai cột I.");
                    continue;
                }
            }
            var absent=new List<MotorSerialEntry>();
            var conflict=false;
            foreach(var unit in wanted)
            {
                var key=Rules.Code(unit.SerialNumber);
                if(key.Length==0||key.Length>200 ||
                    unit.SerialNumber.Contains('\r')||unit.SerialNumber.Contains('\n'))
                {
                    warnings.Add("HĐ "+first.Contract+": serial nhập tay không hợp lệ.");
                    conflict=true;break;
                }
                if(dup.Contains(key)||global.TryGetValue(key,out var existing) &&
                    !sourceGroup.Contains(existing.Single()))
                {
                    warnings.Add("Serial "+unit.SerialNumber+
                        " đã có ở dòng khác hoặc bị trùng trong MÁY KÉO.xlsx.");
                    conflict=true;break;
                }
                if(global.ContainsKey(key))already++;
                else absent.Add(unit);
            }
            if(conflict||absent.Count==0)continue;
            var blanks=sourceGroup.Where(x=>string.IsNullOrWhiteSpace(x.Serial)).ToList();
            // A single row representing multiple machines cannot hold multiple
            // physical serials. The operator must first resolve the source table.
            if(blanks.Count!=absent.Count||
                blanks.Any(x=>x.Quantity.HasValue && x.Quantity!=1))
            {
                warnings.Add("HĐ "+first.Contract+" · "+first.Project+
                    ": có "+absent.Count+" serial cần ghi nhưng "+
                    blanks.Count+" dòng máy thiếu serial trong nguồn. "+
                    "Chỉ ghi khi mỗi dòng là đúng một máy, không tự phân bổ nhầm.");
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

    public static Outcome Apply(AppSettings settings,Slip slip,string backupFolder)
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
