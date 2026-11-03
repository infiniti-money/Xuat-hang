namespace Hisa.App;

/// <summary>
/// Fast inbound Zalo workflow: take every checked forecast contract number,
/// then look up actual Hưng Yên packages in Container. Image/OCR tower,
/// company, container ID and departure day are search hints only: never use
/// them to omit a real physical package with the exact contract number.
/// The confirmation screen always shows the ACTUAL Excel source identity.
/// </summary>
public static class ZaloContainerQuickImport
{
    public sealed record Review(int Found,int Preselected,int NeedsManualReview,
        int AmbiguousContracts);

    public static List<AiScheduleLine> ContractRequests(
        IEnumerable<AiScheduleLine> selectedArrivals)
    {
        if(selectedArrivals is null)throw new ArgumentNullException(nameof(selectedArrivals));
        // Do not use outgoing schedule lines and do not infer fuzzy contract
        // prefixes. One source contract can contain several distinct towers
        // and packages, so dedupe only the lookup code, not the source goods.
        return selectedArrivals
            .Where(x=>Rules.Contract(x.Contract).Count(char.IsDigit)>=4)
            .GroupBy(x=>Rules.Contract(x.Contract),StringComparer.Ordinal)
            .Select(g=>new AiScheduleLine
            {
                Contract=g.First().Contract.Trim(),
                PlannedDate=g.First().PlannedDate,
                ManualEntry=g.All(x=>x.ManualEntry)
            }).ToList();
    }

    public static Review PrepareReview(IList<PlannedArrivalRow> rows)
    {
        if(rows is null)throw new ArgumentNullException(nameof(rows));
        // Physical source identification and all duplicate/history checks
        // already happened in ContDiArrivalLookup.Preview.
        PlannedArrivalWorkflow.SelectAllEligibleForReview(rows);
        var ambiguous=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in rows
            .Where(x=>x.Status==ContDiArrivalLookup.Ready)
            .GroupBy(x=>Rules.Contract(x.Contract),StringComparer.Ordinal))
        {
            var distinctProjects=group
                .Select(x=>Rules.Identity(x.Contract,x.Project,x.Company))
                .Distinct(StringComparer.Ordinal).Count();
            var distinctLots=group
                .Select(x=>Rules.Code(x.Container)+"|"+x.SourceContDay)
                .Distinct(StringComparer.Ordinal).Count();
            if(distinctProjects>1 || distinctLots>1)
                ambiguous.Add(group.Key);
        }
        // Repeated contract number with T1/T2 or multiple physical container
        // lots is NEVER automatically marked as actually arrived. The user
        // must tick the intended packages explicitly before saving.
        foreach(var row in rows)
            if(ambiguous.Contains(Rules.Contract(row.Contract)) ||
               row.Notes.Contains("CẢNH BÁO",StringComparison.OrdinalIgnoreCase))
                row.Selected=false;
        return new Review(rows.Count(x=>x.Status==ContDiArrivalLookup.Ready),
            rows.Count(x=>x.Selected),
            rows.Count(x=>!x.Selected),
            ambiguous.Count);
    }
}
