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

        // Production bug reported in v1.9.43: HĐ 43822026 has two Excel
        // identities whose names differ only by Thang 01 / Thang 02, while
        // the Zalo OCR row ends with the unnumbered word "Thang".
        const string projectRoot =
            "Công Ty CP Cung Ứng Tàu Biển Quảng Ninh - Số 2, Đường Hạ Long, "+
            "Phường Bãi Cháy, Quảng Ninh";
        var excel1=Source("43822026",projectRoot+" (Thang 01)","Vinatech");
        var excel2=Source("43822026",projectRoot+" (Thang 02)","Vinatech");
        var vague=Dispatch("43822026",
            "Cty Tàu Biển Quảng Ninh - Số 2, Đường Hạ Long / Thang",
            "Vinatech");
        var twoTower=Report(excel1,excel2);
        Check(AiHandoverSelection.RequiresProjectChoice(twoTower,vague) &&
            AiHandoverSelection.SourceOptions(twoTower,vague).Count==2,
            "Unnumbered Zalo Thang presents two exact Excel source choices");

        var pickSecond=new Dictionary<AiScheduleLine,List<ContractRow>>
            { [vague]=[excel2] };
        AiHandoverSelection.Build(twoTower,Schedule(vague),[vague],pickSecond);
        Check(!excel1.Selected && excel2.Selected,
            "Checking only Thang 02 selects no materials from Thang 01");

        var pickBoth=new Dictionary<AiScheduleLine,List<ContractRow>>
            { [vague]=[excel1,excel2] };
        AiHandoverSelection.Build(twoTower,Schedule(vague),[vague],pickBoth);
        Check(excel1.Selected && excel2.Selected,
            "Checking both towers explicitly includes both source identities");

        var forged=new Dictionary<AiScheduleLine,List<ContractRow>>
            { [vague]=[Source("99992026","Thang 02","Vinatech")] };
        refused=false;
        try { AiHandoverSelection.Build(twoTower,Schedule(vague),[vague],forged); }
        catch(InvalidOperationException ex) { refused=ex.Message.Contains("không còn hợp lệ"); }
        Check(refused,"Stale/foreign source choice is rejected before drafting");

        var explicitSecond=Dispatch("43822026",
            "Cty Tàu Biển Quảng Ninh (Thang 02)","Vinatech");
        Check(!AiHandoverSelection.RequiresProjectChoice(twoTower,explicitSecond),
            "Explicit Thang 02 in OCR needs no additional source picker");
        AiHandoverSelection.Build(twoTower,Schedule(explicitSecond),[explicitSecond]);
        Check(!excel1.Selected && excel2.Selected,
            "Explicit Thang 02 auto-resolves when Excel projects share one family");

        var unrelated=Report(
            Source("43822026","Dự án khác T1","Vinatech"),
            Source("43822026","Tàu Biển Quảng Ninh T2","Vinatech"));
        var unrelatedHint=Dispatch("43822026","Dự án khác T1","Hãng khác");
        Check(AiHandoverSelection.RequiresProjectChoice(unrelated,unrelatedHint),
            "Different Excel project families cannot auto-resolve from a tower alone");

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
