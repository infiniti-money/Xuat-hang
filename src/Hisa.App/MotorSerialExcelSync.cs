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
        Slip slip,XLWorkbook book,string source,
        IReadOnlyDictionary<string,string>? approvedProjectChoices)
    {
        var warnings=new List<string>();
        var changes=new List<Assignment>();
        int already=0;
        MotorSerials.Validate(slip,requireAll:false);
        var sheet=book.Worksheets.FirstOrDefault()??
            throw new InvalidDataException("MÁY KÉO.xlsx không có sheet nguồn.");
        if(Rules.Code(sheet.Cell(2,4).GetString())!="HD")
            throw new InvalidDataException("MÁY KÉO.xlsx: cột D dòng 2 không phải HĐ. Không ghi file.");
        var header=Rules.Code(sheet.Cell(2,SerialColumn).GetString());
        if(header.Length>0 && !header.Contains("SERI",StringComparison.Ordinal) &&
            header!="SN" && header!="SERIALNO")
            throw new InvalidDataException("Cột I không có tiêu đề Serial/Seri. Không sửa file.");

        var rows=EquipmentListReader.Read(source,"Máy kéo");
        var filled=MotorSerials.BuildRows(slip)
            .Where(x=>!string.IsNullOrWhiteSpace(x.SerialNumber)).ToList();
        var dup=rows.Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
            .GroupBy(x=>Rules.Code(x.Serial),StringComparer.Ordinal)
            .Where(g=>g.Count()>1).Select(g=>g.Key)
            .ToHashSet(StringComparer.Ordinal);
        var global=rows.Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
            .GroupBy(x=>Rules.Code(x.Serial),StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.Ordinal);

        // Source column D finds the contract. Source column B separates projects;
        // optionally column C disambiguates different companies under a project.
        // Grouping per project prevents a T1 serial from being written into T2.
        foreach(var group in filled.GroupBy(x=>(
            Contract:Rules.Contract(x.Contract),
            Project:Rules.Code(x.Project),Company:Rules.Code(x.Company))))
        {
            var first=group.First();
            var selectionKey=MotorSerialProjectScope.SlipKey(
                first.Contract,first.Project,first.Company);
            string? selectedProject=null;
            if(approvedProjectChoices!=null)
                approvedProjectChoices.TryGetValue(selectionKey,out selectedProject);
            var scope=MotorSerialProjectScope.Resolve(
                rows,first.Contract,first.Project,first.Company,selectedProject);
            if(scope.Warning!=null)
            {
                warnings.Add(scope.Warning);
                continue;
            }
            var sourceGroup=scope.Rows.OrderBy(x=>x.SourceRow).ToList();
            var absent=new List<MotorSerialEntry>();
            bool conflict=false;
            foreach(var unit in group)
            {
                var key=Rules.Code(unit.SerialNumber);
                if(key.Length==0||unit.SerialNumber.Length>200||
                   unit.SerialNumber.Contains('\r')||unit.SerialNumber.Contains('\n'))
                {
                    warnings.Add("HĐ "+first.Contract+" · "+first.Project+
                        ": serial nhập tay không hợp lệ.");
                    conflict=true;break;
                }
                if(dup.Contains(key))
                {
                    warnings.Add("Serial "+unit.SerialNumber+
                        " bị trùng nhiều dòng trong MÁY KÉO.xlsx.");
                    conflict=true;break;
                }
                if(global.TryGetValue(key,out var existing))
                {
                    if(existing.Count!=1 || !sourceGroup.Any(x=>
                        x.SourceRow==existing[0].SourceRow))
                    {
                        warnings.Add("Serial "+unit.SerialNumber+" thuộc công trình "+
                            existing[0].Project+" hoặc HĐ khác. Không ghi đè/chuyển serial.");
                        conflict=true;break;
                    }
                    already++;
                }
                else absent.Add(unit);
            }
            if(conflict||absent.Count==0)continue;
            var blanks=sourceGroup.Where(x=>
                string.IsNullOrWhiteSpace(x.Serial)).ToList();
            if(blanks.Count!=absent.Count ||
               blanks.Any(x=>x.Quantity.HasValue&&x.Quantity!=1))
            {
                warnings.Add("HĐ "+first.Contract+" · "+first.Project+
                    ": có "+blanks.Count+" ô cột I trống nhưng có "+
                    absent.Count+" serial mới. Không tự chọn dòng Excel khi "+
                    "không thể ghép chính xác một máy với một serial.");
                continue;
            }
            var proposed=new List<Assignment>();
            foreach(var (row,unit) in blanks.Zip(absent.OrderBy(x=>x.LineId,
                StringComparer.Ordinal).ThenBy(x=>x.UnitIndex)))
            {
                var cell=sheet.Cell(row.SourceRow,SerialColumn);
                if(cell.HasFormula||!string.IsNullOrWhiteSpace(cell.GetString()))
                {
                    warnings.Add("Cột I dòng "+row.SourceRow+
                        " chứa công thức hoặc dữ liệu khác. Không ghi đè.");
                    conflict=true;break;
                }
                if(changes.Any(x=>x.SourceRow==row.SourceRow))
                {
                    warnings.Add("Dòng "+row.SourceRow+
                        " đã được dùng cho công trình khác trên phiếu. Không ghi trùng.");
                    conflict=true;break;
                }
                proposed.Add(new Assignment(row.SourceRow,row.Contract,row.Project,
                    row.Company,row.Material,unit.SerialNumber.Trim()));
            }
            if(!conflict)changes.AddRange(proposed);
        }
        return (changes,already,warnings);
    }

    public static Outcome Preview(AppSettings settings,Slip slip,
        IReadOnlyDictionary<string,string>? approvedProjectChoices=null)
    {
        var path=settings.TractionMachineListPath;
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
            throw new FileNotFoundException("Chưa liên kết file MÁY KÉO.xlsx.",path);
        lock(Gate)
        {
            using var book=new XLWorkbook(path);
            var (actions,already,warnings)=BuildPlan(
                slip,book,path,approvedProjectChoices);
            return new Outcome(0,already,actions,warnings);
        }
    }

    public static Outcome Apply(AppSettings settings,Slip slip,string backupFolder,
        IReadOnlyList<Assignment>? approvedAssignments=null,
        IReadOnlyDictionary<string,string>? approvedProjectChoices=null)
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
            var (actions,already,warnings)=BuildPlan(
                slip,book,path,approvedProjectChoices);
            if(approvedAssignments!=null)
            {
                // The physical Excel rows were displayed in the approval
                // dialog. If they changed after preview, a new confirmation
                // is mandatory rather than silently choosing other rows.
                var planned=actions.OrderBy(x=>x.SourceRow)
                    .Select(x=>(x.SourceRow,x.Contract,x.Project,x.Company,x.Serial)).ToArray();
                var approved=approvedAssignments.OrderBy(x=>x.SourceRow)
                    .Select(x=>(x.SourceRow,x.Contract,x.Project,x.Company,x.Serial)).ToArray();
                if(!planned.SequenceEqual(approved))
                    throw new IOException("Danh sách dòng serial trong MÁY KÉO.xlsx "+
                        "đã khác bản xem trước. Chưa ghi dữ liệu; hãy tải lại "+
                        "và xác nhận các dòng cột I lần nữa.");
            }
            if(actions.Count==0)return new Outcome(0,already,[],warnings);
            // The previous ClosedXML SaveAs rebuilt the entire workbook,
            // which could discard Excel-only extensions and make Excel repair
            // a production file. Patch ONLY column I XML cells instead.
            if(File.GetLastWriteTimeUtc(path)!=stamp ||
                new FileInfo(path).Length!=length)
                throw new IOException("MÁY KÉO.xlsx vừa thay đổi; tải lại và duyệt serial.");
            MotorWorkbookCellPatch.Apply(path,backupFolder,
                actions.Select(a=>new MotorWorkbookCellPatch.Edit(
                    a.SourceRow,SerialColumn,a.Serial)).ToList(),
                "MAY-KEO-truoc-ghi-serial");
            return new Outcome(actions.Count,already,actions,warnings);
        }
    }
}
