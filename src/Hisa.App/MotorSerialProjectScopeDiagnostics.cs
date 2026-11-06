namespace Hisa.App;

/// <summary>Contract and project disambiguation, entirely offline.</summary>
public static class MotorSerialProjectScopeDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool valid,string label)
        {
            if(!valid)throw new InvalidOperationException("Motor project scope: "+label);
            checks.Add("Motor project scope: "+label);
        }

        var rows=new List<EquipmentListReader.Row>
        {
            new(){Category="Máy kéo",Contract="56062026",Project="Hưng Yên T1",
                Company="A",Material="Máy kéo YJ160",Serial="T1-001",SourceRow=3},
            new(){Category="Máy kéo",Contract="56062026",Project="Hưng Yên T1",
                Company="A",Material="Máy kéo YJ160",Serial="",SourceRow=4},
            new(){Category="Máy kéo",Contract="56062026",Project="Hưng Yên T2",
                Company="A",Material="Máy kéo YJ160",Serial="T2-001",SourceRow=5},
            new(){Category="Máy kéo",Contract="77772026",Project="Công trình độc lập",
                Company="A",Material="Máy kéo",Serial="OTHER",SourceRow=6}
        };
        var options=MotorSerialProjectScope.Projects(rows,"56062026");
        Check(options.Count==2&&options.Any(x=>x.Contains("T1"))&&
              options.Any(x=>x.Contains("T2")),
            "Contract D with multiple project B values exposes explicit choices");
        var t1=MotorSerialProjectScope.Resolve(rows,"56062026","Hưng Yên T1","A");
        Check(t1.MultipleProjects&&t1.Warning==null&&
              t1.Rows.Select(x=>x.SourceRow).SequenceEqual([3,4]),
            "Resolve T1 using both contract and project, never T2");
        var t2=MotorSerialProjectScope.Resolve(rows,"56062026","Hưng Yên T2","A");
        Check(t2.Rows.Count==1&&t2.Rows[0].SourceRow==5,
            "Resolve T2 separately for same contract");
        var unselected=MotorSerialProjectScope.Resolve(rows,"56062026","","A");
        Check(unselected.Rows.Count==0&&unselected.Warning!=null,
            "Missing project must not silently use the first source project");
        var wrong=MotorSerialProjectScope.Resolve(rows,"56062026","Hưng Yên T3","A");
        Check(wrong.Rows.Count==0&&wrong.Warning!=null,
            "Wrong slip project cannot claim an existing source serial");
        var uniqueWrong=MotorSerialProjectScope.Resolve(rows,"77772026",
            "Một công trình khác","A");
        Check(uniqueWrong.Rows.Count==0&&uniqueWrong.Warning!=null,
            "Unique contract still must not silently change a known project");
        var companies=new List<EquipmentListReader.Row>
        {
            new(){Category="Máy kéo",Contract="88882026",Project="Công trình T1",
                Company="A",SourceRow=10},
            new(){Category="Máy kéo",Contract="88882026",Project="Công trình T1",
                Company="B",SourceRow=11}
        };
        Check(MotorSerialProjectScope.Resolve(companies,"88882026","Công trình T1","").Warning!=null,
            "Same contract and project with multiple companies requires company");
        Check(MotorSerialProjectScope.Resolve(companies,"88882026","Công trình T1","B")
            .Rows.Single().SourceRow==11,
            "Different companies are not mixed");
        var blankProjects=new List<EquipmentListReader.Row>
        {
            new(){Category="Máy kéo",Contract="99992026",SourceRow=20},
            new(){Category="Máy kéo",Contract="99992026",SourceRow=21}
        };
        Check(MotorSerialProjectScope.Resolve(blankProjects,"99992026","A","B")
            .Warning!=null,
            "Multiple source rows with missing projects cannot be guessed");
        return checks;
    }
}
