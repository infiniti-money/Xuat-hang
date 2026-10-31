namespace Hisa.App;

/// <summary>Pure source lookup tests; no live source or Gemini request.</summary>
public static class ContDiArrivalLookupDiagnostics
{
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
        Check(ContDiArrivalLookup.Preview(manual,snapshot,day)
            .Single(x=>x.SourcePackageId=="b").Status=="Kiện đã nhập kho",
            "Existing package origin ID prevents duplicate receipt");
        bool rejected=false;
        try{ContDiArrivalLookup.SelectVerified(
            [preview.Single(x=>x.SourcePackageId=="b")],manual,snapshot,day);}
        catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Revalidation blocks a previously selectable but now imported package");
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
