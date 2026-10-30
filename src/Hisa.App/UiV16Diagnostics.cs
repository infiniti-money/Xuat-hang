namespace Hisa.App;

/// <summary>Regression fixtures for v1.6: no external Gemini call, no real workbook mutations.</summary>
public static class UiV16Diagnostics
{
    public static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool pass, string name)
        {
            if (!pass) throw new InvalidOperationException("FAIL v1.6: " + name);
            checks.Add("v1.6: " + name);
        }

        const string day = "2026-10-31";
        var doc = new AiScheduleDocument
        {
            ApprovedAt = DateTimeOffset.Now.ToString("O"),
            ArrivalDate = day, DispatchDate = day,
            Arrivals =
            [
                new AiScheduleLine { Contract = "123(1)", Project = "Công trình A",
                    Company = "HISA", Container = "CONT-01", PlannedDate = day },
                new AiScheduleLine { Contract = "123(2)", Project = "Công trình B",
                    Company = "HISA", Container = "CONT-02", PlannedDate = day }
            ],
            Dispatches =
            [
                new AiScheduleLine { Contract = "123(1)", Project = "Công trình A",
                    Company = "HISA", PlannedDate = day },
                new AiScheduleLine { Contract = "123(2)", Project = "Công trình B",
                    Company = "HISA", PlannedDate = day }
            ]
        };
        var pkgA = new Package
        {
            Id = "source-01", Contract = "123(1)", Project = "Công trình A",
            Company = "HISA",
            Items = [new MaterialLine { Contract = "123(1)", Project = "Công trình A",
                Company = "HISA", Material = "Đầu cửa tầng 700", Unit = "Bộ", Quantity = 1 }]
        };
        var pkgB = new Package
        {
            Id = "source-02", Contract = "123(2)", Project = "Công trình B",
            Company = "HISA",
            Items = [new MaterialLine { Contract = "123(2)", Project = "Công trình B",
                Company = "HISA", Material = "Đầu cửa tầng 750", Unit = "Bộ", Quantity = 1 }]
        };
        var snapshot = new WorkbookSnapshot
        {
            ContPackages = [pkgA, pkgB],
            Packages = [pkgA, pkgB]
        };
        var preview = PlannedArrivalWorkflow.Preview(doc, snapshot, day);
        Check(preview.Count == 2 && preview.All(x => x.Status.StartsWith("Đủ nguồn")),
            "AI incoming schedule compares exact source package identities");
        Check(preview.All(x => !x.Selected), "Planned containers are never automatically marked arrived");
        var chosen = PlannedArrivalWorkflow.SelectVerified([preview[0]], doc, snapshot, day);
        Check(chosen.Count == 1 && chosen.Single().Id == "source-01",
            "Only manually checked actual container can be imported");
        snapshot.DetailPackages =
        [
            new Package { Id = "detail-01", Contract = pkgA.Contract,
                Project = pkgA.Project, Company = pkgA.Company, ArrivalDate = day,
                Items = pkgA.Items }
        ];
        Check(PlannedArrivalWorkflow.Preview(doc, snapshot, day)[0].Status == "Cần kiểm tra kiện nhập cũ",
            "Already received contract/day is blocked against duplicates");
        var preventedDuplicate = false;
        try { PlannedArrivalWorkflow.SelectVerified([preview[0]], doc, snapshot, day); }
        catch (InvalidOperationException) { preventedDuplicate = true; }
        Check(preventedDuplicate, "Rechecking source before import blocks duplicate");

        snapshot.DetailPackages = [];
        doc.Arrivals[1].Container = "CONT-01"; // different contracts: still separate goods
        Check(PlannedArrivalWorkflow.Preview(doc, snapshot, day).Count == 2,
            "Different project/contract in one container remains separate");
        doc.Arrivals[1].Contract = pkgA.Contract;
        doc.Arrivals[1].Project = pkgA.Project;
        var collision = PlannedArrivalWorkflow.Preview(doc, snapshot, day);
        Check(collision[0].Status == "Đủ nguồn · chờ xác nhận đã về",
            "Same contract/identity in same container is possible; not automatically imported");
        doc.Arrivals[1].Container = "CONT-02";
        Check(PlannedArrivalWorkflow.Preview(doc, snapshot, day)
            .All(x => x.Status == "Cùng HĐ ở nhiều container"),
            "Same source packages in multiple containers cannot be assigned by guess");

        // Reset schedule after intentional collision fixture, before testing quantity variants.
        doc.Arrivals=[
            new AiScheduleLine {Contract=pkgA.Contract,Project=pkgA.Project,Company=pkgA.Company,
                Container="CONT-01",PlannedDate=day},
            new AiScheduleLine {Contract=pkgB.Contract,Project=pkgB.Project,Company=pkgB.Company,
                Container="CONT-02",PlannedDate=day}
        ];
        // Same contract, same project but different material quantities: two physical packages.
        snapshot.DetailPackages=[];
        var manyA=new Package {
            Id="source-different-qty", Contract=pkgA.Contract, Project=pkgA.Project,
            Company=pkgA.Company, SourceDate="2026-10-25",
            Items=[new MaterialLine {Material="Đầu cửa tầng 700",Unit="Bộ",Quantity=3}]
        };
        snapshot.ContPackages=[pkgA,manyA,pkgB];
        var both=PlannedArrivalWorkflow.Preview(doc,snapshot,day);
        Check(both.Count==3 && both.Count(x=>x.Contract==pkgA.Contract)==2,
            "Same contract/project with different material quantity retains both packages");
        Check(PlannedArrivalWorkflow.MaterialFingerprint(pkgA)!=
            PlannedArrivalWorkflow.MaterialFingerprint(manyA),
            "Material quantity is part of physical package distinction");

        // T1/T2 and parenthesized tower names must not be normalized into one project.
        Check(Rules.ProjectCode("Công trình Hisa T1")!=Rules.ProjectCode("Công trình Hisa T2") &&
              Rules.ProjectCode("Công trình Hisa (Thang 1)")!=
              Rules.ProjectCode("Công trình Hisa (Thang 2)") &&
              Rules.ProjectCode("Công trình Hisa (1)")!=
              Rules.ProjectCode("Công trình Hisa (2)"),
            "T1/T2, Thang 1/2 and (1)/(2) distinguish separate project packages");

        // Two cont shipments with same contract/project/quantity require unique source
        // identification, including departure day; never combine by contract.
        manyA.Items=[new MaterialLine {Material="Đầu cửa tầng 700",Unit="Bộ",Quantity=1}];
        pkgA.SourceDate="2026-10-26";
        manyA.SourceDate="2026-10-27";
        doc.Arrivals=[
            new AiScheduleLine {Contract=pkgA.Contract,Project=pkgA.Project,Company=pkgA.Company,
                Container="CONT-01",ContainerExportDate="2026-10-26",PlannedDate=day},
            new AiScheduleLine {Contract=pkgA.Contract,Project=pkgA.Project,Company=pkgA.Company,
                Container="CONT-02",ContainerExportDate="2026-10-27",PlannedDate=day}
        ];
        var twoContainers=PlannedArrivalWorkflow.Preview(doc,snapshot,day);
        Check(twoContainers.Count==2 && twoContainers.All(x=>x.Status.StartsWith("Đủ nguồn")) &&
              twoContainers.Select(x=>x.SourcePackageId).Distinct().Count()==2,
            "Two departures with same contract and material quantity remain independent packages");
        var bothSelected=PlannedArrivalWorkflow.SelectVerified(twoContainers,doc,snapshot,day);
        Check(bothSelected.Count==2 && bothSelected[0].OriginPackageId!=bothSelected[1].OriginPackageId &&
              bothSelected[0].ContainerNumber!=bothSelected[1].ContainerNumber,
            "Selecting separate containers retains per-source identity even with same quantity");

        // No known shipment identifiers: cannot silently guess which package belongs to which cont.
        doc.Arrivals[1].ContainerExportDate=doc.Arrivals[0].ContainerExportDate;
        Check(PlannedArrivalWorkflow.Preview(doc,snapshot,day)
            .All(x=>x.Status=="Cùng HĐ ở nhiều container"),
            "Ambiguous identical departure dates are blocked instead of guessing");
        doc.Arrivals=[
            new AiScheduleLine {Contract="123(1)",Project="Công trình A",
                Company="HISA",Container="CONT-01",PlannedDate=day},
            new AiScheduleLine {Contract="123(2)",Project="Công trình B",
                Company="HISA",Container="CONT-02",PlannedDate=day}
        ];
        snapshot.ContPackages=[pkgA,pkgB];

        // Handover is always based on Dispatches, never the Arrivals table.
        var report = new ContractReport
        {
            DataAvailable = true,
            Rows =
            [
                new ContractRow { Contract = pkgA.Contract, Project = pkgA.Project,
                    Company = pkgA.Company, Materials = pkgA.Items, Packages = [pkgA] },
                new ContractRow { Contract = pkgB.Contract, Project = pkgB.Project,
                    Company = pkgB.Company, Materials = pkgB.Items, Packages = [pkgB] }
            ]
        };
        doc.Arrivals[1].Contract = pkgB.Contract;
        doc.Arrivals[1].Project = pkgB.Project;
        var selectedReport = AiHandoverSelection.Build(report, doc, [doc.Dispatches[0]]);
        Check(selectedReport.Rows.Count(x => x.Selected) == 1 &&
            selectedReport.Rows.Single(x => x.Selected).Contract == pkgA.Contract,
            "Ticking one AI dispatch row selects exactly one contract for planned handover");
        Check(!doc.Arrivals[0].SelectedForHandover &&
              !doc.Arrivals[1].SelectedForHandover,
            "AI arrivals are never treated as handover selections");
        var different = new AiScheduleLine
        {
            Contract = pkgA.Contract, Project = "Other project", Company = "HISA", PlannedDate = day
        };
        doc.Dispatches.Add(different);
        var ambiguous = false;
        try { AiHandoverSelection.Build(report, doc, [different]); }
        catch (InvalidOperationException) { ambiguous = true; }
        Check(ambiguous, "Same contract but mismatched project cannot create wrong handover");
        var unapproved = new AiScheduleDocument
        {
            DispatchDate = day, Dispatches = [doc.Dispatches[0]]
        };
        var blocked = false;
        try { AiHandoverSelection.Build(report, unapproved, unapproved.Dispatches); }
        catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "Handover requires approved Gemini schedule");
        var noSelection = false;
        try { AiHandoverSelection.Build(report, doc, []); }
        catch (InvalidOperationException) { noSelection = true; }
        Check(noSelection, "No checked dispatch row means no handover");

        return checks;
    }
}
