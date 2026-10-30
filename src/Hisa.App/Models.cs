using System.ComponentModel;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hisa.App;

public static class Rules
{
    public static readonly string[] Categories = ["Đầu cửa tầng", "Đầu cửa car", "Máy kéo", "Tủ điện", "Ray dẫn hướng", "Bass nối ray", "Khác"];
    private static readonly ConcurrentDictionary<string,string> CodeCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string,string> ContractCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string,string> CleanCache = new(StringComparer.Ordinal);
    private static readonly Regex NonCode = new("[^A-Z0-9]",RegexOptions.Compiled);
    private static readonly Regex Marks = new(@"\p{Mn}",RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+",RegexOptions.Compiled);
    private static readonly Regex ProjectTower = new(@"\b(?:THANG|T)\s*[-_.]?\s*(\d+)\b",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    private static readonly Regex ProjectParenthesis = new(@"\(([^)]*)\)",RegexOptions.Compiled);
    private static readonly Regex BgPrefix = new(@"^BG[\s._:/-]*(?=\d)",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    public static string Norm(string? text) => Marks.Replace((text ?? "").Replace('đ','d').Replace('Đ','D').Normalize(NormalizationForm.FormD), "").ToUpperInvariant();
    public static string Code(string? text)
    {
        if(CodeCache.TryGetValue(text??"",out var cached))return cached;
        if(CodeCache.Count>100000)CodeCache.Clear();
        return CodeCache.GetOrAdd(text??"",x=>NonCode.Replace(Norm(x),""));
    }
    public static string CleanContract(string? text)
    {
        if(CleanCache.TryGetValue(text??"",out var cached))return cached;
        if(CleanCache.Count>20000)CleanCache.Clear();
        return CleanCache.GetOrAdd(text??"",x=>Spaces.Replace(BgPrefix.Replace(x.Trim(),""),""));
    }
    public static string Contract(string? text)
    {
        if(ContractCache.TryGetValue(text??"",out var cached))return cached;
        if(ContractCache.Count>20000)ContractCache.Clear();
        return ContractCache.GetOrAdd(text??"",ContractCore);
    }
    private static string ContractCore(string text)
    {
        text = CleanContract(text);
        var suffix = Regex.Match(text, @"^(.+)\((\d+)\)$");
        if (suffix.Success) return Contract(suffix.Groups[1].Value) + "(" + int.Parse(suffix.Groups[2].Value) + ")";
        if (Regex.IsMatch(text, @"^\d+(?:\.\d+)?$") && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n)) return n.ToString("0.############################", CultureInfo.InvariantCulture);
        return Code(text);
    }
    // Project labels from Cont and KT often add a location after " - ". Treat
    // those as the same project, while keeping explicit sub-project markers
    // (parentheses and Thang 1 / T1) distinct.
    public static string ProjectCode(string? project)
    {
        var text=(project??"").Trim();
        var parenthetical=ProjectParenthesis.Matches(text).Select(m=>m.Groups[1].Value.Trim())
            .Select(x=>Regex.Match(Norm(x),@"^(?:THANG|T)\s*[-_.]?\s*(\d+)$") is { Success:true } tower ? "T"+tower.Groups[1].Value.TrimStart('0') : Code(x))
            .Select(x=>x=="T"?"T0":x).Where(x=>x.Length>0).Order(StringComparer.Ordinal).ToArray();
        var towerIds=ProjectTower.Matches(Norm(text)).Select(m=>m.Groups[1].Value.TrimStart('0'))
            .Select(x=>x.Length==0?"0":x).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        text=ProjectParenthesis.Replace(text," ");
        text=ProjectTower.Replace(text," ");
        var baseName=Regex.Split(text,@"\s+[-–—]\s+")[0];
        var baseCode=Code(baseName);
        return baseCode+"|P:"+string.Join(",",parenthetical)+"|T:"+string.Join(",",towerIds);
    }
    public static string Identity(string? contract, string? project = "", string? company = "") => Contract(contract) + "|" + ProjectCode(project) + "|" + Code(company);
    public static List<string> Tokens(string? text) => Regex.Split(text ?? "", @"[,;\r\n]+").Select(CleanContract).Where(x => x.Length > 0).DistinctBy(Contract).ToList();
    public static string Classify(string? material)
    {
        var m = Code(material);
        if (m.Contains("DAUCUATANG")) return Categories[0];
        if (m.Contains("DAUCUACAR") || m.Contains("DAUCUACABIN")) return Categories[1];
        if (m.Contains("MAYKEO")) return Categories[2];
        if (m.Contains("TUDIEN")) return Categories[3];
        if (m.Contains("BASSNOI") || m.Contains("BASNOI") || m.Contains("BATNOIRAY")) return Categories[5];
        if (m.Contains("RAYDANHUONG") || m.StartsWith("RAY")) return Categories[4];
        return Categories[6];
    }
    public static bool MaterialMatches(MaterialLine item, IEnumerable<string>? categories, IEnumerable<string>? keywords)
    {
        var cats = categories?.ToList() ?? []; var keys = keywords?.Where(x=>Code(x).Length>0).ToList() ?? [];
        return cats.Count == 0 && keys.Count == 0 || cats.Contains(Classify(item.Material)) || keys.Any(k=>Code(item.Material).Contains(Code(k)));
    }
    public static string StatusLabel(string? status) => status switch { "delivered" => "Đã xuất", "delayed" => "Tạm hoãn", "cancelled" => "Đã hủy", "cancel_pickup" => "Hủy lấy hàng", _ => "Chờ xuất" };
    public static string Day(string? day) => DateTime.TryParse(day, out var date) ? date.ToString("dd/MM/yyyy") : "—";
    public static bool SameProject(string contract, string? project, string? company, string otherContract, string? otherProject, string? otherCompany) => Contract(contract)==Contract(otherContract) && (string.IsNullOrEmpty(project)||string.IsNullOrEmpty(otherProject)||ProjectCode(project)==ProjectCode(otherProject)) && (string.IsNullOrEmpty(company)||string.IsNullOrEmpty(otherCompany)||Code(company)==Code(otherCompany));
}

public class AppSettings
{
    public List<string> DataSources { get; set; } = [];
    public string MasterPath { get; set; } = "";
    public string KtPath { get; set; } = "";
    public string ProductExportPath { get; set; } = "";
    public bool AutoRefresh { get; set; } = true;
    public bool AutoSyncCont { get; set; } = true;
    public bool AutoSyncKt { get; set; } = true;
    public int PollSeconds { get; set; } = 10;
}
public class MaterialLine
{
    public MaterialLine Copy() => (MaterialLine)MemberwiseClone();
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Material { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string Category { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Include { get; set; } = true;
    public bool IsHungYen { get; set; } = true;
    public string PackageId { get; set; } = "";
    public string Package { get; set; } = "HƯNG YÊN";
    public int PackageNo { get; set; }
    public string SourceDate { get; set; } = "";
    public string ArrivalDate { get; set; } = "";
    public string SourceFile { get; set; } = "";
    [JsonIgnore] public string Group => string.IsNullOrEmpty(Category) ? Rules.Classify(Material) : Category;
}
public class Package
{
    public string Id { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string SourceDate { get; set; } = "";
    public string ArrivalDate { get; set; } = "";
    public string ContDate { get; set; } = "";
    // Explicit physical container and source package identity, persisted in hidden
    // detail-sheet metadata so equal contracts/quantities in different containers stay separate.
    public string ContainerNumber { get; set; } = "";
    public string OriginPackageId { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string SourceSheet { get; set; } = "";
    public int SourceRowStart { get; set; }
    public int SourceRowEnd { get; set; }
    public int PackageNo { get; set; }
    public List<MaterialLine> Items { get; set; } = [];
    [JsonIgnore] public int ItemCount => Items.Count;
    [JsonIgnore] public string Identity => Rules.Identity(Contract, Project, Company);
    [JsonIgnore] public string ArrivalLabel => Rules.Day(ArrivalDate);
    [JsonIgnore] public string ContLabel => Rules.Day(ContDate.Length>0?ContDate:SourceDate);
}
public class Slip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SlipCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "Phiếu bàn giao vật tư";
    public string HandoverDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Status { get; set; } = "planned";
    public string TemplateId { get; set; } = "internal_summary";
    public string Note { get; set; } = "";
    public List<string> Contracts { get; set; } = [];
    // Selected contracts without source materials are tracked as shipped contracts,
    // without inventing a stock item or quantity.
    public List<ContractRow> ContractOnly { get; set; } = [];
    public List<MaterialLine> Lines { get; set; } = [];
    public List<MotorSerialEntry> MotorSerials { get; set; } = [];
    // Set only after the user explicitly confirms export with missing motor serials.
    public bool AllowMissingMotorSerials { get; set; } = false;
    public List<Package> ContractPackages { get; set; } = [];
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string DeliveredAt { get; set; } = "";
    public string DelayedAt { get; set; } = "";
    public string CancelledAt { get; set; } = "";
    public string DelayReason { get; set; } = "";
    public string CancelReason { get; set; } = "";
    public string PreviousStatus { get; set; } = "planned";
    public bool ProductExportPending { get; set; }
    public List<StatusEvent> StatusHistory { get; set; } = [];
    [JsonIgnore] public string StatusLabel => Rules.StatusLabel(Status);
    [JsonIgnore] public string DateLabel => Rules.Day(HandoverDate);
    [JsonIgnore] public string ContractLabel => string.Join(", ",Status=="delivered"&&Lines.Count>0?Lines.Where(x=>x.Include).Select(x=>x.Contract).Concat(ContractOnly.Select(x=>x.Contract)).Where(x=>!string.IsNullOrWhiteSpace(x)).DistinctBy(Rules.Contract):Contracts);
    [JsonIgnore] public int LineCount => Lines.Count(x=>x.Include);
}
public class StatusEvent
{
    public string At { get; set; } = DateTime.Now.ToString("O");
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
}
public class ContractFlag
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Status { get; set; } = "delayed";
    public string Note { get; set; } = "";
    public string SlipId { get; set; } = "";
    public string UpdatedAt { get; set; } = DateTime.Now.ToString("O");
    [JsonIgnore] public string StatusLabel => Rules.StatusLabel(Status);
}
public class PrintTemplate
{
    public string Id { get; set; } = "internal_summary";
    public string Name { get; set; } = "Biên bản bàn giao nội bộ";
    public string Layout { get; set; } = "internal_summary";
    public string Company { get; set; } = "CÔNG TY TNHH THANG MÁY VÀ KỸ THUẬT ĐIỆN HISA";
    public string Branch { get; set; } = "KHO HƯNG YÊN";
    public string Title { get; set; } = "BIÊN BẢN BÀN GIAO NỘI BỘ";
    public string Subtitle { get; set; } = "";
    public string Note { get; set; } = "Hai bên kiểm tra và xác nhận vật tư bàn giao.";
    public string CodePrefix { get; set; } = "BBBG-HY";
    public string CodeLabel { get; set; } = "Mã phiếu";
    public string DateLabel { get; set; } = "Ngày xuất";
    public string ProjectLabel { get; set; } = "Công trình";
    public string CompanyLabel { get; set; } = "Công ty";
    public string ContractLabel { get; set; } = "Số hợp đồng";
    public string MaterialLabel { get; set; } = "Tên vật tư";
    public string UnitLabel { get; set; } = "ĐVT";
    public string QuantityLabel { get; set; } = "Số lượng";
    public string NotesLabel { get; set; } = "Ghi chú";
    public string CategoryPrefix { get; set; } = "TỔNG HỢP";
    public string CategorySuffix { get; set; } = "BÀN GIAO";
    public string SignatureCaption { get; set; } = "(Ký, ghi rõ họ tên)";
    public string FooterText { get; set; } = "";
    public string Location { get; set; } = "Kho Hisa Hưng Yên";
    public string OpeningText { get; set; } = "Xuất theo danh mục vật tư (List hàng)";
    public string SupplementaryLabel { get; set; } = "Danh sách vật tư bàn giao";
    public string AccountingQtyLabel { get; set; } = "Kế toán";
    public string ActualQtyLabel { get; set; } = "Thực tế";
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "landscape";
    public string FontFamily { get; set; } = "Times New Roman";
    public double FontSize { get; set; } = 10;
    public double TableFontSize { get; set; } = 9;
    public double TitleFontSize { get; set; } = 19;
    public double MarginTop { get; set; } = 12;
    public double MarginBottom { get; set; } = 12;
    public double MarginLeft { get; set; } = 10;
    public double MarginRight { get; set; } = 10;
    public string CompanyAlign { get; set; } = "center";
    public string TitleAlign { get; set; } = "center";
    public string TableAlign { get; set; } = "left";
    public string MetaAlign { get; set; } = "left";
    public string SignatureAlign { get; set; } = "center";
    public List<string> Signatures { get; set; } = ["BÊN NHẬN", "BÊN BÀN GIAO", "THỦ KHO"];
    public int MinRows { get; set; } = 10;
    public bool ShowCategory { get; set; } = true;
    public bool ShowUnit { get; set; } = true;
    public bool ShowQuantity { get; set; } = true;
    public bool ShowSource { get; set; }
    public bool IncludeOther { get; set; }
}
public class WorkbookSnapshot
{
    public List<Package> Packages { get; set; } = [];
    public List<Package> ContPackages { get; set; } = [];
    public List<Package> DetailPackages { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public DateTime LoadedAt { get; set; } = DateTime.Now;
}
public class SourceScanResult
{
    public List<Package> Packages { get; set; } = [];
    public List<ContFileOrganizer.Candidate> HangGuiFiles { get; set; } = [];
    public List<ContFileOrganizer.Candidate> ImportedHungYenFiles { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public int FileCount { get; set; }
    public int ChangedFiles { get; set; }
}
public class ComparisonRow
{
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Status { get; set; } = "";
    public string Note { get; set; } = "";
    public int ContItems { get; set; }
    public int KtItems { get; set; }
    public List<MaterialLine> ContOnly { get; set; } = [];
    public List<MaterialLine> KtOnly { get; set; } = [];
    [JsonIgnore] public string StatusLabel => Status switch { "matched"=>"Khớp", "mismatch"=>"Lệch vật tư / số lượng", "not_found"=>"Không có trong KT", _=>"Chưa đối chiếu" };
}
public class RecordArrivalResult
{
    public int Contracts { get; set; }
    public int Packages { get; set; }
    public int Lines { get; set; }
    public List<string> Warnings { get; set; } = [];
}
public class ShipmentRecord
{
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Date { get; set; } = "";
    public string SlipId { get; set; } = "";
    public string SlipCode { get; set; } = "";
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
}
public class SearchFilter
{
    public string ContractsText { get; set; } = "";
    public string Query { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public string KeywordsText { get; set; } = "";
    public string ArrivalDate { get; set; } = "";
    public string ShipDate { get; set; } = "";
    public List<ContractRow> ProjectTargets { get; set; } = [];
}
public class ContractRow
{
    public bool Selected { get; set; } = true;
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public List<Package> Packages { get; set; } = [];
    public List<MaterialLine> Materials { get; set; } = [];
    public List<MaterialLine> AllMaterials { get; set; } = [];
    public List<Slip> Slips { get; set; } = [];
    public List<ContractFlag> Flags { get; set; } = [];
    public List<ShipmentRecord> Shipments { get; set; } = [];
    public string Warning { get; set; } = "";
    [JsonIgnore] public string Identity => Rules.Identity(Contract,Project,Company);
    [JsonIgnore] public int PackageCount => Packages.Count;
    [JsonIgnore] public int ItemCount => Materials.Count;
    [JsonIgnore] public string ArrivalDates => string.Join(", ",Packages.Select(x=>x.ArrivalDate).Where(x=>x.Length>0).Distinct().Order().Select(Rules.Day));
    [JsonIgnore] public string StatusLabel => string.Join(" · ",Slips.Select(x=>x.StatusLabel).Concat(Flags.Select(x=>x.StatusLabel)).Concat(Shipments.Count>0?["Đã xuất theo sổ"]:[]).Distinct());
}
public class CategoryStat
{
    public string Category { get; set; } = "";
    public int ContractCount { get; set; }
    public int ProjectCount { get; set; }
    public int LineCount { get; set; }
    public Dictionary<string,decimal> Units { get; set; } = [];
    public List<ContractRow> Contracts { get; set; } = [];
    [JsonIgnore] public string QuantityLabel => string.Join(" · ",Units.Select(x=>x.Value.ToString("#,0.00##########################",CultureInfo.GetCultureInfo("vi-VN"))+" "+x.Key));
}
public class ContractReport
{
    public string Title { get; set; } = "BÁO CÁO HỢP ĐỒNG VÀ VẬT TƯ HƯNG YÊN";
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public SearchFilter Filter { get; set; } = new();
    public int RequestedCount { get; set; }
    public int ContractCount { get; set; }
    public int ProjectCount { get; set; }
    public int PackageCount { get; set; }
    public int LineCount { get; set; }
    public int WithSlips { get; set; }
    public int CancelledCount { get; set; }
    public bool DataAvailable { get; set; } = true;
    public List<ContractRow> Rows { get; set; } = [];
    public List<ContractRow> Missing { get; set; } = [];
    public List<ContractRow> NoMaterialMatch { get; set; } = [];
    public List<ContractRow> DateMismatch { get; set; } = [];
    public List<CategoryStat> Categories { get; set; } = [];
    public Dictionary<string,List<ContractRow>> StatusDetails { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    [JsonIgnore] public string Summary => $"{ContractCount:N0} số HĐ · {ProjectCount:N0} công trình · {PackageCount:N0} kiện · {LineCount:N0} dòng vật tư · {Missing.Count:N0} mã chưa có dữ liệu";
}
public class DailyReport
{
    public string Date { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public ContractReport Contracts { get; set; } = new();
    public List<Slip> Slips { get; set; } = [];
    public List<Slip> OutstandingDelayed { get; set; } = [];
    public List<ShipmentRecord> LegacyDeliveries { get; set; } = [];
    public List<CategoryStat> Categories { get; set; } = [];
    public Dictionary<string,int> StatusCounts { get; set; } = [];
}
public class OperationProgress
{
    public string Message { get; set; } = "";
    public int Current { get; set; }
    public int Total { get; set; }
}
