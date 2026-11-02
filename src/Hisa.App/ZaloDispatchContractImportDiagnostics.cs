namespace Hisa.App;

/// <summary>Fixtures for importing only explicitly chosen outgoing Zalo contracts.</summary>
public static class ZaloDispatchContractImportDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string title)
        {
            if(!ok)throw new InvalidOperationException("Zalo dispatch to slip: "+title);
            checks.Add(title);
        }
        var outT1=new AiScheduleLine{Contract="10392026",Project="Công trình A T1",
            Company="Công ty A",PlannedDate="2026-11-02"};
        var outT2=new AiScheduleLine{Contract="10392026",Project="Công trình A T2",
            Company="Công ty A",PlannedDate="2026-11-02"};
        var outB=new AiScheduleLine{Contract="05492026",Project="Công trình B",
            Company="Công ty B",PlannedDate="2026-11-02"};
        var arrival=new AiScheduleLine{Contract="88882026",Project="Công trình nhập",
            Company="Công ty A",PlannedDate="2026-11-02"};
        var schedule=new AiScheduleDocument
        {
            Arrivals=[arrival],Dispatches=[outT1,outT2,outB],
            DispatchDate="2026-11-02"
        };
        var merged=ZaloDispatchContractImport.Merge(schedule,[outT1,outT2,outB],
            "22222026\n05492026");
        // Compare the displayed contract codes, not the normalized lookup keys:
        // "05492026" must keep its leading zero in the input field.
        var actual=Rules.Tokens(merged.ContractsText).ToArray();
        Check(actual.SequenceEqual(["22222026","05492026","10392026"]) &&
              merged.AddedContractCodes==1,
            "Keep manual contract and existing outbound contract; dedupe repeated T1/T2 code");
        Check(merged.SelectedProjects==3 &&
              merged.ProjectTargets.Count(x=>Rules.Contract(x.Contract)=="10392026")==2 &&
              merged.ProjectTargets.Select(x=>x.Identity).Distinct().Count()==3,
            "Two towers with the same contract remain distinct exact project/company targets");
        Check(schedule.Arrivals.Count==1 && schedule.Dispatches.Count==3 &&
              string.IsNullOrEmpty(schedule.ApprovedAt) &&
              outT1.SelectedForHandover==false,
            "Import is in-memory only and does not confirm incoming cont, approve the schedule or ship");
        bool incomingBlocked=false;
        try{ZaloDispatchContractImport.Merge(schedule,[arrival],"");}
        catch(InvalidOperationException ex)
        {incomingBlocked=ex.Message.Contains("HÀNG DỰ KIẾN XUẤT KHO");}
        Check(incomingBlocked,"Incoming cont rows cannot be imported as outgoing slips");
        bool noneBlocked=false;
        try{ZaloDispatchContractImport.Merge(schedule,[],"");}
        catch(InvalidOperationException){noneBlocked=true;}
        Check(noneBlocked,"Unselected dispatch entries are never imported implicitly");
        var unclear=new AiScheduleLine{Contract="10392026",Project="",
            Company="Công ty A",NeedsReview=true};
        schedule.Dispatches.Add(unclear);
        bool ambiguityBlocked=false;
        try{ZaloDispatchContractImport.Merge(schedule,[unclear],"");}
        catch(InvalidOperationException){ambiguityBlocked=true;}
        Check(ambiguityBlocked,"Ambiguous contract without confirmed project is blocked");
        var single=ZaloDispatchContractImport.Merge(schedule,[outB],"22222026");
        Check(Rules.Tokens(single.ContractsText).Count==2 &&
              single.ProjectTargets.Single().Identity==
                  Rules.Identity(outB.Contract,outB.Project,outB.Company),
            "Only checked outgoing project is added, not every contract from the Zalo image");
        return checks;
    }
}
