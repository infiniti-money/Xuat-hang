using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>Isolated regression coverage for manual photo+code schedule workflow.</summary>
public static class AiWorkspaceDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string name)
        {
            if(!ok)throw new InvalidOperationException("AI workspace regression: "+name);
            checks.Add("AI workspace: "+name);
        }
        const string day="2026-10-31";
        var packages=new List<Package>
        {
            new() {Id="source-A-26",Contract="56062026",Project="Công trình A T1",
                Company="HISA",SourceDate="2026-10-26",
                Items=[new MaterialLine{Contract="56062026",Project="Công trình A T1",
                    Company="HISA",Material="Đầu cửa tầng 700",Unit="Bộ",Quantity=1}]},
            new() {Id="source-A-27",Contract="56062026",Project="Công trình A T1",
                Company="HISA",SourceDate="2026-10-27",
                Items=[new MaterialLine{Contract="56062026",Project="Công trình A T1",
                    Company="HISA",Material="Đầu cửa tầng 700",Unit="Bộ",Quantity=1}]},
            new() {Id="source-B",Contract="13482026",Project="Công trình B T2",
                Company="HISA",SourceDate="2026-10-26",
                Items=[new MaterialLine{Contract="13482026",Project="Công trình B T2",
                    Company="HISA",Material="Ray dẫn hướng",Unit="Bộ",Quantity=1}]}
        };
        var snapshot=new WorkbookSnapshot{ContPackages=packages,Packages=packages};
        var doc=AiScheduleWorkspace.NewDraft(new DateTime(2026,10,31));
        Check(doc.ArrivalDate==day && doc.DispatchDate==day && doc.ApprovedAt=="",
            "New manually entered schedules are unapproved and date-scoped");
        var added=AiScheduleWorkspace.AddManual(doc,snapshot,false,"56062026\n13482026",
            "","","","","Khách yêu cầu",true);
        Check(added==2 && doc.Dispatches.Count==2 &&
            doc.Dispatches.All(x=>x.ManualEntry && x.Urgent && !x.NeedsReview),
            "Multi-line dispatch codes are enriched only with exact unique project and company");
        Check(doc.Dispatches[0].Project=="Công trình A T1" &&
            doc.Dispatches[1].Project=="Công trình B T2",
            "Different projects are identified per contract without cross-contamination");
        Check(AiScheduleWorkspace.AddManual(doc,snapshot,false,"56062026",
            "","","","","",false)==0,"Repeating identical handover contract does not add a duplicate");
        var extra=AiScheduleWorkspace.AddManual(doc,snapshot,false,"88882026",
            "","","","","",false);
        Check(extra==1 && doc.Dispatches.Last().NeedsReview &&
            doc.Dispatches.Last().Project.Length==0,
            "Unrecognized manual contract stays visible but unconfirmed, not marked missing goods");
        var arrival=AiScheduleWorkspace.AddManual(doc,snapshot,true,"56062026",
            "","","CONT-01","2026-10-26","Hàng dự kiến về",false);
        Check(arrival==1 && doc.Arrivals.Single().ManualEntry &&
            doc.Arrivals.Single().ContainerExportDate=="2026-10-26" &&
            doc.Arrivals.Single().Project=="Công trình A T1",
            "Manually added inbound contract keeps verified factory departure day");
        var next=AiScheduleWorkspace.AddManual(doc,snapshot,true,"56062026",
            "","","CONT-02","2026-10-27","",false);
        Check(next==1 && doc.Arrivals.Count==2 &&
            doc.Arrivals.Select(x=>x.ContainerExportDate).Distinct().Count()==2,
            "Same contract and material sent on two different days remain distinct scheduled arrivals");
        doc.ApprovedAt=DateTimeOffset.Now.ToString("O");
        doc.Arrivals[0].SelectedForArrival=true;
        var incoming=AiScheduleWorkspace.SelectedArrivalPreview(doc,snapshot,day);
        Check(incoming.Count==1 && incoming[0].SourcePackageId=="source-A-26",
            "Selecting one contract container/date does not select the other physical departure");
        AiScheduleWorkspace.PreselectUnique(incoming);
        Check(incoming.Single().Selected,
            "One independently verified physical package may be preselected but not imported");
        doc.Arrivals[1].SelectedForArrival=true;
        var both=AiScheduleWorkspace.SelectedArrivalPreview(doc,snapshot,day);
        Check(both.Count==2 && both.Select(x=>x.SourcePackageId).Distinct().Count()==2,
            "Multiple explicitly selected source departure days are kept separate");
        Check(snapshot.DetailPackages.Count==0,
            "Contract selection cannot record physical stock receipt");
        var invalid=false;
        try {AiScheduleWorkspace.AddManual(doc,snapshot,true,"123456",
            "","","","2026-11-01","",false);}
        catch(InvalidOperationException){invalid=true;}
        Check(invalid,"Factory departure after planned warehouse arrival is rejected");
        invalid=false;
        try{AiScheduleWorkspace.AddManual(doc,snapshot,false,new string('1',82),
            "","","","","",false);}
        catch(InvalidOperationException){invalid=true;}
        Check(invalid,"Malformed manual contract input is rejected");

        var ambiguousSnapshot=new WorkbookSnapshot
        {
            Packages=[new Package{Contract="12345678",Project="Thang 1",Company="HISA"},
                new Package{Contract="12345678",Project="Thang 2",Company="HISA"}]
        };
        var uncertain=AiScheduleWorkspace.NewDraft(new DateTime(2026,10,31));
        AiScheduleWorkspace.AddManual(uncertain,ambiguousSnapshot,false,"12345678",
            "","","","","",false);
        Check(uncertain.Dispatches[0].NeedsReview &&
            uncertain.Dispatches[0].Project.Length==0,
            "Same contract across T1/T2 never silently picks a tower");
        AiScheduleWorkspace.AddManual(uncertain,ambiguousSnapshot,false,"12345678",
            "Thang 2","","","","",false);
        Check(uncertain.Dispatches.Count==2 &&
            uncertain.Dispatches[1].Project=="Thang 2",
            "Explicit subproject identifier can be entered as independent schedule row");

        var temp=Path.Combine(Path.GetTempPath(),"hisa-ai-workspace-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp,"ai_schedules"));
        try
        {
            var onlyDispatch=AiScheduleWorkspace.NewDraft(new DateTime(2026,10,31));
            onlyDispatch.Dispatches.Add(new AiScheduleLine{
                Contract="56062026",Project="Công trình A T1",Company="HISA",
                PlannedDate=day,ManualEntry=true
            });
            onlyDispatch.ApprovedAt=DateTimeOffset.Now.ToString("O");
            File.WriteAllText(Path.Combine(temp,"ai_schedules","lich-kho-20261031-xyz.json"),
                JsonSerializer.Serialize(onlyDispatch,WarehouseService.Json));
            var loaded=AiScheduleWorkspace.LatestApproved(temp);
            Check(loaded!=null && loaded.Arrivals.Count==0 &&
                  loaded.Dispatches.Single().ManualEntry,
                "Approved dispatch-only manual schedules reopen after restarting HISA");
        }
        finally {Directory.Delete(temp,true);}
        return checks;
    }
}
