using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Two-way motor serial regression: manual Excel edits must be visible to
/// HISA and user-entered HISA serials must never overwrite existing Excel
/// rows or assign another tower's motor.
/// Uses an isolated synthetic workbook, not the operator's production file.
/// </summary>
public static class MotorSerialTwoWayDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool pass,string label)
        {
            if(!pass)throw new InvalidOperationException("Motor two-way: "+label);
            checks.Add("Motor two-way: "+label);
        }
        var root=Path.Combine(Path.GetTempPath(),
            "hisa-motor-two-way-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"MÁY KÉO.xlsx");
            var backups=Path.Combine(root,"backups");
            using(var wb=new XLWorkbook())
            {
                var ws=wb.AddWorksheet("Máy kéo");
                ws.Cell(2,4).Value="HĐ";
                ws.Cell(2,9).Value="Số Seri";
                void Row(int i,string project,string serial)
                {
                    ws.Cell(i,2).Value=project;
                    ws.Cell(i,3).Value="Công ty A";
                    ws.Cell(i,4).Value="56062026";
                    ws.Cell(i,5).Value="Máy kéo 11 kW";
                    ws.Cell(i,6).Value="Bộ";
                    ws.Cell(i,7).Value=1;
                    ws.Cell(i,9).Value=serial;
                    ws.Cell(i,10).Value="Giữ nguyên ghi chú";
                    ws.Cell(i,12).FormulaA1="G"+i+"+2";
                }
                Row(3,"Hưng Yên T1","");
                Row(4,"Hưng Yên T1","EXCEL-SERIAL");
                Row(5,"Hưng Yên T2","T2-SERIAL");
                wb.SaveAs(path);
            }
            var settings=new AppSettings{TractionMachineListPath=path};
            Slip Slip()=>new()
            {
                Contracts=["56062026"],Status="planned",
                HandoverDate="2026-11-07",
                Lines=[new MaterialLine
                {
                    Id="motor-1",Contract="56062026",Project="Hưng Yên T1",
                    Company="Công ty A",Material="Máy kéo 11 kW",
                    Unit="Bộ",Quantity=2,Include=true
                }]
            };

            var first=Slip();
            first.MotorSerials=MotorSerials.BuildRows(first);
            first.MotorSerials[0].SerialNumber="HISA-NEW";
            var read=EquipmentListReader.Load(settings);
            var merged=MotorSerialAutoFill.Fill(first,read.Rows);
            Check(merged.Assigned==1 && merged.Missing==0 &&
                  first.MotorSerials.Select(x=>x.SerialNumber)
                      .SequenceEqual(["HISA-NEW","EXCEL-SERIAL"]),
                "HISA-entered NEW serial is retained while existing Excel serial is imported");
            Check(first.Status=="planned" &&
                  merged.Warnings.Any(w=>w.Contains("chưa thấy ở cột I")),
                "Unsynced HISA-only serial is visible and does not mark delivery");

            first.ConfirmedMotorSourceProjects[
                MotorSerialProjectScope.SlipKey("56062026","Hưng Yên T1",
                    "Công ty A")]="Hưng Yên T1";
            var plan=MotorSerialExcelSync.Preview(settings,first);
            Check(plan.Proposed.Count==1 &&
                  plan.Proposed[0].SourceRow==3 &&
                  plan.Proposed[0].Serial=="HISA-NEW",
                "Software-to-Excel preview selects only T1 blank I3");
            var applied=MotorSerialExcelSync.Apply(settings,first,backups,
                plan.Proposed);
            using(var wb=new XLWorkbook(path))
            {
                var ws=wb.Worksheet(1);
                Check(applied.Written==1 &&
                      ws.Cell(3,9).GetString()=="HISA-NEW" &&
                      ws.Cell(4,9).GetString()=="EXCEL-SERIAL" &&
                      ws.Cell(5,9).GetString()=="T2-SERIAL" &&
                      ws.Cell(3,11).IsEmpty() &&
                      ws.Cell(3,12).HasFormula,
                    "Only approved I3 changes; existing serials, K and formulas are preserved");
            }
            Check(Directory.GetFiles(backups,"*.xlsx").Length==1,
                "Writing new serial creates a backup of the original Excel");

            // User edits Excel directly while the application is still open.
            using(var wb=new XLWorkbook(path))
            {
                wb.Worksheet(1).Cell(4,9).Value="EXCEL-EDITED";
                wb.Save();
            }
            var snapshot=File.ReadAllBytes(path);
            var reloaded=EquipmentListReader.Load(settings);
            Check(reloaded.Rows.Single(x=>x.Category=="Máy kéo"&&
                      x.SourceRow==4).Serial=="EXCEL-EDITED" &&
                  File.ReadAllBytes(path).SequenceEqual(snapshot),
                "Reading manual Excel serial changes is fresh and strictly read-only");

            var next=Slip();
            var fromExcel=MotorSerialAutoFill.Fill(next,reloaded.Rows);
            Check(fromExcel.Assigned==2 && fromExcel.Missing==0 &&
                  next.MotorSerials.Select(x=>x.SerialNumber)
                      .SequenceEqual(["HISA-NEW","EXCEL-EDITED"]),
                "Next slip pulls fresh manually edited Excel serials without retyping");
            var anotherTower=Slip();
            anotherTower.MotorSerials=MotorSerials.BuildRows(anotherTower);
            anotherTower.MotorSerials[0].SerialNumber="T2-SERIAL";
            var conflict=MotorSerialAutoFill.Fill(anotherTower,reloaded.Rows);
            Check(conflict.Assigned==0 &&
                  conflict.Warnings.Any(w=>w.Contains("công trình khác") ||
                      w.Contains("công trình") || w.Contains("dòng máy")),
                "T2 serial typed into T1 cannot be assigned across projects");

            var noop=MotorSerialExcelSync.Preview(settings,next);
            Check(noop.Proposed.Count==0 && noop.AlreadyPresent==2,
                "Two-way re-read does not propose duplicate writes for synced serials");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        return checks;
    }
}
