using System.IO;
using System.Text.Json;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Pure source lookup tests; no live source or Gemini request.</summary>
public static class ContDiArrivalLookupDiagnostics
{
    /// <summary>Without Container, searching must fail rather than use stale master data.</summary>
    public static async Task<List<string>> RunWorkbookAsync()
    {
        var checks=new List<string>();
        var root=SelfTests.CreateDemoWorkspace();
        try
        {
            var folder=Path.Combine(root,"CONT ĐI");
            Directory.Delete(folder,true);
            var settings=new AppSettings
            {
                MasterPath=Path.Combine(root,"Tổng hợp.xlsx"),
                KtPath=Path.Combine(root,"KT tổng hợp.xlsx"),
                ProductExportPath=Path.Combine(root,"Thành phẩm xuất kho.xlsx"),
                DataSources=[],AutoRefresh=false,AutoSyncCont=false,AutoSyncKt=false
            };
            File.WriteAllText(Path.Combine(root,"native_data","settings.json"),
                JsonSerializer.Serialize(settings,WarehouseService.Json));
            using var service=new WarehouseService(root);
            await service.InitializeAsync();
            if(service.ContDiLookupSnapshot.ContPackages.Count!=0)
                throw new InvalidOperationException("Arrival scan used old master Cont đi rows.");
            checks.Add("Container: no automatic lookup from the historical Cont đi sheet");
            bool blocked=false;
            try{await service.RefreshContDiForArrivalAsync();}
            catch(DirectoryNotFoundException){blocked=true;}
            if(!blocked || !File.Exists(settings.MasterPath))
                throw new InvalidOperationException("Missing Container was not safely blocked.");
            checks.Add("Container: missing directory blocks receipt without changing master Excel");
            return checks;
        }
        finally
        {
            try{Directory.Delete(root,true);}
            catch(IOException){/* Temporary Windows file locks may delay cleanup. */}
            catch(UnauthorizedAccessException){/* Preserve test data for diagnosis. */}
        }
    }

    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string name)
        {
            if(!ok)throw new InvalidOperationException("CONT ĐI lookup: "+name);
            checks.Add("CONT ĐI: "+name);
        }
        Package Pack(string id,string contract,string project,string cont,string departure,
            bool hungYen=true) => new()
        {
            Id=id,Contract=contract,Project=project,Company="Công ty A",
            ContainerNumber=cont,SourceDate=departure,PackageNo=id=="a"?1:2,
            SourceFile="Nguồn cont đi.xlsx",
            Items=[new MaterialLine
            {
                Contract=contract,Project=project,Company="Công ty A",
                Material="Máy kéo",Unit="Bộ",Quantity=2,IsHungYen=hungYen
            }]
        };
        var snapshot=new WorkbookSnapshot
        {
            ContPackages=[
                Pack("a","05492026","Dự án T1","CONT-001","2026-10-25"),
                Pack("b","05492026","Dự án T2","CONT-002","2026-10-26"),
                Pack("c","0123(1)","Dự án (1)","","2026-10-27"),
                Pack("other","77772026","Hà Nội","","2026-10-27",false)
            ]
        };
        const string day="2026-11-02";
        var manual=ContDiArrivalLookup.FromContracts("05492026\n0123(1)\n01231");
        var preview=ContDiArrivalLookup.Preview(manual,snapshot,day);
        Check(preview.Count(x=>x.Contract=="05492026")==2 &&
              preview.Count(x=>x.Status==ContDiArrivalLookup.Ready)==3,
            "Exact manual contract lookup shows distinct T1 and T2 packages");
        Check(preview.Any(x=>x.Contract=="01231" && x.SourcePackageId.Length==0) &&
              preview.Any(x=>x.Contract=="0123(1)" && x.SourcePackageId=="c"),
            "Parenthesized contract never fuzzy-matches unrelated number");
        Check(!preview.Any(x=>x.Contract=="77772026"),
            "Hà Nội / non-Hưng Yên goods not selected");
        var selected=ContDiArrivalLookup.SelectVerified(
            [preview.Single(x=>x.SourcePackageId=="b")],manual,snapshot,day);
        Check(selected.Count==1 && selected[0].Id=="b" &&
              selected[0].OriginPackageId=="b" &&
              selected[0].Project=="Dự án T2",
            "Explicitly selected physical package is copied with immutable provenance");
        var image=new List<AiScheduleLine>
        {
            new(){Contract="05492026",Project="Dự án T2",
                Company="Công ty A",Container="CONT-002",
                ContainerExportDate="2026-10-26"}
        };
        var imageRows=ContDiArrivalLookup.Preview(image,snapshot,day);
        Check(imageRows.Count(x=>x.Status==ContDiArrivalLookup.Ready)==1 &&
              imageRows.Single(x=>x.SourcePackageId=="a").Status.Contains("Lệch"),
            "Zalo image can search by contract while flagging different T1/cont/day");
        var bad=image.Select(x=>new AiScheduleLine
        {
            Contract=x.Contract,Project=x.Project,
            Company=x.Company,Container=x.Container,
            ContainerExportDate="2026-10-29"
        }).ToList();
        Check(!ContDiArrivalLookup.Preview(bad,snapshot,day)
            .Any(x=>x.Status==ContDiArrivalLookup.Ready),
            "Incorrect container departure day blocks import");
        snapshot.DetailPackages.Add(Pack("history","05492026","Dự án T2","CONT-002","2026-10-26"));
        snapshot.DetailPackages[0].OriginPackageId="b";
        snapshot.DetailPackages[0].ArrivalDate="2026-10-30";
        var hidden=ContDiArrivalLookup.Preview(manual,snapshot,day);
        var receipts=ContDiArrivalLookup.PreviouslyReceived(manual,snapshot);
        Check(!hidden.Any(x=>x.SourcePackageId=="b") &&
              hidden.Any(x=>x.SourcePackageId=="a") &&
              hidden.Any(x=>x.SourcePackageId=="c") &&
              receipts.Count==1 &&
              receipts[0].SourcePackageId=="b" &&
              receipts[0].ArrivalDays.Contains("30/10/2026") &&
              receipts[0].Description.Contains("Dự án T2"),
            "Hide only the already-received physical source ID; show actual imported day and T2 from master");
        var unrelated=ContDiArrivalLookup.PreviouslyReceived(
            ContDiArrivalLookup.FromContracts("05492026(1)"),snapshot);
        Check(unrelated.Count==0,"Same-looking contract suffix never receives another contract's history");
        var historyOnly=new WorkbookSnapshot{DetailPackages=snapshot.DetailPackages.ToList()};
        Check(ContDiArrivalLookup.Preview(manual,historyOnly,day)
              .All(x=>x.SourcePackageId.Length==0) &&
              ContDiArrivalLookup.PreviouslyReceived(manual,historyOnly).Count==1,
            "Archived Container source stays hidden but prior receipt date remains visible");
        // Zalo can name one tower; do not imply a receipt from another tower.
        Check(ContDiArrivalLookup.PreviouslyReceived(
            [new AiScheduleLine{Contract="05492026",Project="Dự án T1"}],snapshot).Count==0,
            "Historical receipt from T2 is not mistaken for a new T1 delivery");
        bool rejected=false;
        try{ContDiArrivalLookup.SelectVerified(
            [preview.Single(x=>x.SourcePackageId=="b")],manual,snapshot,day);}
        catch(InvalidOperationException ex)
        {
            rejected=ex.Message.Contains("30/10/2026") &&
                ex.Message.Contains("KHÔNG NHẬP TRÙNG");
        }
        Check(rejected,"Stale checkbox is rejected with exact prior receipt date rather than generic changed-source warning");
        snapshot.DetailPackages.Clear();
        // A legacy receipt with no source package ID is not proven duplicate.
        // Warn with its date and disallow automatic import until reconciliation.
        var legacy=Pack("legacy","05492026","Dự án T1","CONT-001","2026-10-25");
        legacy.OriginPackageId="";
        legacy.ArrivalDate="2026-10-29";
        snapshot.DetailPackages.Add(legacy);
        var legacyRows=ContDiArrivalLookup.Preview(manual,snapshot,day);
        Check(legacyRows.Any(x=>x.SourcePackageId=="a" &&
              x.Status=="Cần đối chiếu kiện nhập cũ" &&
              x.Notes.Contains("29/10/2026")) &&
              legacyRows.Any(x=>x.SourcePackageId=="b" && x.Status==ContDiArrivalLookup.Ready),
            "Legacy same-goods receipts display saved date but do not hide unrelated T2 or assume exact source ID");
        snapshot.DetailPackages.Clear();
        snapshot.ContPackages[1].Items[0].Quantity=3;
        rejected=false;
        try{ContDiArrivalLookup.SelectVerified(
            [preview.Single(x=>x.SourcePackageId=="b")],manual,snapshot,day);}
        catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Changes in source material quantity require new lookup and approval");
        var absent=ContDiArrivalLookup.Preview(
            ContDiArrivalLookup.FromContracts("99999999"),snapshot,day);
        Check(absent.Count==1 && absent[0].Status.Contains("Chưa tìm thấy"),
            "Missing exact contract must never invent a package");
        return checks;
    }
}
