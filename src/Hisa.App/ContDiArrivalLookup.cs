using System.Globalization;

namespace Hisa.App;

/// <summary>
/// Exact contract search against physical workbooks under Container, shared
/// by Zalo photos and manually entered contract codes.
/// The user selects physical source packages, not whole contract numbers.
/// </summary>
public static class ContDiArrivalLookup
{
    public const string Ready="Đủ nguồn · chờ xác nhận đã về";

    /// <summary>
    /// The receipt's authoritative day is column B of Tổng hợp.xlsx/chi tiết.
    /// SourcePackageId comes from the original physical Container package (AB);
    /// contract alone must NEVER be used to mark another package as received.
    /// </summary>
    public sealed record ReceivedNotice(string Contract,string Project,string Company,
        string SourcePackageId,int PackageNo,string SourceContDay,string ArrivalDays)
    {
        public string Description => "HĐ "+Contract+" · "+Project+
            " · kiện "+PackageNo+
            (string.IsNullOrWhiteSpace(SourceContDay)?"":" · cont đi "+Rules.Day(SourceContDay))+
            " · ĐÃ NHẬP ngày "+ArrivalDays+" (Tổng hợp.xlsx / chi tiết)";
    }

    public static List<ReceivedNotice> PreviouslyReceived(
        IEnumerable<AiScheduleLine> requests,WorkbookSnapshot snapshot)
    {
        var wanted=requests.Where(x=>Rules.Contract(x.Contract).Length>0).ToList();
        var groups=snapshot.DetailPackages
            .Where(p=>!string.IsNullOrWhiteSpace(p.OriginPackageId) &&
                wanted.Any(req=>Rules.Contract(req.Contract)==Rules.Contract(p.Contract) &&
                    (req.Project.Length==0 ||
                        Rules.ProjectCode(req.Project)==Rules.ProjectCode(p.Project)) &&
                    (req.Company.Length==0 || Rules.Code(req.Company)==Rules.Code(p.Company))))
            .GroupBy(p=>p.OriginPackageId,StringComparer.Ordinal);
        return groups.Select(group=>
        {
            var first=group.First();
            // The source ID, not just contract and quantities, identifies one
            // already-received package across source moves / repeated searches.
            var source=snapshot.ContPackages.FirstOrDefault(x=>x.Id==group.Key);
            var dates=group.Select(x=>x.ArrivalDate)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x=>x,StringComparer.Ordinal)
                .Select(x=>string.IsNullOrWhiteSpace(x)?"chưa rõ (kiểm tra cột B)":Rules.Day(x));
            return new ReceivedNotice(first.Contract,first.Project,first.Company,group.Key,
                source?.PackageNo??first.PackageNo,
                source?.SourceDate??first.SourceDate,string.Join(", ",dates));
        }).OrderBy(x=>Rules.Contract(x.Contract)).ThenBy(x=>x.Project)
          .ThenBy(x=>x.SourceContDay).ThenBy(x=>x.PackageNo).ToList();
    }

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
        var receipts=PreviouslyReceived(requests,snapshot);
        var results=new Dictionary<string,PlannedArrivalRow>(StringComparer.Ordinal);
        foreach(var request in requests.Where(x=>Rules.Contract(x.Contract).Length>0))
        {
            var code=Rules.Contract(request.Contract);
            // Exact contract match, never prefix/fuzzy search. A contract can have
            // separate T1/T2, containers, departure days and physical packages.
            var packages=snapshot.ContPackages
                .Where(x=>Rules.Contract(x.Contract)==code && x.Id.Length>0 &&
                    x.Items.Any(i=>i.IsHungYen))
                .ToList();
            if(packages.Count==0)
            {
                // A fully received workbook may have moved to Đã nhập kho.
                // Show its actual receipt day in the separate notice, never as
                // a selectable Container row or a misleading "not found" row.
                if(receipts.Any(x=>Rules.Contract(x.Contract)==code))
                    continue;
                results.TryAdd("missing|"+code,new PlannedArrivalRow
                {
                    Contract=request.Contract,PlannedDate=request.PlannedDate,
                    Status="Chưa tìm thấy kiện Hưng Yên trong Container",
                    Notes="Không có kiện HƯNG YÊN/HUNG YEN đúng số HĐ trong thư mục Container."
                });
                continue;
            }
            foreach(var source in packages)
            {
                // Filter before all mismatch/status checks: an imported package
                // must never reappear even when its Zalo metadata disagrees.
                // Its saved date is shown by PreviouslyReceived instead.
                if(imported.Contains(source.Id))continue;
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
                    row.Status="Lệch lịch / nguồn Container";
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
                else if(snapshot.DetailPackages.Any(x=>x.OriginPackageId.Length==0 &&
                    x.Identity==source.Identity &&
                    PlannedArrivalWorkflow.MaterialFingerprint(x)==
                    PlannedArrivalWorkflow.MaterialFingerprint(source)))
                {
                    var dates=snapshot.DetailPackages.Where(x=>x.OriginPackageId.Length==0 &&
                        x.Identity==source.Identity &&
                        PlannedArrivalWorkflow.MaterialFingerprint(x)==
                        PlannedArrivalWorkflow.MaterialFingerprint(source))
                        .Select(x=>x.ArrivalDate).Distinct().Order()
                        .Select(x=>x.Length==0?"chưa rõ":Rules.Day(x));
                    row.Status="Cần đối chiếu kiện nhập cũ";
                    row.Notes="Tổng hợp.xlsx/chi tiết đã có hàng tương tự nhập ngày "+
                        string.Join(", ",dates)+
                        " nhưng KHÔNG có mã kiện nguồn để chứng minh là cùng kiện. "+
                        "Không tự ẩn hoặc nhập lại; hãy đối chiếu nguồn.";
                }
                else
                {
                    row.Status=Ready;
                    var sameGoods=snapshot.DetailPackages.Any(x=>
                        x.OriginPackageId!=source.Id &&
                        x.Identity==source.Identity && x.ArrivalDate==actualDay &&
                        PlannedArrivalWorkflow.MaterialFingerprint(x)==
                        PlannedArrivalWorkflow.MaterialFingerprint(source));
                    row.Notes=(sameGoods?"CẢNH BÁO: cùng vật tư đã nhận ngày này; kiểm tra kiện nguồn khác. · ":"")+
                        "Đã quét file Container · "+
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
        var already=PreviouslyReceived(requests,fresh);
        // A receipt can be committed by another session or its workbook moved
        // after the user ticked it. Report the actual saved day, not a generic
        // "source changed" warning.
        foreach(var row in selected)
        {
            var past=already.FirstOrDefault(x=>x.SourcePackageId==row.SourcePackageId);
            if(past!=null)
                throw new InvalidOperationException("KHÔNG NHẬP TRÙNG: "+past.Description+
                    ". Kiện đã ẩn khỏi bảng Container; bấm Tìm kiện để cập nhật.");
        }
        var current=Preview(requests,fresh,actualDay);
        var packages=new List<Package>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in selected)
        {
            if(row.SourcePackageId.Length==0 || !seen.Add(row.SourcePackageId))
                throw new InvalidOperationException("Kiện chưa có ID nguồn hoặc bị chọn trùng.");
            var match=current.SingleOrDefault(x=>x.SourcePackageId==row.SourcePackageId);
            if(match==null)
                throw new InvalidOperationException("Không còn thấy kiện chưa nhập của HĐ "+
                    row.Contract+" · kiện "+row.PackageNo+
                    " trong nguồn Container. Tìm lại; nếu đã được nhập, xem thông báo ngày nhập ở phía trên.");
            if(match.Status!=Ready)
                throw new InvalidOperationException("HĐ "+row.Contract+" · kiện "+row.PackageNo+
                    " chưa đủ điều kiện: "+match.Status+". "+match.Notes);
            if(match.SourceContDay!=row.SourceContDay ||
               match.MaterialSummary!=row.MaterialSummary ||
               match.SourceFingerprint!=row.SourceFingerprint ||
               match.PackageNo!=row.PackageNo || match.Contract!=row.Contract)
                throw new InvalidOperationException("Nguồn Container của HĐ "+row.Contract+
                    " · kiện "+row.PackageNo+" đã thay đổi ngày cont đi hoặc vật tư/số lượng."+
                    " Chưa ghi Excel. Bấm Tìm kiện để kiểm tra lại.");
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
