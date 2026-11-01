using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Hisa.App;

/// <summary>
/// Exact motor serial source -> handover slip -> printed document regression.
/// No production workbook, printer, external Gemini or shipped ledger is used.
/// </summary>
public static class MotorSerialAutoFillDiagnostics
{
    private static IEnumerable<DependencyObject> Walk(DependencyObject node)
    {
        yield return node;
        foreach(var item in LogicalTreeHelper.GetChildren(node))
            if(item is DependencyObject child)
                foreach(var nested in Walk(child))yield return nested;
    }
    private static string PrintedText(Slip slip,string layout)
    {
        var doc=PrintService.BuildSlipDocument(slip,new PrintTemplate{Layout=layout});
        return string.Join("\n",doc.Pages.SelectMany(p=>Walk(p.Child))
            .OfType<TextBlock>().Select(x=>x.Text));
    }

    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool condition,string name)
        {
            if(!condition)throw new InvalidOperationException("Motor source serial: "+name);
            checks.Add("Motor source serial: "+name);
        }
        Slip NewSlip(string project="Hưng Yên T1",int quantity=2)=>new()
        {
            HandoverDate="2026-11-03",Status="planned",
            Contracts=["56062026"],
            Lines=[new MaterialLine
            {
                Contract="56062026",Project=project,Company="Công ty A",
                Material="Máy kéo 11 kW",Quantity=quantity,Unit="Bộ"
            }]
        };
        var source=new List<EquipmentListReader.Row>
        {
            new(){Category="Máy kéo",Contract="56062026",Project="Hưng Yên T1",
                Company="Công ty A",Material="Máy kéo 11 kW",Serial="HY-SERIAL-101",SourceRow=8},
            new(){Category="Máy kéo",Contract="56062026",Project="Hưng Yên T1",
                Company="Công ty A",Material="Máy kéo 11 kW",Serial="HY-SERIAL-102",SourceRow=9},
            new(){Category="Máy kéo",Contract="56062026",Project="Hưng Yên T2",
                Company="Công ty A",Material="Máy kéo 11 kW",Serial="HY-OTHER-T2",SourceRow=10}
        };
        var slip=NewSlip();
        var first=MotorSerialAutoFill.Fill(slip,source);
        Check(first.Assigned==2&&first.Missing==0&&
            slip.MotorSerials.Select(x=>x.SerialNumber).SequenceEqual(
                ["HY-SERIAL-101","HY-SERIAL-102"]),
            "Import physical Excel serials into each matching machine in the handover slip");
        Check(slip.Status=="planned"&&slip.SlipCode=="",
            "Importing serials does not approve/export the slip or reserve a slip code");
        foreach(var layout in new[]{"internal_summary","contract_delivery","standard"})
        {
            var printed=PrintedText(slip,layout);
            Check(printed.Contains("HY-SERIAL-101")&&
                  printed.Contains("HY-SERIAL-102")&&
                  !printed.Contains("HY-OTHER-T2"),
                "Printed "+layout+" slip includes BOTH source serials, never T2 serial");
        }
        var again=MotorSerialAutoFill.Fill(slip,source);
        Check(again.Assigned==0&&again.Missing==0&&
            slip.MotorSerials[0].SerialNumber=="HY-SERIAL-101",
            "Refreshing preview never replaces existing serials or duplicates entries");
        var userEntered=NewSlip();
        userEntered.MotorSerials=MotorSerials.BuildRows(userEntered);
        userEntered.MotorSerials[0].SerialNumber="HY-SERIAL-102";
        var manual=MotorSerialAutoFill.Fill(userEntered,source);
        Check(manual.Assigned==1&&manual.Missing==0&&
              userEntered.MotorSerials[0].SerialNumber=="HY-SERIAL-102"&&
              userEntered.MotorSerials[1].SerialNumber=="HY-SERIAL-101",
            "Preserve manually entered serial and fill only missing unit from Excel");
        var fallback=NewSlip(quantity:1);
        var modelDifferent=new List<EquipmentListReader.Row>
        {
            new(){Category="Máy kéo",Contract="56062026",Project="Hưng Yên T1",
                Company="Công ty A",Material="Model YJ-160",Serial="HY-MODEL-FALLBACK",SourceRow=7}
        };
        var result=MotorSerialAutoFill.Fill(fallback,modelDifferent);
        Check(result.Assigned==1&&fallback.MotorSerials[0].SerialNumber=="HY-MODEL-FALLBACK",
            "Single unambiguous machine may match across differently named model text");
        var duplicate=NewSlip();
        var duplicatedSource=source.Concat([
            new EquipmentListReader.Row{Category="Máy kéo",Contract="12345678",
                Project="Khác",Company="B",Material="Máy kéo 11 kW",
                Serial="HY-SERIAL-101",SourceRow=11}
        ]).ToList();
        var dup=MotorSerialAutoFill.Fill(duplicate,duplicatedSource);
        Check(dup.Assigned==0&&dup.Missing==2,
            "Duplicated physical Excel serial blocks automatic assignment of whole group");
        var history=new Slip
        {
            Status="delivered",SlipCode="XUAT-001",HandoverDate="2026-11-01",
            Contracts=["56062026"],
            Lines=[new MaterialLine{Id="history-motor",Contract="56062026",
                Project="Hưng Yên T1",Company="Công ty A",
                Material="Máy kéo 11 kW",Quantity=1}]
        };
        history.MotorSerials=MotorSerials.BuildRows(history);
        history.MotorSerials[0].SerialNumber="HY-SERIAL-101";
        var newSlip=NewSlip();
        var previouslyShipped=MotorSerialAutoFill.Fill(newSlip,source,[history]);
        Check(previouslyShipped.Assigned==0&&previouslyShipped.Missing==2,
            "Serial used on an already delivered slip is never assigned to a new slip");
        return checks;
    }
}
