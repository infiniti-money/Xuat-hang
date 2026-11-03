namespace Hisa.App;

/// <summary>Offline Zalo-to-Container fast path. No Gemini calls or real Excel writes.</summary>
public static class ZaloContainerQuickImportDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool valid,string text)
        {
            if(!valid)throw new InvalidOperationException("Zalo quick arrival: "+text);
            checks.Add("Zalo quick arrival: "+text);
        }

        var doc=new AiScheduleDocument
        {
            AnalyzedBy="Gemini",ArrivalDate="2026-11-03",
            Arrivals=[
                new AiScheduleLine{Contract="05492026",Project="Dự án T1",
                    Company="Sai tên OCR",Container="ZALO-001",
                    PlannedDate="2026-11-03"},
                new AiScheduleLine{Contract="05492026",Project="Dự án T2",
                    Company="Công ty A",Container="ZALO-002",
                    PlannedDate="2026-11-03",NeedsReview=true},
                new AiScheduleLine{Contract="29102026",Project="OCR sai tên",
                    PlannedDate="2026-11-03"},
                new AiScheduleLine{Contract="99999999",Project="Chưa có nguồn",
                    PlannedDate="2026-11-03"},
                new AiScheduleLine{Contract="không rõ"}
            ],
            Dispatches=[new AiScheduleLine{Contract="88882026",
                Project="HĐ chỉ dự kiến xuất",PlannedDate="2026-11-03"}]
        };
        AiScheduleBulkSelection.SetAll(doc,true,true);
        var requests=ZaloContainerQuickImport.ContractRequests(
            doc.Arrivals.Where(x=>x.SelectedForArrival));
        Check(requests.Select(x=>Rules.Contract(x.Contract)).SequenceEqual(
                ["5492026","29102026","99999999"]) &&
              requests.All(x=>x.Project=="" && x.Company=="" && x.Container=="") &&
              requests.All(x=>!x.SelectedForArrival),
            "All incoming numbers are unique and looked up by exact code only, ignoring OCR project/container mismatches");
        Check(!requests.Any(x=>Rules.Contract(x.Contract)==Rules.Contract("88882026")) &&
              doc.Arrivals.All(x=>x.SelectedForArrival) &&
              doc.Dispatches.All(x=>!x.SelectedForHandover),
            "Dispatch-only contracts are not mixed into inbound scans; selection does not imply physical receipt");

        Package Pack(string id,string code,string project,string cont,string date,
            bool hungYen=true) => new()
        {
            Id=id,Contract=code,Project=project,Company="Công ty A",
            ContainerNumber=cont,SourceDate=date,PackageNo=1,
            SourceFile="Container.xlsx",
            Items=[new MaterialLine{Contract=code,Project=project,
                Company="Công ty A",Material="Máy kéo",Unit="Bộ",
                Quantity=1,IsHungYen=hungYen}]
        };
        var snap=new WorkbookSnapshot
        {
            ContPackages=[
                Pack("t1","05492026","Dự án T1","CNTR-1","2026-10-28"),
                Pack("t2","05492026","Dự án T2","CNTR-2","2026-10-29"),
                Pack("single","29102026","Dự án đúng từ Excel","CNTR-3","2026-10-30"),
                Pack("hn","88882026","Hà Nội","CNTR-4","2026-10-30",false)
            ]
        };
        var preview=ContDiArrivalLookup.Preview(requests,snap,"2026-11-03");
        Check(preview.Count(x=>x.Status==ContDiArrivalLookup.Ready)==3 &&
              preview.Single(x=>x.SourcePackageId=="single").Project==
                  "Dự án đúng từ Excel" &&
              preview.All(x=>x.Contract!="88882026") &&
              preview.Any(x=>x.Contract=="99999999"&&x.SourcePackageId==""),
            "One Container scan shows every pending Hưng Yên package and missing contracts, using actual Excel project details");
        var review=ZaloContainerQuickImport.PrepareReview(preview);
        Check(review.Preselected==1 && review.AmbiguousContracts==1 &&
              preview.Single(x=>x.SourcePackageId=="single").Selected &&
              !preview.Single(x=>x.SourcePackageId=="t1").Selected &&
              !preview.Single(x=>x.SourcePackageId=="t2").Selected,
            "One-project HĐ is prechecked; T1/T2 under same HĐ require deliberate package selection");
        var eligible=ContDiArrivalLookup.SelectVerified(
            preview.Where(x=>x.Selected).ToList(),requests,snap,"2026-11-03");
        Check(eligible.Count==1 && eligible[0].OriginPackageId=="single",
            "Precheck only prepares exact physical package IDs for the existing approval gate");
        Check(snap.DetailPackages.Count==0,
            "Preview and auto-selection never write master Excel or convert forecasts into actual arrivals");

        doc.Arrivals[2].SelectedForArrival=false;
        var filtered=ZaloContainerQuickImport.ContractRequests(
            doc.Arrivals.Where(x=>x.SelectedForArrival));
        Check(filtered.Count==3 &&
              filtered.All(x=>Rules.Contract(x.Contract)!="29102026"),
            "Unticking an incoming HĐ removes it from the next scan without affecting another contract");
        return checks;
    }
}
