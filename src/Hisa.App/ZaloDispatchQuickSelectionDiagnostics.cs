namespace Hisa.App;

/// <summary>Dispatch selection uses the outgoing half of a Zalo schedule only.</summary>
public static class ZaloDispatchQuickSelectionDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string label)
        {
            if(!ok)throw new InvalidOperationException("Zalo dispatch quick: "+label);
            checks.Add("Zalo dispatch quick: "+label);
        }
        var schedule=new AiScheduleDocument
        {
            DispatchDate="2026-11-03",
            Arrivals=[new AiScheduleLine{Contract="05552026",
                Project="HĐ chỉ cont về",Company="Đơn vị A",
                PlannedDate="2026-11-03",SelectedForArrival=true}],
            Dispatches=[
                new AiScheduleLine{Contract="29102026",Project="Hưng Yên T1",
                    Company="Công ty A",PlannedDate="2026-11-03"},
                new AiScheduleLine{Contract="05492026",Project="Hưng Yên T2",
                    Company="Công ty B",PlannedDate="2026-11-03"},
                new AiScheduleLine{Contract="68882026",Project="Chưa rõ công ty",
                    PlannedDate="2026-11-03"},
                new AiScheduleLine{Contract="79992026",Project="Chưa rõ lịch",
                    Company="Công ty C",PlannedDate="2026-11-05"},
                new AiScheduleLine{Contract="80002026",Project="Cần kiểm tra",
                    Company="Công ty D",PlannedDate="2026-11-03",
                    NeedsReview=true,ReviewNote="Tên T1/T2 chưa chắc chắn"}
            ]
        };
        var chosen=ZaloDispatchQuickSelection.SelectAllEligible(schedule);
        Check(chosen==2 &&
              schedule.Dispatches.Count(x=>x.SelectedForHandover)==2 &&
              !schedule.Arrivals[0].SelectedForHandover,
            "Select-all picks only valid expected-dispatch rows, never incoming contracts");
        Check(ZaloDispatchQuickSelection.Selected(schedule)
                .Select(x=>x.Contract).SequenceEqual(["29102026","05492026"]),
            "Quick list retains outgoing contracts for explicit material selection");
        Check(schedule.Dispatches.Skip(2).All(x=>!x.SelectedForHandover),
            "Missing company, wrong date and OCR review are excluded from auto-selection");
        AiScheduleBulkSelection.SetAll(schedule,false,false);
        Check(ZaloDispatchQuickSelection.Selected(schedule).Count==0 &&
              schedule.Arrivals[0].SelectedForArrival,
            "Clearing dispatch selection does not touch receipt selection");
        var filter=HandoverMaterialGroups.BuildFilter(
            string.Join("\n",new[]{"29102026","05492026"}),
            new[]{"Máy kéo","Tủ điện"});
        Check(filter.Categories.SequenceEqual(["Máy kéo","Tủ điện"]) &&
              !filter.Categories.Contains("Khác"),
            "Selected material groups exclude all unselected categories");
        return checks;
    }
}
