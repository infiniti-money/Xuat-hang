namespace Hisa.App;

/// <summary>
/// The six operator-selectable material groups for warehouse handover slips.
/// "Khác" is intentionally absent: miscellaneous goods must never be
/// silently added when the operator selects a group on the dispatch screen.
/// </summary>
public static class HandoverMaterialGroups
{
    public sealed record Option(string Key,string Label);

    public static readonly IReadOnlyList<Option> Options =
    [
        new("Tủ điện","Tủ điện"),
        new("Máy kéo","Máy kéo"),
        new("Đầu cửa tầng","Đầu cửa tầng"),
        new("Đầu cửa car","Đầu cửa car"),
        new("Ray dẫn hướng","Ray dẫn hướng"),
        new("Bass nối ray","Bass nối")
    ];

    public static List<string> All => Options.Select(x=>x.Key).ToList();

    /// <summary>
    /// Normalize the selected group keys and fail closed when no group was chosen.
    /// Do not interpret an empty category filter as "all": generic Excel search
    /// treats it that way, which would add unwanted material to a handover slip.
    /// </summary>
    public static List<string> RequireSelected(IEnumerable<string> selected)
    {
        if(selected is null)throw new ArgumentNullException(nameof(selected));
        var values=selected.ToList();
        var valid=Options.Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
        if(values.Any(x=>!valid.Contains(x)))
            throw new InvalidOperationException("Có nhóm vật tư không hỗ trợ. Chỉ chọn 6 nhóm bàn giao.");
        var normalized=values.Distinct(StringComparer.Ordinal).ToList();
        if(normalized.Count==0)
            throw new InvalidOperationException("Hãy tích ít nhất một nhóm vật tư cần lập phiếu.");
        return normalized;
    }

    public static SearchFilter BuildFilter(string contracts,IEnumerable<string> selected)
    {
        if(Rules.Tokens(contracts).Count==0)
            throw new InvalidOperationException("Hãy chọn ít nhất một hợp đồng trước khi lập phiếu.");
        return new SearchFilter
        {
            ContractsText=contracts,
            Categories=RequireSelected(selected)
        };
    }

    public static void EnsureSelectedContractsHaveMaterials(ContractReport report)
    {
        var empty=report.NoMaterialMatch.Where(x=>x.Selected).ToList();
        if(empty.Count==0)return;
        throw new InvalidOperationException(
            "Không có vật tư thuộc các nhóm đã chọn trong Tổng hợp.xlsx cho: "+
            string.Join("; ",empty.Select(x=>x.Contract+" · "+x.Project))+
            ". Bỏ tích hợp đồng này hoặc chọn thêm nhóm vật tư rồi thử lại.");
    }
}
