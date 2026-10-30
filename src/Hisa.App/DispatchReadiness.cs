using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Hisa.App;

// Lịch xuất dự kiến và tình trạng chuẩn bị là dữ liệu độc lập với phiếu bàn giao.
// File này KHÔNG thay đổi workbook, tồn kho, serial đã xuất hay trạng thái "Đã xuất".
public sealed class DispatchReadinessState
{
    public List<DispatchPlanItem> Plans { get; set; } = [];
    public List<PreparedMaterialRecord> Prepared { get; set; } = [];
}
public sealed class DispatchPlanItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Day { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Source { get; set; } = "manual";
    public string Destination { get; set; } = "Kho thành phẩm";
    public string Reason { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Urgent { get; set; }
    public bool NeedsReview { get; set; }
    public string SourceLabel => Source == "ai" ? "Ảnh Zalo đã duyệt" : "Nhập tay";
}
public sealed class PreparedMaterialRecord
{
    public string MaterialKey { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Serials { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
public sealed class DispatchMaterialStatus
{
    public string MaterialKey { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Material { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Group { get; set; } = "";
    public decimal? Required { get; set; }
    public decimal? Prepared { get; set; }
    public string Serials { get; set; } = "";
    public string Status { get; set; } = "";
    public string RequiredLabel => Required?.ToString("0.##", CultureInfo.GetCultureInfo("vi-VN")) ?? "Chưa rõ";
    public string PreparedLabel => Prepared?.ToString("0.##", CultureInfo.GetCultureInfo("vi-VN")) ?? "Chưa cập nhật";
}
public sealed class DispatchPlanStatus
{
    public DispatchPlanItem Plan { get; set; } = new();
    public List<DispatchMaterialStatus> Materials { get; set; } = [];
    public string Status { get; set; } = "";
    public string Contract => Plan.Contract;
    public string Project => Plan.Project;
    public string Destination => Plan.Destination;
    public string Origin => Plan.SourceLabel;
    public string Reason => Plan.Reason;
    public string Note => Plan.Note;
    public string Priority => Plan.Urgent ? "KHẨN" : "";
    public string Review => Plan.NeedsReview ? "Cần duyệt lại" : "";
    public int MaterialCount => Materials.Count;
}
public sealed class DispatchReadinessStore
{
    private readonly string _path;
    public DispatchReadinessStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "dispatch_readiness.json");
    }
    public DispatchReadinessState Load()
    {
        if (!File.Exists(_path)) return new DispatchReadinessState();
        var state = JsonSerializer.Deserialize<DispatchReadinessState>(File.ReadAllText(_path), WarehouseService.Json)
                    ?? new DispatchReadinessState();
        state.Plans ??= [];
        state.Prepared ??= [];
        return state;
    }
    public void Save(DispatchReadinessState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions(WarehouseService.Json) { WriteIndented = true }));
            File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string RequireDay(string day)
    {
        if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new InvalidOperationException("Ngày phải có định dạng yyyy-MM-dd.");
        return day;
    }
    public static DispatchPlanItem AddManual(DispatchReadinessState state, string day, string contract,
        string project, string company, string destination, string reason, string note)
    {
        RequireDay(day);
        contract = Rules.CleanContract(contract);
        if (Rules.Contract(contract).Length == 0)
            throw new InvalidOperationException("Nhập số hợp đồng phát sinh.");
        project = project.Trim();
        company = company.Trim();
        var existing = state.Plans.FirstOrDefault(p => p.Day == day &&
            Rules.Identity(p.Contract, p.Project, p.Company) == Rules.Identity(contract, project, company) &&
            p.Destination == destination);
        if (existing != null)
        {
            existing.Reason = reason.Trim();
            existing.Note = note.Trim();
            return existing;
        }
        var item = new DispatchPlanItem
        {
            Day = day, Contract = contract, Project = project, Company = company,
            Destination = destination, Reason = reason.Trim(), Note = note.Trim()
        };
        state.Plans.Add(item);
        return item;
    }
    // Chỉ nhập PHẦN XUẤT của một lịch đã được người sử dụng duyệt.
    // Không lấy Arrivals, không đổi ngày cont về và không ghi sổ.
    public static int ImportApproved(DispatchReadinessState state, AiScheduleDocument document, string day)
    {
        RequireDay(day);
        if (string.IsNullOrWhiteSpace(document.ApprovedAt) || document.DispatchDate != day)
            throw new InvalidOperationException("Lịch AI chưa duyệt hoặc ngày xuất không khớp ngày đang chọn.");
        var dispatches = document.Dispatches.Where(x => Rules.Contract(x.Contract).Length > 0).ToList();
        state.Plans.RemoveAll(p => p.Source == "ai" && p.Day == day);
        int added = 0;
        foreach (var row in dispatches)
        {
            if (state.Plans.Any(p => p.Day == day &&
                Rules.Identity(p.Contract, p.Project, p.Company) == Rules.Identity(row.Contract, row.Project, row.Company)))
                continue;
            state.Plans.Add(new DispatchPlanItem
            {
                Day = day, Contract = row.Contract.Trim(), Project = row.Project.Trim(),
                Company = row.Company.Trim(), Source = "ai", Note = row.Note,
                Urgent = row.Urgent, NeedsReview = row.NeedsReview,
                Destination = "Kho thành phẩm"
            });
            added++;
        }
        return added;
    }
    public static string MaterialKey(string contract, string project, string company, string material, string unit) =>
        Rules.Identity(contract, project, company) + "|" + Rules.Code(material) + "|" + Rules.Code(unit);

    public static void UpdatePrepared(DispatchReadinessState state, DispatchMaterialStatus row,
        decimal quantity, string serials)
    {
        if (quantity < 0) throw new InvalidOperationException("Số lượng đã chuẩn bị không được âm.");
        if (row.Required == null) throw new InvalidOperationException("Chưa có số lượng yêu cầu để đối chiếu.");
        if (quantity > row.Required)
            throw new InvalidOperationException("Số lượng chuẩn bị lớn hơn số lượng yêu cầu. Hãy kiểm tra lại kiện và quy cách.");
        var values = SplitSerials(serials);
        if (values.Count != values.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidOperationException("Serial bị trùng trong danh sách.");
        if (row.Group == "Máy kéo" && values.Count > 0 && values.Count != quantity)
            throw new InvalidOperationException("Số serial phải bằng số máy kéo đã xác nhận chuẩn bị.");
        var entry = state.Prepared.FirstOrDefault(x => x.MaterialKey == row.MaterialKey);
        if (entry == null) { entry = new PreparedMaterialRecord { MaterialKey = row.MaterialKey }; state.Prepared.Add(entry); }
        entry.Quantity = quantity;
        entry.Serials = serials.Trim();
        entry.UpdatedAt = DateTimeOffset.Now.ToString("O");
    }
    private static List<string> SplitSerials(string s) => s.Split(['\r','\n',';',','],
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    public static List<DispatchPlanStatus> Evaluate(DispatchReadinessState state, WorkbookSnapshot snapshot, string day)
    {
        var output = new List<DispatchPlanStatus>();
        foreach (var plan in state.Plans.Where(x => x.Day == day))
        {
            var result = new DispatchPlanStatus { Plan = plan };
            var matches = snapshot.Packages.Where(p => Rules.Contract(p.Contract) == Rules.Contract(plan.Contract) &&
                (string.IsNullOrWhiteSpace(plan.Project) || Rules.ProjectCode(p.Project) == Rules.ProjectCode(plan.Project)) &&
                (string.IsNullOrWhiteSpace(plan.Company) || Rules.Code(p.Company) == Rules.Code(plan.Company))).ToList();
            var identities = matches.Select(p => p.Identity).Distinct(StringComparer.Ordinal).ToList();
            if (matches.Count == 0)
            {
                result.Status = "Chưa có dữ liệu kiện Hưng Yên";
                output.Add(result); continue;
            }
            if (identities.Count > 1)
            {
                result.Status = "Cần xác định công trình / đơn vị";
                output.Add(result); continue;
            }
            var items = matches.SelectMany(p => p.Items)
                .Where(m => m.IsHungYen)
                .GroupBy(m => MaterialKey(m.Contract, m.Project, m.Company, m.Material, m.Unit));
            foreach (var group in items)
            {
                var first = group.First();
                var required = group.All(x => x.Quantity.HasValue)
                    ? group.Sum(x => x.Quantity!.Value) : (decimal?)null;
                var prepared = state.Prepared.FirstOrDefault(x => x.MaterialKey == group.Key);
                var status = prepared == null ? "Chưa cập nhật" :
                    required == null ? "Chưa rõ số lượng nguồn" :
                    prepared.Quantity < required ? "Thiếu vật tư" :
                    first.Group == "Máy kéo" && SplitSerials(prepared.Serials).Count != required ? "Cần kiểm tra serial" :
                    "Đã chuẩn bị · chờ bàn giao";
                result.Materials.Add(new DispatchMaterialStatus
                {
                    MaterialKey = group.Key, Contract = first.Contract, Project = first.Project,
                    Material = first.Material, Unit = first.Unit, Group = first.Group,
                    Required = required, Prepared = prepared?.Quantity, Serials = prepared?.Serials ?? "",
                    Status = status
                });
            }
            result.Status = result.Materials.Count == 0 ? "Chưa có dữ liệu vật tư" :
                result.Materials.All(x => x.Status == "Đã chuẩn bị · chờ bàn giao") && !plan.NeedsReview ?
                    (plan.Destination == "Xuất trực tiếp khách" ? "Sẵn sàng xuất trực tiếp" : "Sẵn sàng bàn giao thành phẩm") :
                result.Materials.Any(x => x.Status == "Thiếu vật tư") ? "Thiếu vật tư" :
                result.Materials.Any(x => x.Status == "Cần kiểm tra serial") ? "Cần kiểm tra serial" :
                "Chưa cập nhật đầy đủ";
            output.Add(result);
        }
        return output;
    }
    public AiScheduleDocument? LatestApprovedAi(string day)
    {
        RequireDay(day);
        var folder = Path.Combine(Path.GetDirectoryName(_path)!, "ai_schedules");
        if (!Directory.Exists(folder)) return null;
        foreach (var file in Directory.EnumerateFiles(folder, "lich-kho-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<AiScheduleDocument>(File.ReadAllText(file), WarehouseService.Json);
                if (doc?.ApprovedAt?.Length > 0 && doc.DispatchDate == day) return doc;
            }
            catch (JsonException) { /* Ignore incomplete historical file; never import unapproved rows. */ }
            catch (IOException) { /* A temporary file may be locked: other approved files remain usable. */ }
        }
        return null;
    }
}
