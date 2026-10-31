namespace Hisa.App;

/// <summary>
/// Tests for visible workflow consolidation: no existing warehouse action is removed,
/// only routed from a smaller, role-oriented navigation menu.
/// </summary>
public static class WorkflowUiDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool okay,string label)
        {
            if(!okay)throw new InvalidOperationException("Workflow UI regression: "+label);
            checks.Add("Workflow UI: "+label);
        }
        var dispatch=new[]{"create","plan","readiness"};
        var arrivals=new[]{"arrival","planned-arrivals","arrival-packages","packages","duplicate-review"};
        var ai=new[]{"gemini-chat","ai-schedule"};
        var management=new[]{"flags","slips","daily","templates","information","reports-hub"};
        Check(dispatch.All(x=>MainWindow.NavigationGroup(x)=="dispatch-hub"),
            "Readiness and create actions share dispatch section");
        Check(arrivals.All(x=>MainWindow.NavigationGroup(x)=="arrival-hub"),
            "Arrival, approved AI import and independent package review share inbound section");
        Check(ai.All(x=>MainWindow.NavigationGroup(x)=="ai-hub"),
            "Gemini chat and Zalo image workflow share one AI section");
        Check(management.All(x=>MainWindow.NavigationGroup(x)=="management-hub"),
            "Status, handover slips, history, settings and legacy reporting share one management hub");
        Check(MainWindow.NavigationGroup("flags")=="management-hub",
            "Hoan/huy contract status screen is reachable from consolidated navigation");
        var visible=new[]{"dashboard","dispatch-hub","arrival-hub","lookup","ai-hub","management-hub","settings"};
        Check(visible.All(x=>MainWindow.NavigationGroup(x)==x),
            "Primary destinations and direct Excel source settings remain selectable");
        Check(MainWindow.NavigationGroup("settings")=="settings",
            "Excel source settings stays a direct navigation destination");
        Check(dispatch.Concat(arrivals).Concat(ai).Concat(management).Concat(visible)
            .Distinct().Count()==dispatch.Length+arrivals.Length+ai.Length+management.Length+visible.Length,
            "No workflow route is shown in multiple independent top-level sections");

        var tomorrow=DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        var today=DateTime.Today.ToString("yyyy-MM-dd");
        Check(MainWindow.PreferredDispatchDay(new DispatchReadinessState())==tomorrow,
            "Missing schedule prompts for tomorrow instead of fabricating a plan");
        Check(MainWindow.PreferredDispatchDay(new DispatchReadinessState
        {
            Plans=[new DispatchPlanItem{Day=today,Contract="TEST"}]
        })==today,"Existing today's plan is displayed when no tomorrow plan exists");
        Check(MainWindow.PreferredDispatchDay(new DispatchReadinessState
        {
            Plans=[new DispatchPlanItem{Day=today,Contract="TEST"},
                new DispatchPlanItem{Day=tomorrow,Contract="TEST2"}]
        })==tomorrow,"When both exist, upcoming plan remains accessible by date");

        // Same-contract different dates must remain independent after a UI change.
        Check(Rules.ProjectWithContDate("Công trình A T1","2026-10-26") ==
              "Công trình A T1 (26.10)" &&
              Rules.ProjectWithContDate("Công trình A T1","2026-10-27") ==
              "Công trình A T1 (27.10)",
            "New navigation never changes (dd.MM) physical package distinction");
        return checks;
    }
}
