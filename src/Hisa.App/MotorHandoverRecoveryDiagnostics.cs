using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Regression fixture for a previously exported HĐ 37452026 whose machine
/// K column remained blank after Zalo project/company names differed.
/// All files are synthetic, isolated and NEVER touch the operator's workbook.
/// </summary>
public static class MotorHandoverRecoveryDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string title)
        {
            if(!ok)throw new InvalidOperationException("Motor 37452026: "+title);
            checks.Add("Motor 37452026: "+title);
        }
        var root=Path.Combine(Path.GetTempPath(),"hisa-37452026-"+
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"MÁY KÉO.xlsx");
            var data=Path.Combine(root,"data");
            var backup=Path.Combine(root,"backup");
            Directory.CreateDirectory(data);
            using(var book=new XLWorkbook())
            {
                var sheet=book.AddWorksheet("DS máy kéo");
                sheet.Cell(2,4).Value="HĐ";
                sheet.Cell(2,9).Value="Số seri";
                sheet.Cell(2,11).Value="Ngày bàn giao";
                void Motor(int row,string contract,string project,string serial)
                {
                    sheet.Cell(row,2).Value=project;
                    sheet.Cell(row,3).Value="Đơn vị nguồn Excel";
                    sheet.Cell(row,4).Value=contract;
                    sheet.Cell(row,5).Value="Máy kéo";
                    sheet.Cell(row,6).Value="Bộ";
                    sheet.Cell(row,7).Value=1;
                    sheet.Cell(row,9).Value=serial;
                }
                Motor(3,"37452026","Công trình Hưng Yên (Thang 01)","SERIAL-3745-T1");
                Motor(4,"37452026","Công trình Hưng Yên (Thang 02)","SERIAL-3745-T2");
                Motor(5,"99992026","Công trình khác","SERIAL-9999");
                sheet.Cell(5,11).Value=new DateTime(2026,10,15);
                book.SaveAs(path);
            }
            var settings=new AppSettings{TractionMachineListPath=path};
            Slip SlipFor(string contract,string serial,string status,string date,
                string project="Công trình Hưng Yên - địa chỉ OCR",string company="Đơn vị OCR") 
            {
                var line=new MaterialLine{Id="motor-"+serial,
                    Contract=contract,Project=project,Company=company,
                    Material="Máy kéo",Unit="Bộ",Quantity=1,Include=true};
                return new Slip{Id="slip-"+serial,SlipCode="BBBG-"+contract,
                    Status=status,HandoverDate=date,
                    Contracts=[contract],Lines=[line],
                    MotorSerials=[new MotorSerialEntry
                    {
                        LineId=line.Id,Contract=contract,Project=project,
                        Company=company,Material=line.Material,UnitIndex=1,
                        SerialNumber=serial
                    }]};
            }
            var delivered=SlipFor("37452026","SERIAL-3745-T1","delivered",
                "2026-10-31");
            var unconfirmed=SlipFor("37452026","SERIAL-3745-T2","planned",
                "2026-10-31");
            var other=SlipFor("99992026","SERIAL-9999","delivered",
                "2026-10-15","Công trình khác","Đơn vị nguồn Excel");
            var before=MotorHandoverExcel.Sync(settings,[unconfirmed,other],
                data,backup,contractScope:"37452026");
            Check(before.Written==0 && before.Cleared==0,
                "Creating a pending handover does not write a K date");
            var run=MotorHandoverExcel.Sync(settings,[delivered,unconfirmed,other],
                data,backup,contractScope:"37452026");
            using(var book=new XLWorkbook(path))
            {
                var sheet=book.Worksheet(1);
                Check(run.Written==1 && run.Unmatched==0 &&
                    sheet.Cell(3,11).GetDateTime()==new DateTime(2026,10,31),
                    "Old delivered slip writes actual export date using unique HĐ + serial even when OCR name differs");
                Check(sheet.Cell(4,11).IsEmpty() &&
                    sheet.Cell(5,11).GetDateTime()==new DateTime(2026,10,15),
                    "Scope does not touch another tower or a different contract");
            }
            Check(run.Warnings.Any(x=>x.Contains("đã đối chiếu bằng số HĐ")),
                "Canonical Excel alias fallback is visible in the diagnostic");
            var retry=MotorHandoverExcel.Sync(settings,
                [delivered,unconfirmed,other],data,backup,
                contractScope:"37452026");
            Check(retry.Written==0 && retry.Cleared==0 &&
                Directory.GetFiles(backup,"*.xlsx").Length==1,
                "Repeated repair is idempotent and creates no extra backup");
            var noSerial=SlipFor("37452026","","delivered","2026-10-31");
            var blocked=MotorHandoverExcel.Sync(settings,[noSerial],
                data,backup,contractScope:"37452026");
            Check(blocked.Written==0 && blocked.Unmatched>0 &&
                blocked.Warnings.Any(x=>x.Contains("thiếu") &&
                    x.Contains("serial")),
                "Delivered contract without serial is reported as unresolved");

            using(var book=new XLWorkbook(path))
            {
                // Duplicate physical serial anywhere in the workbook must
                // never match by contract number alone.
                book.Worksheet(1).Cell(4,9).Value="SERIAL-3745-T1";
                book.Save();
            }
            var duplicate=MotorHandoverExcel.Sync(settings,[delivered],
                data,backup,contractScope:"37452026");
            Check(duplicate.Unmatched==1 && duplicate.Written==0 &&
                duplicate.Warnings.Any(x=>x.Contains("trùng")),
                "Duplicate source serial fails closed, never picks a tower");
            using(var book=new XLWorkbook(path))
            {
                Check(book.Worksheet(1).Cell(3,11).GetDateTime()==
                        new DateTime(2026,10,31) &&
                    book.Worksheet(1).Cell(5,11).GetDateTime()==
                        new DateTime(2026,10,15),
                    "Conflicting source never overwrites previously confirmed or unrelated dates");
            }
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
        return checks;
    }
}
