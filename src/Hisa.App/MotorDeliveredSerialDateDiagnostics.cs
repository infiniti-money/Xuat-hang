using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Offline regression for a confirmed handover that must write both serial I
/// and real handover date K to the SAME motor workbook transaction.
/// Never opens or changes the operator's configured production workbook.
/// </summary>
public static class MotorDeliveredSerialDateDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string label)
        {
            if(!ok)throw new InvalidOperationException("Motor I+K delivery: "+label);
            checks.Add("Motor I+K delivery: "+label);
        }
        var root=Path.Combine(Path.GetTempPath(),
            "hisa-serial-date-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"MÁY KÉO.xlsx");
            var data=Path.Combine(root,"data");
            var backups=Path.Combine(root,"backups");
            Directory.CreateDirectory(data);
            using(var book=new XLWorkbook())
            {
                var ws=book.AddWorksheet("DS máy kéo");
                ws.Cell(2,4).Value="HĐ";
                ws.Cell(2,9).Value="Số Seri";
                ws.Cell(2,11).Value="Ngày bàn giao";
                void Machine(int row,string contract,string project,string serial)
                {
                    ws.Cell(row,2).Value=project;
                    ws.Cell(row,3).Value="Công ty A";
                    ws.Cell(row,4).Value=contract;
                    ws.Cell(row,5).Value="Máy kéo 11 kW";
                    ws.Cell(row,6).Value="Bộ";
                    ws.Cell(row,7).Value=1;
                    ws.Cell(row,9).Value=serial;
                    ws.Cell(row,10).Value="Ghi chú bảo vệ";
                    ws.Cell(row,12).FormulaA1="G"+row+"+2";
                }
                Machine(3,"56062026","Hưng Yên T1","");
                Machine(4,"56062026","Hưng Yên T1","T1-EXISTING");
                Machine(5,"56062026","Hưng Yên T2","T2-EXISTING");
                Machine(6,"88882026","Dự án khác","OTHER-SERIAL");
                ws.Cell(6,11).Value=new DateTime(2026,10,12);
                book.SaveAs(path);
            }
            // Unknown Office extensions must survive the original-OOXML patch.
            var opaque=Encoding.UTF8.GetBytes("motor-extension-"+Guid.NewGuid().ToString("N"));
            using(var archive=ZipFile.Open(path,ZipArchiveMode.Update))
            {
                using var writer=archive.CreateEntry("customXml/custom-preserve.bin").Open();
                writer.Write(opaque);
            }
            var settings=new AppSettings{TractionMachineListPath=path};
            Slip NewSlip(string project,string id,string status,string date,
                params string[] serials)
            {
                var line=new MaterialLine
                {
                    Id="motor-"+id,Contract="56062026",Project=project,
                    Company="Công ty A",Material="Máy kéo 11 kW",
                    Unit="Bộ",Quantity=serials.Length,Include=true
                };
                var slip=new Slip{Id=id,SlipCode="BBBG-"+id,
                    Status=status,HandoverDate=date,Contracts=["56062026"],
                    Lines=[line]};
                slip.MotorSerials=MotorSerials.BuildRows(slip);
                for(int i=0;i<serials.Length;i++)
                    slip.MotorSerials[i].SerialNumber=serials[i];
                return slip;
            }
            var planned=NewSlip("Hưng Yên T1","planned","planned","2026-11-07",
                "T1-NEW","T1-EXISTING");
            var untouched=File.ReadAllBytes(path);
            var draft=MotorHandoverExcel.Sync(settings,[planned],data,backups);
            Check(draft.Written==0&&draft.SerialWritten==0&&
                File.ReadAllBytes(path).SequenceEqual(untouched),
                "Planned handover never changes serial I or date K");

            planned.Status="delivered";
            var outcome=MotorHandoverExcel.Sync(settings,[planned],data,backups);
            using(var book=new XLWorkbook(path))
            {
                var ws=book.Worksheet(1);
                Check(outcome.Written==2&&outcome.SerialWritten==1&&
                    outcome.Unmatched==0 &&
                    ws.Cell(3,9).GetString()=="T1-NEW" &&
                    ws.Cell(4,9).GetString()=="T1-EXISTING",
                    "Delivered slip fills ONLY missing T1 serial in I and both K dates");
                Check(ws.Cell(3,11).GetDateTime()==new DateTime(2026,11,7) &&
                      ws.Cell(4,11).GetDateTime()==new DateTime(2026,11,7) &&
                      ws.Cell(5,11).IsEmpty() &&
                      ws.Cell(6,11).GetDateTime()==new DateTime(2026,10,12),
                    "K date is same real handover day, without T2 or other contract changes");
                Check(ws.Cell(3,10).GetString()=="Ghi chú bảo vệ" &&
                      ws.Cell(3,12).HasFormula &&
                      ws.Cell(5,9).GetString()=="T2-EXISTING",
                    "Unrelated notes, formulas and T2 serial remain untouched");
            }
            using(var zip=ZipFile.OpenRead(path))
            {
                using var input=zip.GetEntry("customXml/custom-preserve.bin")!.Open();
                using var buffer=new MemoryStream();
                input.CopyTo(buffer);
                Check(buffer.ToArray().SequenceEqual(opaque),
                    "Unknown Excel extension survives atomic serial/date update");
            }
            Check(Directory.GetFiles(backups,"*.xlsx").Length==1,
                "I+K paired update creates exactly one original workbook backup");
            var repeat=MotorHandoverExcel.Sync(settings,[planned],data,backups);
            Check(repeat.SerialWritten==0&&repeat.Written==0&&
                Directory.GetFiles(backups,"*.xlsx").Length==1,
                "Retry is idempotent, no extra serial/date writes or backups");

            var wrong=NewSlip("Hưng Yên T3","wrong","delivered",
                "2026-11-08","T3-NEW");
            var other=MotorHandoverExcel.Sync(settings,[planned,wrong],data,backups);
            using(var check=new XLWorkbook(path))
                Check(other.SerialWritten==0&&other.Unmatched>0&&
                    check.Worksheet(1).Cell(5,9).GetString()=="T2-EXISTING"&&
                    check.Worksheet(1).Cell(5,11).IsEmpty(),
                    "Unknown project cannot allocate T2 serial or date by contract alone");

            var noSerial=NewSlip("Hưng Yên T2","missing","delivered",
                "2026-11-08","");
            var missing=MotorHandoverExcel.Sync(settings,[planned,noSerial],data,backups);
            Check(missing.SerialWritten==0&&missing.Unmatched>0,
                "Optional missing serial leaves unresolved K, not a guessed date");

            // Simulate a workbook being externally edited after a preview.
            var before=File.ReadAllBytes(path);
            bool staleRejected=false;
            try
            {
                MotorWorkbookCellPatch.Apply(path,backups,
                    [new MotorWorkbookCellPatch.Edit(5,11,"2026-11-09",true)],
                    "must-not-write","BAD_SHA256");
            }
            catch(IOException){staleRejected=true;}
            Check(staleRejected&&File.ReadAllBytes(path).SequenceEqual(before),
                "Changed workbook fingerprint blocks stale I/K confirmation");

            // A protected formula in K must roll back the new serial I too.
            var formulaPath=Path.Combine(root,"MÁY KÉO-formula.xlsx");
            using(var book=new XLWorkbook())
            {
                var ws=book.AddWorksheet("Máy kéo");
                ws.Cell(2,4).Value="HĐ";
                ws.Cell(2,9).Value="Số Seri";
                ws.Cell(2,11).Value="Ngày bàn giao";
                ws.Cell(3,2).Value="Hưng Yên T1";
                ws.Cell(3,3).Value="Công ty A";
                ws.Cell(3,4).Value="56062026";
                ws.Cell(3,5).Value="Máy kéo 11 kW";
                ws.Cell(3,7).Value=1;
                ws.Cell(3,11).FormulaA1="IF(1=2,DATE(2026,11,7),\"\")";
                book.SaveAs(formulaPath);
            }
            var beforeFormula=File.ReadAllBytes(formulaPath);
            bool formulaRejected=false;
            try
            {
                MotorHandoverExcel.Sync(new AppSettings{TractionMachineListPath=formulaPath},
                    [NewSlip("Hưng Yên T1","formula","delivered",
                        "2026-11-07","T1-FORMULA")],
                    Path.Combine(root,"formula-data"),
                    Path.Combine(root,"formula-backup"));
            }
            catch(IOException){formulaRejected=true;}
            // The existing K formula may instead be considered a conflicting
            // human-entered value before patching; both paths must keep I blank.
            using(var read=new XLWorkbook(formulaPath))
                Check(read.Worksheet(1).Cell(3,9).IsEmpty() &&
                      read.Worksheet(1).Cell(3,11).HasFormula,
                    "Unwritable K formula cannot leave a partially written I serial");
            Check(!formulaRejected||File.ReadAllBytes(formulaPath).SequenceEqual(beforeFormula),
                "Formula conflict fails safely without replacing source workbook");

            planned.Status="cancelled";
            var undo=MotorHandoverExcel.Sync(settings,[planned],data,backups);
            using(var read=new XLWorkbook(path))
                Check(undo.Cleared==2 &&
                      read.Worksheet(1).Cell(3,11).IsEmpty() &&
                      read.Worksheet(1).Cell(4,11).IsEmpty() &&
                      read.Worksheet(1).Cell(3,9).GetString()=="T1-NEW",
                    "Cancel clears only application-owned delivery K; physical serial I remains");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        return checks;
    }
}
