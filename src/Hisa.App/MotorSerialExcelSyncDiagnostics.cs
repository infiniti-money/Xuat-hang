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
            // An advanced Excel file may contain parts outside ClosedXML's
            // object model. Patching a serial must preserve them byte-for-byte.
            var opaque=System.Text.Encoding.UTF8.GetBytes(
                "EXCEL_CUSTOM_PART_PRESERVE"+Guid.NewGuid().ToString("N"));
            using(var zip=System.IO.Compression.ZipFile.Open(
                source,System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry=zip.CreateEntry("customXml/hisa-opaque.bin");
                using var output=entry.Open();
                output.Write(opaque);
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
            using(var zip=System.IO.Compression.ZipFile.OpenRead(source))
            {
                using var opaqueStream=zip.GetEntry("customXml/hisa-opaque.bin")!.Open();
                using var copied=new MemoryStream();
                opaqueStream.CopyTo(copied);
                Check(copied.ToArray().SequenceEqual(opaque),
                    "Serial patch preserves opaque workbook extension parts exactly");
            }
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
            Check(conflict.Proposed.Count==0 && conflict.AlreadyPresent==1 &&
                  conflict.Warnings.Count==0,
                "Same contract serial already in column I is recognized regardless of tower/model");
            using(var workbook=new XLWorkbook(source))
            {
                var sheet=workbook.Worksheets.First();
                sheet.Cell(4,9).FormulaA1="A1";
                workbook.Save();
            }
            var formula=MotorSerialExcelSync.Preview(settings,slip);
            Check(formula.Proposed.Count==0&&formula.Warnings.Count>0,
                "Formula in target serial cell is protected from overwrite");
            // Real-world files often call the motor model differently from
            // the incoming HISA material. Two different contracts must still
            // write ONLY their own verified single-machine row.
            var alternate=Path.Combine(root,"MÁY KÉO-đổi tên model.xlsx");
            using(var book=new XLWorkbook())
            {
                var sheet=book.AddWorksheet("Máy kéo");
                sheet.Cell(2,4).Value="HĐ";
                sheet.Cell(2,9).Value="Số Seri";
                void SourceRow(int row,string contract,string project,string company,
                    string model,string serial="")
                {
                    sheet.Cell(row,2).Value=project;
                    sheet.Cell(row,3).Value=company;
                    sheet.Cell(row,4).Value=contract;
                    sheet.Cell(row,5).Value=model;
                    sheet.Cell(row,6).Value="Bộ";
                    sheet.Cell(row,7).Value=1;
                    sheet.Cell(row,9).Value=serial;
                    sheet.Cell(row,11).Value="Chưa làm";
                }
                SourceRow(3,"25132026","Đồi thông T1","Công ty A","YJ-160");
                SourceRow(4,"46242026","Quang Hanh T1","Công ty B","YJ-200");
                SourceRow(5,"25132026","Đồi thông T2","Công ty A",
                    "YJ-160","SERIAL-T2-ALREADY");
                book.SaveAs(alternate);
            }
            var alternateSettings=new AppSettings{TractionMachineListPath=alternate};
            var anotherSlip=new Slip{Contracts=["25132026","46242026"],
                Lines=[
                    new MaterialLine{Id="s1",Contract="25132026",Project="Đồi thông T1",
                        Company="Công ty A",Material="Máy kéo YJ-160 11kW",
                        Quantity=1,Include=true},
                    new MaterialLine{Id="s2",Contract="46242026",Project="Quang Hanh T1",
                        Company="Công ty B",Material="Máy kéo YJ-200 15kW",
                        Quantity=1,Include=true}
                ]};
            anotherSlip.MotorSerials=MotorSerials.BuildRows(anotherSlip);
            anotherSlip.MotorSerials[0].SerialNumber="SERIAL-25132026";
            anotherSlip.MotorSerials[1].SerialNumber="SERIAL-46242026";
            var safe=MotorSerialExcelSync.Preview(alternateSettings,anotherSlip);
            Check(safe.Proposed.Count==2&&safe.Warnings.Count==0&&
                  safe.Proposed.Select(x=>x.SourceRow).SequenceEqual([3,4]),
                "Serials are placed by contract in source column D, not by project, company or model");
            var saved=MotorSerialExcelSync.Apply(alternateSettings,anotherSlip,backups,safe.Proposed);
            using(var book=new XLWorkbook(alternate))
            {
                var sheet=book.Worksheets.First();
                Check(saved.Written==2 &&
                      sheet.Cell(3,9).GetString()=="SERIAL-25132026" &&
                      sheet.Cell(4,9).GetString()=="SERIAL-46242026" &&
                      sheet.Cell(5,9).GetString()=="SERIAL-T2-ALREADY" &&
                      sheet.Cell(3,11).GetString()=="Chưa làm",
                    "Write only two actual serials into column I and never overwrite T2 or status column K");
                // An additional model for the same project must NOT be guessed
                // when the slip's model differs from both source descriptions.
                sheet.Cell(6,2).Value="Đồi thông T1";
                sheet.Cell(6,3).Value="Công ty A";
                sheet.Cell(6,4).Value="25132026";
                sheet.Cell(6,5).Value="YJ-250";
                sheet.Cell(6,7).Value=1;
                book.Save();
            }
            var ambiguousSlip=new Slip{Contracts=["25132026"],
                Lines=[new MaterialLine{Id="ambiguous",Contract="25132026",
                    Project="Đồi thông T1",Company="Công ty A",
                    Material="Máy kéo cấu hình chưa rõ",Quantity=1,Include=true}]};
            ambiguousSlip.MotorSerials=MotorSerials.BuildRows(ambiguousSlip);
            ambiguousSlip.MotorSerials[0].SerialNumber="NEW-SERIAL-NOT-WRITTEN";
            var ambiguous=MotorSerialExcelSync.Preview(alternateSettings,ambiguousSlip);
            Check(ambiguous.Proposed.Count==1 &&
                  ambiguous.Proposed[0].SourceRow==6 &&
                  ambiguous.Warnings.Count==0,
                "Model mismatch and multiple source models do not block exact contract-only serial entry when one blank source row remains");
            var wrongProject=new Slip{Contracts=["25132026"],
                Lines=[new MaterialLine{Id="wrong-project",Contract="25132026",
                    Project="Đồi thông T3",Company="Công ty A",
                    Material="Máy kéo YJ-160 11kW",Quantity=1,Include=true}]};
            wrongProject.MotorSerials=MotorSerials.BuildRows(wrongProject);
            wrongProject.MotorSerials[0].SerialNumber="WRONG-PROJECT";
            var mismatch=MotorSerialExcelSync.Preview(alternateSettings,wrongProject);
            Check(mismatch.Proposed.Count==1 &&
                  mismatch.Proposed[0].SourceRow==6,
                "Source lookup uses contract only, even when project and company text on slip differ");
            using(var book=new XLWorkbook(alternate))
            {
                var sheet=book.Worksheets.First();
                sheet.Cell(7,2).Value="Đồi thông T2";
                sheet.Cell(7,3).Value="Công ty Z";
                sheet.Cell(7,4).Value="25132026";
                sheet.Cell(7,5).Value="Khác model";
                sheet.Cell(7,7).Value=1;
                book.Save();
            }
            var tooMany=MotorSerialExcelSync.Preview(alternateSettings,ambiguousSlip);
            Check(tooMany.Proposed.Count==0 &&
                  tooMany.Warnings.Any(x=>x.Contains("2 dòng serial trống") &&
                    x.Contains("1 serial mới")),
                "Multiple blank rows with fewer serial values must not be guessed into a source row");
            bool changedPreviewBlocked=false;
            try { MotorSerialExcelSync.Apply(alternateSettings,ambiguousSlip,backups,
                ambiguous.Proposed); }
            catch(IOException ex)
            { changedPreviewBlocked=ex.Message.Contains("đã khác bản xem trước"); }
            Check(changedPreviewBlocked,
                "Operator's approved Excel row cannot be replaced by another row without new confirmation");
            var absentSlip=new Slip{Contracts=["00009999"],
                Lines=[new MaterialLine{Id="missing",Contract="00009999",
                    Project="Dự án khác",Company="Công ty A",
                    Material="Máy kéo YJ-160",Quantity=1,Include=true}]};
            absentSlip.MotorSerials=MotorSerials.BuildRows(absentSlip);
            absentSlip.MotorSerials[0].SerialNumber="NOT-EXISTS";
            var notFound=MotorSerialExcelSync.Preview(alternateSettings,absentSlip);
            Check(notFound.Proposed.Count==0 &&
                  notFound.Warnings.Any(x=>x.Contains("cột D")&&x.Contains("00009999")),
                "Missing contract is explained as source-column D mismatch, not generic model mismatch");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return checks;
    }
}
