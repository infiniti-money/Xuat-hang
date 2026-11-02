namespace Hisa.App;

/// <summary>
/// Transfers only explicitly selected OUTGOING rows from the current AI Zalo
/// draft to the ordinary slip-contract input. Never imports incoming cont rows,
/// assumes actual receipt, saves a slip or writes any Excel source.
/// </summary>
public static class ZaloDispatchContractImport
{
    public sealed record Selection(string ContractsText,List<ContractRow> ProjectTargets,
        int AddedContractCodes,int SelectedProjects);

    public static Selection Merge(AiScheduleDocument schedule,
        IReadOnlyCollection<AiScheduleLine> checkedDispatches,string existingContracts)
    {
        if(schedule is null)throw new ArgumentNullException(nameof(schedule));
        if(checkedDispatches is null || checkedDispatches.Count==0)
            throw new InvalidOperationException("Hãy tích hợp đồng trong bảng HÀNG DỰ KIẾN XUẤT KHO.");
        if(checkedDispatches.Any(x=>!schedule.Dispatches.Contains(x)))
            throw new InvalidOperationException("Chỉ được lấy hợp đồng từ HÀNG DỰ KIẾN XUẤT KHO, không lấy lịch cont về.");
        if(checkedDispatches.Any(x=>x.NeedsReview || Rules.Contract(x.Contract).Length<4 ||
                string.IsNullOrWhiteSpace(x.Project) || string.IsNullOrWhiteSpace(x.Company)))
        {
            var failed=checkedDispatches.Where(x=>x.NeedsReview ||
                Rules.Contract(x.Contract).Length<4 ||
                string.IsNullOrWhiteSpace(x.Project) ||
                string.IsNullOrWhiteSpace(x.Company)).Select(x=>x.Contract).Distinct();
            throw new InvalidOperationException("Cần kiểm tra thông tin ảnh Zalo trước khi nhập phiếu: "+
                string.Join(", ",failed)+
                ". Mở AI lịch nhập – xuất để sửa công trình, đơn vị hoặc mã HĐ.");
        }

        var old=Rules.Tokens(existingContracts);
        var selected=checkedDispatches.DistinctBy(x=>
            Rules.Identity(x.Contract,x.Project,x.Company)).ToList();
        var result=old.ToList();
        var existingKeys=old.Select(Rules.Contract).ToHashSet(StringComparer.Ordinal);
        var added=0;
        foreach(var line in selected)
        {
            if(existingKeys.Add(Rules.Contract(line.Contract)))
            {
                result.Add(Rules.CleanContract(line.Contract));
                added++;
            }
        }
        // Identity filter prevents the same contract number on a different
        // project/tower/company from being silently included on the slip.
        var projects=selected.Select(x=>new ContractRow
        {
            Contract=Rules.CleanContract(x.Contract),
            Project=x.Project.Trim(),
            Company=x.Company.Trim()
        }).ToList();
        return new Selection(string.Join(Environment.NewLine,result),projects,added,projects.Count);
    }
}
