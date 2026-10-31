using System.Globalization;

namespace Hisa.App;

/// <summary>
/// Exact contract search against the currently scanned CONT ĐI HƯNG YÊN
/// warehouse source, shared by Zalo photos and manually entered contract codes.
/// The user selects physical source packages, not whole contract numbers.
/// </summary>
public static class ContDiArrivalLookup
{
    public const string Ready="Đủ nguồn · chờ xác nhận đã về";

    public static List<AiScheduleLine> FromContracts(string text) =>
        Rules.Tokens(text).Select(code=>new AiScheduleLine
        {
            Contract=code,ManualEntry=true
        }).ToList();

    public static List<PlannedArrivalRow> Preview(
        IEnumerable<AiScheduleLine> requests,WorkbookSnapshot snapshot,string actualDay)
    {
        if(!DateTime.TryParseExact(actualDay,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            throw new InvalidOperationException("Chọn ngày cont thực tế về kho.");
        var imported=snapshot.DetailPackages.Where(x=>x.OriginPackageId.Length>0)
            .Select(x=>x.OriginPackageId).ToHashSet(StringComparer.Ordinal);
        var results=new Dictionary<string,PlannedArrivalRow>(StringComparer.Ordinal);
        foreach(var request in requests.Where(x=>Rules.Contract(x.Contract).Length>0))
        {
            var code=Rules.Contract(request.Contract);
            // Exact contract match, never prefix/fuzzy search. A contract can have
            // separate T1/T2, containers, departure days and physical packages.
            var packages=snapshot.ContPackages
                .Where(x=>Rules.Contract(x.Contract)==code && x.Id.Length>0)
                .ToList();
            if(packages.Count==0)
            {
                results.TryAdd("missing|"+code,new PlannedArrivalRow
                {
                    Contract=request.Contract,PlannedDate=request.PlannedDate,
                    Status="Chưa tìm thấy kiện Hưng Yên trong CONT ĐI",
                    Notes="Hãy chọn đúng nguồn CONT ĐI và kiểm tra số hợp đồng trong các file Excel."
                });
                continue;
            }
            foreach(var source in packages)
            {
                var row=new PlannedArrivalRow
                {
                    Container=source.ContainerNumber,
                    Contract=source.Contract,Project=source.Project,
                    Company=source.Company,PlannedDate=request.PlannedDate,
                    SourcePackageId=source.Id,SourceContDay=source.SourceDate,
                    SourceFingerprint=PlannedArrivalWorkflow.MaterialFingerprint(source),
                    PackageNo=source.PackageNo,PackageCount=1,
                    MaterialCount=source.Items.Count,
                    MaterialSummary=string.Join(" · ",source.Items.Where(x=>x.IsHungYen)
                        .Take(5).Select(x=>x.Material+" × "+
                            (x.Quantity?.ToString(CultureInfo.GetCultureInfo("vi-VN"))??"?"))),
                    SourceIds=[source.Id]
                };
                var mismatches=new List<string>();
                if(!string.IsNullOrWhiteSpace(request.Project) &&
                   Rules.ProjectCode(request.Project)!=Rules.ProjectCode(source.Project))
                    mismatches.Add("khác công trình/T1/T2");
                if(!string.IsNullOrWhiteSpace(request.Company) &&
                   Rules.Code(request.Company)!=Rules.Code(source.Company))
                    mismatches.Add("khác đơn vị");
                // When the original Excel does not carry a container number,
                // leave it unknown and require per-package confirmation; never
                // pretend a Zalo prediction has been verified by the source file.
                if(!string.IsNullOrWhiteSpace(request.Container) &&
                   !string.IsNullOrWhiteSpace(source.ContainerNumber) &&
                   Rules.Code(request.Container)!=Rules.Code(source.ContainerNumber))
                    mismatches.Add("khác container");
                if(!string.IsNullOrWhiteSpace(request.ContainerExportDate) &&
                   source.SourceDate!=request.ContainerExportDate)
                    mismatches.Add("khác ngày cont đi");
                if(mismatches.Count>0)
                {
                    row.Status="Lệch lịch / nguồn CONT ĐI";
                    row.Notes="Nguồn của HĐ "+code+" "+string.Join(", ",mismatches)+
                        ". Không nhập tự động; sửa lịch hoặc kiểm tra file gốc.";
                }
                else if(string.IsNullOrWhiteSpace(source.Project) ||
                    string.IsNullOrWhiteSpace(source.Company))
                {
                    row.Status="Thiếu công trình hoặc đơn vị nguồn";
                    row.Notes="Cần đúng HĐ + công trình + đơn vị trước khi nhận kho.";
                }
                else if(!DateTime.TryParseExact(source.SourceDate,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                {
                    row.Status="Thiếu ngày cont đi ở nguồn";
                    row.Notes="Không thể phân biệt các đợt cont cùng hợp đồng.";
                }
                else if(!source.Items.Any(x=>x.IsHungYen) ||
                    source.Items.Any(x=>x.IsHungYen && (!x.Quantity.HasValue || x.Quantity<=0)))
                {
                    row.Status="Chưa đủ vật tư / số lượng Hưng Yên";
                    row.Notes="Chỉ lấy kiện được đánh dấu Hưng Yên có tên, đơn vị, số lượng hợp lệ.";
                }
                else if(imported.Contains(source.Id))
                {
                    row.Status="Kiện đã nhập kho";
                    row.Notes="Mã kiện nguồn đã tồn tại trong trang chi tiết Tổng hợp.xlsx.";
                }
                else if(snapshot.DetailPackages.Any(x=>x.OriginPackageId.Length==0 &&
                    x.Identity==source.Identity && x.ArrivalDate==actualDay &&
                    PlannedArrivalWorkflow.MaterialFingerprint(x)==
                    PlannedArrivalWorkflow.MaterialFingerprint(source)))
                {
                    row.Status="Cần kiểm tra kiện đã nhập trước đây";
                    row.Notes="Có kiện cùng vật tư/ngày nhưng thiếu mã nguồn gốc; tránh ghi trùng.";
                }
                else
                {
                    row.Status=Ready;
                    row.Notes="Đã tìm trong nguồn CONT ĐI · "+
                        source.SourceFile+" · kiện "+source.PackageNo+
                        " · ngày cont đi "+Rules.Day(source.SourceDate)+
                        (string.IsNullOrWhiteSpace(source.ContainerNumber)
                         ? " · nguồn chưa ghi mã container, phải xác nhận từng kiện":"");
                }
                // A duplicated source ID can occur on two photo rows. Do not
                // allow an earlier ready match to override a conflicting one.
                if(results.TryGetValue(source.Id,out var previous))
                {
                    if(previous.Status!=row.Status ||
                       (request.Container.Length>0 && previous.Container!=row.Container))
                    {
                        previous.Status="Lịch có nhiều dòng cùng kiện";
                        previous.Notes="Kiện xuất hiện ở nhiều dòng ảnh/lịch. Sửa hoặc xóa dòng trùng trước khi nhập.";
                    }
                }
                else results[source.Id]=row;
            }
        }
        return results.Values.OrderBy(x=>x.Contract).ThenBy(x=>x.Project)
            .ThenBy(x=>x.SourceContDay).ThenBy(x=>x.PackageNo).ToList();
    }

    public static List<Package> SelectVerified(
        IReadOnlyList<PlannedArrivalRow> selected,IEnumerable<AiScheduleLine> requests,
        WorkbookSnapshot fresh,string actualDay)
    {
        if(selected.Count==0)
            throw new InvalidOperationException("Hãy tích từng kiện Hưng Yên thực tế đã về.");
        var current=Preview(requests,fresh,actualDay);
        var packages=new List<Package>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in selected)
        {
            if(row.SourcePackageId.Length==0 || !seen.Add(row.SourcePackageId))
                throw new InvalidOperationException("Kiện chưa có ID nguồn hoặc bị chọn trùng.");
            var match=current.SingleOrDefault(x=>x.SourcePackageId==row.SourcePackageId);
            if(match==null || match.Status!=Ready ||
               match.SourceContDay!=row.SourceContDay ||
               match.MaterialSummary!=row.MaterialSummary ||
               match.PackageNo!=row.PackageNo || match.Contract!=row.Contract)
                throw new InvalidOperationException("Kiện trong CONT ĐI đã thay đổi hoặc không còn đủ điều kiện. Tìm lại trước khi nhập: "+row.Contract);
            var source=fresh.ContPackages.Single(x=>x.Id==row.SourcePackageId);
            var copy=WarehouseService.Clone(source);
            // The source package ID is the receipt's durable, per-package
            // identity. A missing container number is not invented.
            copy.OriginPackageId=source.Id;
            packages.Add(copy);
        }
        return packages;
    }
}
