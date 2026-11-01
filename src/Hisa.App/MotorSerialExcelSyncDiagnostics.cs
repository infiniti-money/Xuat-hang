using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Test original MÁY KÉO.xlsx writeback without ever touching live data.
/// </summary>
public static class MotorSerialExcelSyncDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool v,string label)
        {
            if(!v)throw new InvalidOperationException("Motor serial Excel: "+label);
            checks.Add("Motor serial Excel: "+label);
        }
        var root=Path.Combine(Path.GetTempPath(),
            "hisa-serial-excel-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source=Path.Combine(root,"MÁY KÉO.xlsx");
        var backups=Path.Combine(root,"Backups");
        try
        {
            using(var workbook=new XLWorkbook())
            {
                var sheet=workbook.AddWorksheet("DS máy kéo");
                sheet.Cell(2,4).Value="HĐ";
                sheet.Cell(2,9).Value="Số Seri";
                for(int i=3;i<=5;i++)
                {
                    sheet.Cell(i,2).Value=i==5?"Hưng Yên T2":"Hưng Yên T1";
                    sheet.Cell(i,3).Value="Công ty A";
                    sheet.Cell(i,4).Value="56062026";
                    sheet.Cell(i,5).Value="Máy kéo 11 kW";
                    sheet.Cell(i,6).Value="Bộ";
                    sheet.Cell(i,7).Value=1;
                    sheet.Cell(i,10).Value="Không sửa ghi chú";
                    sheet.Cell(i,11).Value="Chưa làm";
                    sheet.Cell(i,12).FormulaA1="G"+i+"+2";
                }
                sheet.Cell(5,9).Value="SERIAL-T2";
                workbook.SaveAs(source);
            }
            var settings=new AppSettings{TractionMachineListPath=source};
            var slip=new Slip{Contracts=["56062026"],
                Lines=[new MaterialLine{Id="motor-1",Contract="56062026",
                    Project="Hưng Yên T1",Company="Công ty A",
                    Material="Máy kéo 11 kW",Quantity=2,Include=true}]};
            slip.MotorSerials=MotorSerials.BuildRows(slip);
            slip.MotorSerials[0].SerialNumber="SERIAL-T1-1";
            slip.MotorSerials[1].SerialNumber="SERIAL-T1-2";
            var before=File.ReadAllBytes(source);
            var pending=MotorSerialExcelSync.Preview(settings,slip);
            Check(pending.Proposed.Count==2&&pending.Written==0 &&
                  File.ReadAllBytes(source).SequenceEqual(before),
                  "Preview proposes exact two empty source rows without changing Excel");
            var done=MotorSerialExcelSync.Apply(settings,slip,backups);
            Check(done.Written==2&&Directory.GetFiles(backups,"*.xlsx").Length==1,
                "Confirmed manual serials persist in original Excel and create backup");
            using(var workbook=new XLWorkbook(source))
            {
                var sheet=workbook.Worksheets.First();
                Check(sheet.Cell(3,9).GetString()=="SERIAL-T1-1" &&
                      sheet.Cell(4,9).GetString()=="SERIAL-T1-2" &&
                      sheet.Cell(5,9).GetString()=="SERIAL-T2",
                      "All and only T1 serial cells in column I are updated; T2 stays separate");
                Check(sheet.Cell(3,11).GetString()=="Chưa làm" &&
                      sheet.Cell(4,10).GetString()=="Không sửa ghi chú" &&
                      sheet.Cell(4,12).HasFormula,
                      "Writing serial never changes status column K, notes or formulas");
            }
            var again=MotorSerialExcelSync.Apply(settings,slip,backups);
            Check(again.Written==0&&again.AlreadyPresent==2,
                "Rerun is idempotent and does not write or back up twice");
            var wrong=new Slip{Contracts=["56062026"],
                Lines=[new MaterialLine{Id="wrong",Contract="56062026",
                    Project="Hưng Yên T1",Company="Công ty A",
                    Material="Máy kéo 11 kW",Quantity=1,Include=true}]};
            wrong.MotorSerials=MotorSerials.BuildRows(wrong);
            wrong.MotorSerials[0].SerialNumber="SERIAL-T2";
            var conflict=MotorSerialExcelSync.Preview(settings,wrong);
            Check(conflict.Proposed.Count==0&&conflict.Warnings.Count>0,
                "Serial already present in another project is rejected");
            using(var workbook=new XLWorkbook(source))
            {
                var sheet=workbook.Worksheets.First();
                sheet.Cell(4,9).FormulaA1="A1";
                workbook.Save();
            }
            var formula=MotorSerialExcelSync.Preview(settings,slip);
            Check(formula.Proposed.Count==0&&formula.Warnings.Count>0,
                "Formula in target serial cell is protected from overwrite");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return checks;
    }
}
