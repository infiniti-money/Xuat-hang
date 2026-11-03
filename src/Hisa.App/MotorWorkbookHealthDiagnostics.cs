using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Read-only workbook inspection checks on generated, isolated test files.
/// Nothing is loaded from or written to the operator's production directory.
/// </summary>
public static class MotorWorkbookHealthDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool condition,string name)
        {
            if(!condition)throw new InvalidOperationException(
                "Motor workbook health: "+name);
            checks.Add("Motor workbook health: "+name);
        }
        var workspace=Path.Combine(Path.GetTempPath(),
            "hisa-motor-health-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var file=Path.Combine(workspace,"MÁY KÉO.xlsx");
            using(var wb=new XLWorkbook())
            {
                var ws=wb.AddWorksheet("DS máy kéo");
                ws.Cell(2,4).Value="HĐ";
                ws.Cell(2,9).Value="Số Seri";
                ws.Cell(2,11).Value="Ngày bàn giao";
                for(var row=3;row<=5;row++)
                {
                    ws.Cell(row,2).Value="Hưng Yên T1";
                    ws.Cell(row,3).Value="Công ty A";
                    ws.Cell(row,4).Value="56062026";
                    ws.Cell(row,5).Value="Máy kéo 11 kW";
                    ws.Cell(row,7).Value=1;
                    ws.Cell(row,9).Value="MOTOR-"+row;
                }
                ws.Cell(3,11).Value=new DateTime(2026,10,27);
                ws.Cell(3,11).Style.DateFormat.Format="dd/MM/yyyy";
                ws.Cell(5,11).Value="Đã làm";
                wb.SaveAs(file);
            }
            var before=File.ReadAllBytes(file);
            var report=MotorWorkbookHealth.Inspect(file);
            Check(report.CanWrite && report.Machines==3 &&
                  report.HandedOver==1 && report.Working==2 &&
                  File.ReadAllBytes(file).SequenceEqual(before),
                "Read-only check recognizes one K date, two unfinished motors and never saves Excel");
            Check(report.Findings.Any(x=>x.Level=="Cảnh báo" &&
                    x.Detail.Contains("trạng thái chữ cũ")),
                "Legacy work words in K are warned about, not treated as handover");
            using(var wb=new XLWorkbook(file))
            {
                wb.Worksheets.First().Cell(2,4).Value="HĐ KHÁC";
                wb.Save();
            }
            var invalidHeader=File.ReadAllBytes(file);
            report=MotorWorkbookHealth.Inspect(file);
            Check(!report.CanWrite &&
                  report.Findings.Any(x=>x.Detail.Contains("D2")) &&
                  invalidHeader.SequenceEqual(File.ReadAllBytes(file)),
                "Unexpected contract header blocks writing without modifying the source");
            var damaged=Path.Combine(workspace,"MÁY KÉO broken.xlsx");
            File.WriteAllBytes(damaged,[1,2,3,4,5,6]);
            var damagedBefore=File.ReadAllBytes(damaged);
            report=MotorWorkbookHealth.Inspect(damaged);
            Check(!report.CanWrite && report.Findings.Count>0 &&
                  damagedBefore.SequenceEqual(File.ReadAllBytes(damaged)),
                "Broken ZIP is detected and remains byte-for-byte unchanged");
            report=MotorWorkbookHealth.Inspect(
                Path.Combine(workspace,"missing.xlsx"));
            Check(!report.CanWrite && report.Findings.Count==1,
                "Missing workbook yields a clear diagnosis without creating a file");
        }
        finally
        {
            if(Directory.Exists(workspace))Directory.Delete(workspace,true);
        }
        return checks;
    }
}
