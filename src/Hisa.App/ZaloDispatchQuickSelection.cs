namespace Hisa.App;

/// <summary>
/// Quick selection of EXPLICIT outgoing Zalo schedule rows. Selection only
/// changes the local draft checkboxes, never confirms shipment or saves a slip.
/// </summary>
public static class ZaloDispatchQuickSelection
{
    public static bool Eligible(AiScheduleLine line,string dispatchDate)
    {
        return line is not null &&
            !line.NeedsReview &&
            Rules.Contract(line.Contract).Count(char.IsDigit)>=4 &&
            !string.IsNullOrWhiteSpace(line.Project) &&
            !string.IsNullOrWhiteSpace(line.Company) &&
            line.PlannedDate==dispatchDate;
    }

    public static int SelectAllEligible(AiScheduleDocument doc)
    {
        if(doc is null)throw new ArgumentNullException(nameof(doc));
        foreach(var row in doc.Dispatches)
            row.SelectedForHandover=Eligible(row,doc.DispatchDate);
        return doc.Dispatches.Count(x=>x.SelectedForHandover);
    }

    public static List<AiScheduleLine> Selected(AiScheduleDocument doc)
    {
        if(doc is null)throw new ArgumentNullException(nameof(doc));
        return doc.Dispatches.Where(x=>x.SelectedForHandover).ToList();
    }
}
