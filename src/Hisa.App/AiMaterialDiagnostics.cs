namespace Hisa.App;

/// <summary>No real warehouse files or Gemini calls: only synthetic material snapshots.</summary>
public static class AiMaterialDiagnostics
{
    public static List<string> Run()
    {
        var results=new List<string>();
        void Check(bool okay,string label)
        {
            if(!okay)throw new InvalidOperationException("AI materials regression: "+label);
            results.Add("AI materials: "+label);
        }
        var snapshot=new WorkbookSnapshot
        {
            Packages=[
                new Package{Id="A1",Contract="10002026",Project="Tòa nhà T1",
                    Company="HISA",PackageNo=1,SourceDate="2026-10-25",
                    Items=[new MaterialLine{Contract="10002026",Project="Tòa nhà T1",
                        Company="HISA",Material="Máy kéo M1",Unit="Bộ",Quantity=2}]},
                new Package{Id="A2",Contract="10002026",Project="Tòa nhà T1",
                    Company="HISA",PackageNo=2,SourceDate="2026-10-26",ArrivalDate="2026-10-31",
                    Items=[new MaterialLine{Contract="10002026",Project="Tòa nhà T1",
                        Company="HISA",Material="Đầu cửa tầng 700",Unit="Bộ",Quantity=5}]},
                new Package{Id="B1",Contract="10002026",Project="Tòa nhà T2",
                    Company="HISA",PackageNo=1,SourceDate="2026-10-27",
                    Items=[new MaterialLine{Contract="10002026",Project="Tòa nhà T2",
                        Company="HISA",Material="Tủ điện",Unit="Bộ",Quantity=3}]},
                new Package{Id="C1",Contract="20002026",Project="Khu khác",
                    Company="HISA",PackageNo=1,SourceDate="2026-10-28",
                    Items=[new MaterialLine{Contract="20002026",Project="Khu khác",
                        Company="HISA",Material="Ray dẫn hướng",Unit="mét",Quantity=12}]}
            ]
        };
        var t1=new AiScheduleLine{Contract="10002026",Project="Tòa nhà T1",Company="HISA"};
        var exact=AiMaterialLookup.ForContract(t1,snapshot);
        Check(exact.Exact && exact.CanVerify && exact.Items.Count==2 &&
              exact.Items.All(x=>x.Project=="Tòa nhà T1") &&
              exact.Items.Any(x=>x.Material=="Máy kéo M1"&&x.Quantity==2) &&
              exact.Items.Any(x=>x.Material=="Đầu cửa tầng 700"&&x.Quantity==5),
            "Exact contract/project/company displays both physical packages and real quantities");
        Check(exact.Items.Any(x=>x.SourceDate=="2026-10-25"&&x.ArrivalDate=="") &&
              exact.Items.Any(x=>x.SourceDate=="2026-10-26"&&x.ArrivalDate=="2026-10-31"),
            "Material list separates factory departure from actual receipt day");
        var ambiguous=AiMaterialLookup.ForContract(new AiScheduleLine{Contract="10002026"},snapshot);
        Check(!ambiguous.CanVerify && ambiguous.Items.Count==1 &&
              ambiguous.Items[0].Material=="—" &&
              ambiguous.Message.Contains("nhiều công trình"),
            "Contract alone never combines T1 and T2 or invents material");
        var wrongCompany=AiMaterialLookup.ForContract(
            new AiScheduleLine{Contract="10002026",Project="Tòa nhà T1",Company="KHÁC"},snapshot);
        Check(!wrongCompany.CanVerify && wrongCompany.Items.Count==1,
            "Wrong company cannot silently choose a project when contract has multiple identities");
        var single=AiMaterialLookup.ForContract(
            new AiScheduleLine{Contract="20002026",Project="Chưa biết",Company=""},snapshot);
        Check(!single.CanVerify && single.Items.Count==1 &&
              single.Items[0].Material=="Ray dẫn hướng" &&
              single.Message.Contains("tham khảo"),
            "Sole contract fallback is visibly flagged as reference-only");
        var missing=AiMaterialLookup.ForContract(
            new AiScheduleLine{Contract="99992026",Project="Chưa thấy"},snapshot);
        Check(missing.Items.Count==1 && missing.Items[0].Material=="—" &&
              !missing.CanVerify,"Missing contract yields explicit warning, never fabricated goods");
        var all=AiMaterialLookup.ForAll([t1,new AiScheduleLine{Contract="99992026"}],snapshot);
        Check(all.Count==3 && all.Count(x=>x.Contract=="10002026")==2 &&
              all.Count(x=>x.Contract=="99992026")==1,
            "All contracts report contains exact material lines and placeholders for missing contracts");
        Check(snapshot.Packages.All(p=>p.Items.All(x=>x.Include)),
            "Lookup never mutates warehouse quantity, checkbox or receipt history");
        return results;
    }
}
