using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// Adds manually entered contract codes to the same draft as Gemini's Zalo analysis.
/// No Excel write or warehouse receipt is performed here.
/// </summary>
public static class AiScheduleWorkspace
{
    public static AiScheduleDocument NewDraft(DateTime day) => new()
    {
        ArrivalDate = day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DispatchDate = day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        AnalyzedBy = "Nhập hợp đồng thủ công"
    };

    public static List<string> ParseContracts(string text)
    {
        var codes=text.Split(['\r','\n',',',';','\t'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
            .Select(Rules.CleanContract).Where(x=>x.Length>0).ToList();
        if(codes.Count==0)
            throw new InvalidOperationException("Nhập ít nhất một số hợp đồng (mỗi dòng một mã).");
        if(codes.Count>150)
            throw new InvalidOperationException("Chỉ thêm tối đa 150 hợp đồng mỗi lần.");
        if(codes.Any(x=>Rules.Contract(x).Length<4 || x.Length>80))
            throw new InvalidOperationException("Có số hợp đồng không hợp lệ hoặc quá ngắn. Kiểm tra danh sách đã nhập.");
        return codes.DistinctBy(Rules.Contract).ToList();
    }

    public static int AddManual(
        AiScheduleDocument doc, WorkbookSnapshot snapshot, bool arrival, string codes,
        string project, string company, string container, string sourceDay,
        string note="", bool urgent=false)
    {
        var day=arrival?doc.ArrivalDate:doc.DispatchDate;
        if(!DateTime.TryParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture,
            DateTimeStyles.None,out _))
            throw new InvalidOperationException("Ngày dự kiến không hợp lệ. Hãy sửa ngày tại phần phê duyệt trước.");
        project=(project??"").Trim();company=(company??"").Trim();
        container=arrival?(container??"").Trim():"";
        sourceDay=arrival?(sourceDay??"").Trim():"";
        if(sourceDay.Length>0 && !DateTime.TryParseExact(sourceDay,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            throw new InvalidOperationException("Ngày cont đi phải theo yyyy-MM-dd, ví dụ 2026-10-26.");
        if(arrival && sourceDay.Length>0 &&
           string.CompareOrdinal(sourceDay,day)>0)
            throw new InvalidOperationException("Ngày cont rời nhà máy không được sau ngày dự kiến về kho.");
        var existing=arrival?doc.Arrivals:doc.Dispatches;
        var sources=arrival?snapshot.ContPackages:snapshot.Packages;
        var added=0;
        foreach(var code in ParseContracts(codes))
        {
            var id=Rules.Contract(code);
            var possibilities=sources.Where(p=>Rules.Contract(p.Contract)==id &&
                (project.Length==0||Rules.ProjectCode(p.Project)==Rules.ProjectCode(project)) &&
                (company.Length==0||Rules.Code(p.Company)==Rules.Code(company)) &&
                (!arrival || (container.Length==0 || p.ContainerNumber.Length==0 ||
                       Rules.Code(p.ContainerNumber)==Rules.Code(container))) &&
                (!arrival || (sourceDay.Length==0 || p.SourceDate==sourceDay)))
                .DistinctBy(p=>p.Identity).ToList();
            // Autofill only when exactly one unique contract/project/company identity.
            var inferred=possibilities.Count==1?possibilities[0]:null;
            var resolvedProject=project.Length>0?project:(inferred?.Project??"");
            var resolvedCompany=company.Length>0?company:(inferred?.Company??"");
            var newRow=new AiScheduleLine
            {
                Contract=code,Project=resolvedProject,Company=resolvedCompany,
                PlannedDate=day,Container=container,ContainerExportDate=sourceDay,
                Note=note.Trim(),Urgent=urgent,ManualEntry=true
            };
            if(existing.Any(x=>Rules.Contract(x.Contract)==id &&
                Rules.ProjectCode(x.Project)==Rules.ProjectCode(newRow.Project) &&
                Rules.Code(x.Company)==Rules.Code(newRow.Company) &&
                Rules.Code(x.Container)==Rules.Code(container) &&
                x.ContainerExportDate==sourceDay && x.PlannedDate==day))
                continue;
            if(inferred==null && (resolvedProject.Length==0 || resolvedCompany.Length==0))
            {
                newRow.NeedsReview=true;
                newRow.ReviewNote="Chưa xác nhận công trình/đơn vị; hãy sửa trong bảng trước khi lập phiếu hoặc nhập kho";
            }
            if(arrival && (container.Length==0 || sourceDay.Length==0))
            {
                newRow.NeedsReview=true;
                newRow.ReviewNote=(newRow.ReviewNote.Length>0?newRow.ReviewNote+"; ":"")+
                    "Cần mã cont và ngày cont đi để xác minh kiện nhận kho";
            }
            existing.Add(newRow);added++;
        }
        doc.ApprovedAt="";
        if(doc.AnalyzedBy.Length==0)doc.AnalyzedBy="Thủ công";
        return added;
    }

    /// <summary>Loads approved schedules of either direction, including dispatch-only drafts.</summary>
    public static AiScheduleDocument? LatestApproved(string dataDirectory)
    {
        var folder=Path.Combine(dataDirectory,"ai_schedules");
        if(!Directory.Exists(folder))return null;
        foreach(var file in Directory.EnumerateFiles(folder,"lich-kho-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var doc=JsonSerializer.Deserialize<AiScheduleDocument>(
                    File.ReadAllText(file),WarehouseService.Json);
                if(doc!=null && !string.IsNullOrWhiteSpace(doc.ApprovedAt) &&
                    ((doc.Arrivals?.Count??0)+(doc.Dispatches?.Count??0)>0))
                {
                    doc.Arrivals??=[];doc.Dispatches??=[];
                    return doc;
                }
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
            { /* Ignore invalid or locked historical schedule. */ }
        }
        return null;
    }

    public static List<PlannedArrivalRow> SelectedArrivalPreview(
        AiScheduleDocument doc, WorkbookSnapshot snapshot, string actualDay)
    {
        var selected=doc.Arrivals.Where(x=>x.SelectedForArrival).ToList();
        if(selected.Count==0)return [];
        return PlannedArrivalWorkflow.Preview(doc,snapshot,actualDay)
            .Where(row=>selected.Any(line=>Rules.Identity(line.Contract,line.Project,line.Company)==
                Rules.Identity(row.Contract,row.Project,row.Company) &&
                Rules.Code(line.Container)==Rules.Code(row.Container) &&
                (line.ContainerExportDate.Length==0||line.ContainerExportDate==row.SourceContDay)))
            .ToList();
    }

    // Preselect only a single independently verified physical package per source row.
    // Several physical packages for one contract require the user to choose packages one by one.
    public static void PreselectUnique(IReadOnlyList<PlannedArrivalRow> preview)
    {
        foreach(var group in preview.GroupBy(x=>Rules.Code(x.Container)+"|"+
            Rules.Identity(x.Contract,x.Project,x.Company)+"|"+x.SourceContDay))
        {
            if(group.Count()==1 && group.First().Status=="Đủ nguồn · chờ xác nhận đã về")
                group.First().Selected=true;
        }
    }
}
