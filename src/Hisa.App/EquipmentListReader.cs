using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Read-only production lists: Tủ điện, Máy kéo, Đầu cửa car.
/// A production record is NOT a confirmed warehouse arrival, nor material
/// available for issuing a delivery slip. Never write back to these workbooks.
/// </summary>
public static class EquipmentListReader
{
    public sealed class Row
    {
        public string Category { get; set; } = "";
        public string Contract { get; set; } = "";
        public string Project { get; set; } = "";
        public string Company { get; set; } = "";
        public string Material { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal? Quantity { get; set; }
        public string Serial { get; set; } = "";
        public string Lot { get; set; } = "";
        public string ProductionDate { get; set; } = "";
        public string Code { get; set; } = "";
        public string Note { get; set; } = "";
        public string Source { get; set; } = "";
        public int SourceRow { get; set; }
        public string Sheet { get; set; } = "";
        public string Verification { get; set; } = "";
    }

    /// <summary>The three production workbooks are independent, optional inputs.</summary>
    public static IEnumerable<string> ConfiguredPaths(AppSettings settings)=>
        new[]{settings.ElectricalCabinetListPath,settings.TractionMachineListPath,settings.CarDoorListPath}
            .Where(path=>!string.IsNullOrWhiteSpace(path));

    public static bool IsConfiguredEquipmentFile(string path,AppSettings settings)
    {
        var full=Path.GetFullPath(path);
        return ConfiguredPaths(settings).Any(p=>
            string.Equals(Path.GetFullPath(p),full,StringComparison.OrdinalIgnoreCase));
    }

    private static bool WithinFolder(string path,string folder)
    {
        var relative=Path.GetRelativePath(Path.GetFullPath(folder),Path.GetFullPath(path));
        return relative!="." && relative!=".." &&
            !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// Production workbooks must not live inside the master Excel directory or
    /// under CONT ĐI. Never move user files implicitly: ask them to choose the
    /// independently located copies instead. Existing settings are still read.
    /// </summary>
    public static void ValidateIndependentSources(AppSettings settings)
    {
        if(!ConfiguredPaths(settings).Any())return;
        var master=Path.GetFullPath(settings.MasterPath);
        var mainFolder=Path.GetDirectoryName(master)!;
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var (kind,path) in new[]{
            ("Tủ điện",settings.ElectricalCabinetListPath),
            ("Máy kéo",settings.TractionMachineListPath),
            ("Đầu cửa car",settings.CarDoorListPath)
        })
        {
            if(string.IsNullOrWhiteSpace(path))continue;
            var full=Path.GetFullPath(path);
            if(!File.Exists(full))
                throw new FileNotFoundException("Không tìm thấy file danh sách "+kind+": "+full,full);
            if(!used.Add(full))
                throw new InvalidOperationException("Mỗi danh sách thiết bị phải chọn một file Excel riêng: "+kind);
            if(string.Equals(Path.GetDirectoryName(full),mainFolder,StringComparison.OrdinalIgnoreCase) ||
               string.Equals(full,master,StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(settings.KtPath) &&
                string.Equals(full,Path.GetFullPath(settings.KtPath),StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(settings.ProductExportPath) &&
                string.Equals(full,Path.GetFullPath(settings.ProductExportPath),StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Danh sách "+kind+" phải nằm ở thư mục riêng, KHÔNG để chung với Tổng hợp.xlsx. HISA sẽ không tự chuyển file; hãy chọn đúng đường dẫn gốc.");
            if(settings.DataSources.Any(folder=>!string.IsNullOrWhiteSpace(folder)&&
                (WithinFolder(full,folder)||string.Equals(full,Path.GetFullPath(folder),StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Danh sách "+kind+" đang nằm trong thư mục nguồn CONT ĐI. Hãy đưa file sang thư mục thiết bị riêng, rồi chọn lại đường dẫn; HISA không tự di chuyển.");
        }
    }

    public sealed record Catalog(List<Row> Rows,List<string> Warnings)
    {
        public List<Row> Find(AiScheduleLine schedule)
        {
            var code=Rules.Contract(schedule.Contract);
            if(code.Length==0)return [];
            var matches=Rows.Where(x=>Rules.Contract(x.Contract)==code).ToList();
            var identity=Rules.Identity(schedule.Contract,schedule.Project,schedule.Company);
            foreach(var row in matches)
            {
                row.Verification= !string.IsNullOrWhiteSpace(schedule.Project) &&
                    !string.IsNullOrWhiteSpace(schedule.Company) &&
                    Rules.Identity(row.Contract,row.Project,row.Company)==identity
                    ? "Khớp HĐ, công trình, đơn vị"
                    : "Chỉ khớp số HĐ · kiểm tra công trình/đơn vị";
            }
            return matches;
        }
    }

    private sealed record CacheEntry(long Length,long Stamp,List<Row> Rows);
    private static readonly object CacheGate=new();
    private static readonly Dictionary<string,CacheEntry> Cache=new(StringComparer.OrdinalIgnoreCase);

    public static Catalog Load(AppSettings settings)
    {
        var rows=new List<Row>();var warnings=new List<string>();
        foreach(var (path,category) in new[]{
            (settings.ElectricalCabinetListPath,"Tủ điện"),
            (settings.TractionMachineListPath,"Máy kéo"),
            (settings.CarDoorListPath,"Đầu cửa car")
        })
        {
            if(string.IsNullOrWhiteSpace(path))continue;
            try
            {
                var full=Path.GetFullPath(path);
                var file=new FileInfo(full);
                if(!file.Exists)
                {
                    warnings.Add(category+": không tìm thấy tệp "+Path.GetFileName(full));
                    continue;
                }
                var key=category+"|"+full;
                List<Row>? cached=null;
                lock(CacheGate)
                {
                    if(Cache.TryGetValue(key,out var hit) &&
                       hit.Length==file.Length && hit.Stamp==file.LastWriteTimeUtc.Ticks)
                        cached=hit.Rows;
                }
                if(cached==null)
                {
                    cached=Read(full,category);
                    lock(CacheGate)Cache[key]=new CacheEntry(file.Length,file.LastWriteTimeUtc.Ticks,cached);
                }
                // A user's selection/view must not mutate shared cached objects.
                rows.AddRange(cached.Select(Copy));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or FormatException or InvalidOperationException)
            {
                warnings.Add(category+": chưa đọc được Excel: "+ex.Message);
            }
        }
        return new Catalog(rows,warnings);
    }

    private static Row Copy(Row x)=>new()
    {
        Category=x.Category,Contract=x.Contract,Project=x.Project,Company=x.Company,
        Material=x.Material,Unit=x.Unit,Quantity=x.Quantity,Serial=x.Serial,Lot=x.Lot,
        ProductionDate=x.ProductionDate,Code=x.Code,Note=x.Note,Source=x.Source,
        Sheet=x.Sheet,SourceRow=x.SourceRow,Verification=x.Verification
    };

    private static string Txt(IXLWorksheet ws,int row,int col)
    {
        var cell=ws.Cell(row,col);
        return cell.IsEmpty()?"":cell.GetString().Trim();
    }

    private static decimal? Qty(IXLWorksheet ws,int row,int col)
    {
        var cell=ws.Cell(row,col);
        if(cell.IsEmpty())return null;
        if(cell.TryGetValue<decimal>(out var quantity))return quantity;
        var text=cell.GetString().Trim();
        return decimal.TryParse(text,NumberStyles.Number,
            CultureInfo.GetCultureInfo("vi-VN"),out quantity) ? quantity : null;
    }

    private static string ExcelDay(IXLWorksheet ws,int row,int col)
    {
        var cell=ws.Cell(row,col);
        if(cell.IsEmpty())return "";
        if(cell.TryGetValue<DateTime>(out var date))
            return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(cell.TryGetValue<double>(out var serial) && serial>40000 && serial<80000)
        {
            try{return DateTime.FromOADate(serial).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
            catch(ArgumentException){}
        }
        return cell.GetString().Trim();
    }

    private static bool LooksLikeContract(string value)
    {
        var code=Rules.Contract(value);
        // No placeholder, summary row or empty template may become an HĐ.
        return code.Length>=4 && code.Any(char.IsDigit) && !code.Contains("REF");
    }

    public static List<Row> Read(string path,string category)
    {
        if(category is not ("Tủ điện" or "Máy kéo" or "Đầu cửa car"))
            throw new ArgumentException("Danh sách thiết bị không hợp lệ.",nameof(category));
        using var wb=new XLWorkbook(path);
        var output=new List<Row>();
        var source=Path.GetFileName(path);
        var sheets=category=="Đầu cửa car" ? wb.Worksheets.ToList() :
            wb.Worksheets.Take(1).ToList();
        foreach(var sheet in sheets)
        {
            if(sheet.LastRowUsed() is not { } last)continue;
            int max=Math.Min(last.RowNumber(),50000);
            if(max<3)continue;
            if(category=="Tủ điện")
            {
                if(Rules.Code(Txt(sheet,2,4))!="SHD")continue;
                for(var i=3;i<=max;i++)
                {
                    var contract=Txt(sheet,i,4);
                    if(!LooksLikeContract(contract))continue;
                    var motor=Txt(sheet,i,7);
                    var panel=Txt(sheet,i,8);
                    output.Add(new Row{
                        Category=category,Contract=contract,Company=Txt(sheet,i,2),
                        Project=Txt(sheet,i,3),Material=panel.Length>0?panel:"Tủ điện · thông tin cấu hình",
                        Serial=Txt(sheet,i,5),ProductionDate="",
                        Note="Điện áp phanh: "+Txt(sheet,i,6)+
                             "; Loại máy kéo: "+motor+
                             "; Lịch: "+Txt(sheet,i,9),
                        Source=source,Sheet=sheet.Name
                    });
                }
            }
            else if(category=="Máy kéo")
            {
                if(Rules.Code(Txt(sheet,2,4))!="HD")continue;
                for(var i=3;i<=max;i++)
                {
                    var contract=Txt(sheet,i,4);
                    var material=Txt(sheet,i,5);
                    if(!LooksLikeContract(contract)||material.Length==0)continue;
                    output.Add(new Row{
                        Category=category,Contract=contract,Project=Txt(sheet,i,2),
                        Company=Txt(sheet,i,3),Material=material,
                        Unit=Txt(sheet,i,6),Quantity=Qty(sheet,i,7),
                        Lot=Txt(sheet,i,8),Serial=Txt(sheet,i,9),
                        Note="Thông tin cont đi / trạng thái ghi trong file: "+Txt(sheet,i,10),
                        Source=source,Sheet=sheet.Name,SourceRow=i
                    });
                }
            }
            else
            {
                // Only day-based production sheets with actual data headers.
                // Do not scan printing templates, totals or arbitrary old layouts.
                if(Rules.Code(Txt(sheet,2,5))!="SOHD" ||
                   !Rules.Code(Txt(sheet,2,6)).StartsWith("TENVATTU"))continue;
                for(var i=3;i<=max;i++)
                {
                    var contract=Txt(sheet,i,5);
                    var material=Txt(sheet,i,6);
                    if(!LooksLikeContract(contract)||material.Length==0)continue;
                    output.Add(new Row{
                        Category=category,Contract=contract,Project=Txt(sheet,i,3),
                        Company=Txt(sheet,i,4),Material=material,
                        Unit=Txt(sheet,i,9),Quantity=Qty(sheet,i,10),
                        Code=Txt(sheet,i,7),ProductionDate=ExcelDay(sheet,i,2),
                        Note="Mã kho: "+Txt(sheet,i,8),
                        Source=source,Sheet=sheet.Name
                    });
                }
            }
        }
        return output;
    }
}
