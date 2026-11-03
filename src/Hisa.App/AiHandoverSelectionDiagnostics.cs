namespace Hisa.App;

/// <summary>Offline regression tests for contract-number-first Zalo handover matching.</summary>
public static class AiHandoverSelectionDiagnostics
{
    public static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Handover by HĐ: " + label);
            checks.Add("Handover by HĐ: " + label);
        }
        const string day = "2026-11-03";
        static AiScheduleDocument Schedule(params AiScheduleLine[] lines) =>
            new() { DispatchDate = day, Dispatches = lines.ToList() };
        static AiScheduleLine Dispatch(string code, string project, string company) =>
            new() { Contract = code, Project = project, Company = company,
                PlannedDate = day, SelectedForHandover = true };
        static ContractRow Source(string code, string project, string company) =>
            new() { Contract = code, Project = project, Company = company,
                Materials = [new MaterialLine { Material = "Máy kéo", Quantity = 1 }] };
        static ContractReport Report(params ContractRow[] rows) =>
            new() { Rows = rows.ToList(), DataAvailable = true };

        var source = Source("43822026", "Cty Tàu Biển Quảng Ninh", "Công ty A");
        var ai = Dispatch("43822026",
            "Cty Tàu Biển Quảng Ninh - Số 2, Đường Hạ Long, Bãi Cháy / Thang",
            "Đơn vị từ Zalo");
        var report = Report(source);
        AiHandoverSelection.Build(report, Schedule(ai), [ai]);
        Check(source.Selected && report.Rows.Single().Project == "Cty Tàu Biển Quảng Ninh" &&
            report.Warnings.Any(x => x.Contains("số hợp đồng duy nhất")),
            "Unique contract 43822026 uses canonical Excel project and company despite OCR address mismatch");

        var t1 = Source("43822026", "Dự án Hưng Yên T1", "HISA");
        var t2 = Source("43822026", "Dự án Hưng Yên T2", "HISA");
        var exact = Dispatch("43822026", "Dự án Hưng Yên T2", "HISA");
        AiHandoverSelection.Build(Report(t1,t2), Schedule(exact), [exact]);
        Check(!t1.Selected && t2.Selected,
            "Two Excel towers select only explicitly matching T2, never T1");

        var ambiguous = Dispatch("43822026", "Dự án khác", "HISA");
        var refused = false;
        try { AiHandoverSelection.Build(Report(
            Source("43822026", "Dự án T1", "HISA"),
            Source("43822026", "Dự án T2", "HISA")), Schedule(ambiguous), [ambiguous]); }
        catch (InvalidOperationException ex) { refused = ex.Message.Contains("nhiều công trình"); }
        Check(refused,"Multiple distinct Excel identities never auto-pick a project");

        var wrongTower = Dispatch("43822026", "Dự án T2", "Đơn vị OCR");
        refused = false;
        try { AiHandoverSelection.Build(Report(
            Source("43822026", "Dự án T1", "HISA")), Schedule(wrongTower), [wrongTower]); }
        catch (InvalidOperationException ex) { refused = ex.Message.Contains("T1/T2"); }
        Check(refused,"Even a unique contract cannot silently cross explicit T1 and T2");

        var arrivalsOnly = new AiScheduleDocument
        {
            DispatchDate = day,
            Arrivals = [Dispatch("43822026", "Dự án T1", "HISA")]
        };
        refused = false;
        try { AiHandoverSelection.Build(Report(
            Source("43822026", "Dự án T1", "HISA")),
            arrivalsOnly, [arrivalsOnly.Arrivals[0]]); }
        catch (InvalidOperationException ex) { refused = ex.Message.Contains("DỰ KIẾN XUẤT"); }
        Check(refused,"A cont-arrival-only contract never enters the dispatch draft");

        return checks;
    }
}
