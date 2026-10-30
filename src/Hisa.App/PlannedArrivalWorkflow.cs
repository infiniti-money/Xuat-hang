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
    public string SourcePackageId { get; set; } = "";
    public string SourceContDay { get; set; } = "";
    public string MaterialSummary { get; set; } = "";
    public int PackageNo { get; set; }
    public int PackageCount { get; set; }
    public int MaterialCount { get; set; }
    public List<string> SourceIds { get; set; } = [];
    public string Key => Rules.Code(Container) + "|" + Rules.Identity(Contract, Project, Company) + "|" + SourcePackageId;
}

public static class PlannedArrivalWorkflow
{
    // Identity: contract + project markers (T1/T2/parentheses) + company.
    // Physical package: source ID, container and complete material/quantity signature.
    public static string MaterialFingerprint(Package package) =>
        string.Join(";", package.Items
            .Select(x => Rules.Code(x.Material) + "|" + Rules.Code(x.Unit) + "|" +
                (x.Quantity?.ToString("0.################", CultureInfo.InvariantCulture) ?? "?"))
            .Order(StringComparer.Ordinal));

    public static List<PlannedArrivalRow> Preview(AiScheduleDocument schedule,
        WorkbookSnapshot snapshot, string actualDay)
    {
        if (string.IsNullOrWhiteSpace(schedule.ApprovedAt))
            throw new InvalidOperationException("Lịch Gemini chưa được duyệt.");
        if (!DateTime.TryParseExact(actualDay, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _))
            throw new InvalidOperationException("Chọn ngày container thực tế về kho.");
        var rows=new List<PlannedArrivalRow>();
        var arrivals=schedule.Arrivals ?? [];
        var imported= snapshot.DetailPackages
            .Where(x=>x.OriginPackageId.Length>0)
            .Select(x=>x.OriginPackageId).ToHashSet(StringComparer.Ordinal);
        var duplicateSchedule=arrivals
            .GroupBy(x=>Rules.Identity(x.Contract,x.Project,x.Company))
            .Where(g=>g.Select(x=>Rules.Code(x.Container)).Distinct().Count()>1)
            .Select(g=>g.Key).ToHashSet();

        foreach(var ai in arrivals)
        {
            var identity=Rules.Identity(ai.Contract,ai.Project,ai.Company);
            var candidates=snapshot.ContPackages.Where(x=>x.Identity==identity).ToList();
            // If a contract appears in different containers, only a matching source
            // container number or distinct confirmed departure date is evidence.
            var competing=duplicateSchedule.Contains(identity);
            var candidatesForCont=candidates.Where(x =>
                x.ContainerNumber.Length>0
                    ? Rules.Code(x.ContainerNumber)==Rules.Code(ai.Container) &&
                      (ai.ContainerExportDate.Length==0 ||
                       x.SourceDate==ai.ContainerExportDate)
                    : ai.ContainerExportDate.Length>0 && x.SourceDate==ai.ContainerExportDate)
                .ToList();
            // Even a matching container number is not enough if this physical
            // container carried the contract on multiple factory departure days.
            var multipleDeparturesWithoutDate =
                ai.ContainerExportDate.Length==0 &&
                candidatesForCont.Select(x=>x.SourceDate).Distinct().Count()>1;
            var ambiguous=multipleDeparturesWithoutDate || (competing &&
                (candidatesForCont.Count==0 ||
                 candidatesForCont.Any(x=>x.SourceDate.Length==0 && x.ContainerNumber.Length==0) ||
                 arrivals.Where(x=>Rules.Identity(x.Contract,x.Project,x.Company)==identity)
                     .Any(x=>x!=ai && Rules.Code(x.Container)!=Rules.Code(ai.Container) &&
                         (x.ContainerExportDate==ai.ContainerExportDate || x.ContainerExportDate.Length==0))));
            // CRITICAL: even if this contract appears only once in the Zalo screenshot,
            // do not silently assign an unrelated Cont đi source package to that container.
            // The source must identify this exact container, or at least the verified
            // factory departure date when its physical container ID is not recorded.
            var unmatchedSource = candidates.Count>0 && candidatesForCont.Count==0;
            var matched = ambiguous || unmatchedSource
                ? new List<Package>() : candidatesForCont;
            if(matched.Count==0) matched=[new Package {
                Contract=ai.Contract,Project=ai.Project,Company=ai.Company}];

            foreach(var package in matched)
            {
                var row=new PlannedArrivalRow {
                    Container=ai.Container.Trim(),Contract=ai.Contract.Trim(),
                    Project=package.Id.Length>0 ? package.Project : ai.Project.Trim(),
                    Company=package.Id.Length>0 ? package.Company : ai.Company.Trim(),
                    PlannedDate=ai.PlannedDate, SourcePackageId=package.Id,
                    SourceContDay=package.SourceDate, PackageNo=package.PackageNo,
                    PackageCount=package.Id.Length>0?1:0,
                    MaterialCount=package.Items.Count,
                    MaterialSummary=string.Join(" · ",package.Items.Take(3).Select(x=>
                        x.Material+" × "+(x.Quantity?.ToString(CultureInfo.GetCultureInfo("vi-VN"))??"?"))),
                    SourceIds=package.Id.Length>0?[package.Id]:[]
                };
                if (Rules.Contract(row.Contract).Length==0 || row.Project.Length==0 ||
                    row.Company.Length==0 || row.Container.Length==0)
                {
                    row.Status="Thiếu dữ liệu nhận diện";
                    row.Notes="Cần số HĐ, công trình, công ty và container trước khi nhập.";
                }
                else if (ai.NeedsReview)
                {
                    row.Status="Cần duyệt lại từ ảnh";row.Notes=ai.ReviewNote;
                }
                else if (ambiguous)
                {
                    row.Status=multipleDeparturesWithoutDate
                        ? "Chưa xác minh ngày cont đi" : "Cùng HĐ ở nhiều container";
                    row.Notes="Có nhiều đợt hàng trùng HĐ hoặc cont. Cần đối chiếu đúng ngày cont đi và kiện nguồn trước khi nhập.";
                }
                else if (unmatchedSource)
                {
                    row.Status="Chưa xác minh kiện theo cont đi";
                    row.Notes="Hợp đồng có kiện nguồn nhưng không khớp số cont hoặc ngày cont đi. Không được nhập tất cả kiện theo số HĐ; hãy xác minh nguồn và cập nhật lịch.";
                }
                else if (package.Id.Length==0)
                {
                    row.Status="Chưa có kiện trong Tổng hợp";
                    row.Notes="Thiếu nguồn kiện đúng HĐ + công trình/T1/T2 + công ty. Không tự tạo vật tư.";
                }
                else if (package.Items==null || !package.Items.Any(x=>x.IsHungYen))
                {
                    row.Status="Chưa có vật tư nguồn";
                    row.Notes="Kiện chưa có dòng vật tư Hưng Yên hợp lệ; không được ghi nhận đã nhập kho.";
                }
                else if (!DateTime.TryParseExact(package.SourceDate,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                {
                    row.Status="Thiếu ngày cont đi";
                    row.Notes="Cần xác định ngày cont đi từ file nguồn; không được gộp kiện khác ngày.";
                }
                else if (imported.Contains(package.Id))
                {
                    row.Status="Đã nhập kiện này";
                    row.Notes="Kiện nguồn đã ghi vào Tổng hợp.xlsx (không ghi trùng).";
                }
                else if (snapshot.DetailPackages.Any(x=>x.OriginPackageId.Length==0 &&
                    x.Identity==package.Identity && x.ArrivalDate==actualDay &&
                    MaterialFingerprint(x)==MaterialFingerprint(package)))
                {
                    row.Status="Cần kiểm tra kiện nhập cũ";
                    row.Notes="Tổng hợp đã có kiện cùng vật tư/số lượng nhưng chưa có mã cont để xác minh.";
                }
                else
                {
                    row.Status="Đủ nguồn · chờ xác nhận đã về";
                    row.Notes="Kiện "+package.PackageNo+" · ngày cont đi "+
                        Rules.Day(package.SourceDate)+" · "+package.Items.Count+" dòng vật tư";
                }
                rows.Add(row);
            }
        }
        return rows;
    }

    public static List<Package> SelectVerified(IReadOnlyList<PlannedArrivalRow> selected,
        AiScheduleDocument approved, WorkbookSnapshot freshSnapshot, string actualDay)
    {
        if(selected.Count==0)throw new InvalidOperationException("Chọn ít nhất một kiện hàng thực tế đã về.");
        var latest=Preview(approved,freshSnapshot,actualDay);
        var verified=new List<Package>();
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in selected)
        {
            var matches=latest.Where(x=>x.Key==row.Key).ToList();
            if(matches.Count!=1 || matches[0].Status!="Đủ nguồn · chờ xác nhận đã về" ||
                matches[0].SourcePackageId.Length==0)
                throw new InvalidOperationException("Kiện HĐ "+row.Contract+" · cont "+row.Container+
                    " chưa được đối chiếu hoặc đã nhập. Làm mới rồi kiểm tra lại.");
            var fresh=matches[0];
            if(fresh.MaterialSummary!=row.MaterialSummary ||
                fresh.SourceContDay!=row.SourceContDay ||
                fresh.SourcePackageId!=row.SourcePackageId)
                throw new InvalidOperationException("Danh sách vật tư của kiện đã thay đổi. Đối chiếu lại.");
            if(!ids.Add(fresh.SourcePackageId))
                throw new InvalidOperationException("Một kiện nguồn đang bị chọn hai lần; chưa ghi kho.");
            var source=freshSnapshot.ContPackages.SingleOrDefault(x=>x.Id==fresh.SourcePackageId)
                ?? throw new InvalidOperationException("Không còn kiện nguồn.");
            // Clone to keep shared read-only snapshot and cache unchanged.
            var copy=WarehouseService.Clone(source);
            copy.ContainerNumber=fresh.Container;
            copy.OriginPackageId=source.Id;
            verified.Add(copy);
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
