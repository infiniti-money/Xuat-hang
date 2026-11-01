namespace Hisa.App;

/// <summary>
/// Bulk selection only changes transient checkboxes; it NEVER marks arrivals
/// or dispatches as physically completed or writes a workbook.
/// </summary>
public static class AiScheduleBulkSelection
{
    public static int SetAll(AiScheduleDocument doc,bool arrivals,bool selected)
    {
        if(doc==null)throw new ArgumentNullException(nameof(doc));
        var rows=arrivals?doc.Arrivals:doc.Dispatches;
        foreach(var line in rows)
        {
            if(arrivals)line.SelectedForArrival=selected;
            else line.SelectedForHandover=selected;
        }
        return rows.Count;
    }
}
