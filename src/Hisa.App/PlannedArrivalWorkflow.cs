using System.IO;
using System.Globalization;

namespace Hisa.App;

/// <summary>
/// Safe approval gate between an AI-predicted container arrival and real warehouse Excel.
/// A schedule entry is NEVER evidence the goods arrived. Only a manually selected,
/// unambiguous source package from "Cont đi Hưng Yên" can be recorded.
/// </summary>
public sealed class PlannedArrivalRow
{
    public bool Selected { get; set; }
    public string Container { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string PlannedDate { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public int PackageCount { get; set; }
    public int MaterialCount { get; set; }
    public List<string> SourceIds { get; set; } = [];
    public string Key => Container + "|" + Rules.Identity(Contract, Project, Company);
}

public static class PlannedArrivalWorkflow
{
    public static List<PlannedArrivalRow> Preview(AiScheduleDocument schedule,
        WorkbookSnapshot snapshot, string actualDay)
    {
        if (string.IsNullOrWhiteSpace(schedule.ApprovedAt))
            throw new InvalidOperationException("Lịch AI chưa được duyệt. Vui lòng duyệt và lưu lịch trước.");
        if (!DateTime.TryParseExact(actualDay, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _))
            throw new InvalidOperationException("Chọn ngày container thực tế về kho.");

        // No assumptions based on planned dates, only actual source packages and explicit user selection.
        var existing = snapshot.DetailPackages
            .Where(x => x.ArrivalDate == actualDay).ToList();
        var rows = new List<PlannedArrivalRow>();
        var arrivals = schedule.Arrivals ?? [];
        var collisions = arrivals
            .Where(x => Rules.Contract(x.Contract).Length > 0)
            .GroupBy(x => Rules.Identity(x.Contract, x.Project, x.Company))
            .Where(g => g.Select(x => Rules.Code(x.Container)).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet();

        foreach (var ai in arrivals)
        {
            var row = new PlannedArrivalRow
            {
                Container = ai.Container.Trim(),
                Contract = ai.Contract.Trim(),
                Project = ai.Project.Trim(),
                Company = ai.Company.Trim(),
                PlannedDate = ai.PlannedDate,
                Status = "Chưa đối chiếu"
            };
            var identity = Rules.Identity(row.Contract, row.Project, row.Company);
            var sources = snapshot.ContPackages
                .Where(x => x.Identity == identity)
                .ToList();
            row.SourceIds = sources.Select(x => x.Id).Distinct().ToList();
            row.PackageCount = sources.Count;
            row.MaterialCount = sources.Sum(x => x.Items.Count);

            if (Rules.Contract(row.Contract).Length == 0 ||
                row.Project.Length == 0 || row.Company.Length == 0 ||
                row.Container.Length == 0)
            {
                row.Status = "Thiếu dữ liệu nhận diện";
                row.Notes = "Bổ sung đúng số HĐ, công trình, công ty và container trong lịch AI.";
            }
            else if (ai.NeedsReview)
            {
                row.Status = "Cần duyệt lại từ ảnh";
                row.Notes = ai.ReviewNote;
            }
            else if (collisions.Contains(identity))
            {
                row.Status = "Cùng HĐ ở nhiều container";
                row.Notes = "Không tự phân biệt kiện theo container khi dữ liệu nguồn chưa có mã cont.";
            }
            else if (sources.Count == 0)
            {
                row.Status = "Chưa có kiện trong Tổng hợp";
                row.Notes = "Cần nhập kiện Hưng Yên từ file cont nguồn trước. Không tạo vật tư giả.";
            }
            else if (sources.Any(x => x.Id.Length == 0))
            {
                row.Status = "Kiện nguồn không có ID";
                row.Notes = "Làm mới dữ liệu Excel trước khi nhập.";
            }
            else if (existing.Any(x => x.Identity == identity))
            {
                row.Status = "Đã có hàng nhập ngày này";
                row.Notes = "Đã tồn tại kiện cùng HĐ/công trình/công ty trong chi tiết. Không ghi trùng.";
            }
            else
            {
                row.Status = "Đủ nguồn · chờ xác nhận đã về";
                row.Notes = sources.Count + " kiện · " + row.MaterialCount + " dòng vật tư từ Cont đi Hưng Yên";
            }
            rows.Add(row);
        }
        return rows;
    }

    public static List<Package> SelectVerified(IReadOnlyList<PlannedArrivalRow> selected,
        AiScheduleDocument approved, WorkbookSnapshot freshSnapshot, string actualDay)
    {
        if (selected.Count == 0)
            throw new InvalidOperationException("Chọn ít nhất một container thực tế đã về kho.");
        var latest = Preview(approved, freshSnapshot, actualDay);
        var verified = new List<Package>();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in selected)
        {
            var matches = latest.Where(x => x.Key == row.Key).ToList();
            if (matches.Count != 1 || matches[0].Status != "Đủ nguồn · chờ xác nhận đã về")
                throw new InvalidOperationException("HĐ " + row.Contract + " · container " + row.Container +
                    " chưa đủ điều kiện nhập. Kiểm tra hợp đồng/công trình/kiện nguồn hoặc trạng thái đã nhập.");
            if (!matches[0].SourceIds.Order().SequenceEqual(row.SourceIds.Order()))
                throw new InvalidOperationException("Nguồn Excel đã thay đổi; tải lại và kiểm tra kế hoạch trước khi nhập.");
            foreach (var id in matches[0].SourceIds)
            {
                if (!sourceIds.Add(id))
                    throw new InvalidOperationException("Một kiện nguồn đang được chọn ở nhiều container. Chưa ghi dữ liệu.");
                var package = freshSnapshot.ContPackages.SingleOrDefault(x => x.Id == id);
                if (package is null) throw new InvalidOperationException("Không tìm thấy kiện nguồn. Cập nhật dữ liệu lại.");
                verified.Add(package);
            }
        }
        return verified;
    }

    public static AiScheduleDocument? LoadLatestApproved(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, "ai_schedules");
        if (!Directory.Exists(folder)) return null;
        foreach (var file in Directory.EnumerateFiles(folder, "lich-kho-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var doc = System.Text.Json.JsonSerializer.Deserialize<AiScheduleDocument>(
                    File.ReadAllText(file), WarehouseService.Json);
                if (doc is { Arrivals.Count: > 0 } &&
                    !string.IsNullOrEmpty(doc.ApprovedAt)) return doc;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { /* Ignore a transient/invalid historical schedule, never guess its contents. */ }
        }
        return null;
    }
}
