using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

public static class MotorHandoverExcelDiagnostics
{
    public static List<string> Run(string workspace)
    {
        var checks=new List<string>();
        void Check(bool value,string label)
        {
            if(!value)throw new InvalidOperationException("Motor handover Excel: "+label);
            checks.Add("Motor handover Excel: "+label);
        }
        var root=Path.Combine(workspace,"motor-date-"+Guid.NewGuid().ToString("N"));
        var production=Path.Combine(root,"Danh sách sản xuất");
        var archive=Path.Combine(root,"backups");
        var data=Path.Combine(root,"data");
        Directory.CreateDirectory(production);
        Directory.CreateDirectory(data);
        var path=Path.Combine(production,"MÁY KÉO.xlsx");
        using(var book=new XLWorkbook())
        {
            var sheet=book.AddWorksheet("DS máy kéo");
            sheet.Cell(1,1).Value="DANH SÁCH LÀM MÁY KÉO BÀN GIAO KHO THÀNH PHẨM";
            string[] headers=["STT","Tên công trình","Công ty","HĐ","Tên vật tư",
                "ĐVT","SL","Số Lô","số seri","Ngày cont đi"];
            for(int i=0;i<headers.Length;i++)sheet.Cell(2,i+1).Value=headers[i];
            void Motor(int row,string project,string serial,string material="Máy kéo M1")
            {
                sheet.Cell(row,1).Value=row-2;
                sheet.Cell(row,2).Value=project;
                sheet.Cell(row,3).Value="Công ty A";
                sheet.Cell(row,4).Value="05492026";
                sheet.Cell(row,5).Value=material;
                sheet.Cell(row,6).Value="Bộ";
                sheet.Cell(row,7).Value=1;
                sheet.Cell(row,8).Value="Lô 001";
                sheet.Cell(row,9).Value=serial;
            }
            Motor(3,"Công trình A","S-001");
            Motor(4,"Công trình A","S-002");
            Motor(5,"Công trình B","S-003");
            Motor(6,"Công trình A","S-004");
            Motor(7,"Công trình A","S-005");
            sheet.Cell(6,11).Value=new DateTime(2026,10,19);
            sheet.Cell(6,11).Style.DateFormat.Format="dd/MM/yyyy";
            sheet.Cell(2,11).Value="Ngày bàn giao"; // legacy v1.9.13, must not change
            sheet.Cell(6,14).Value=new DateTime(2026,10,19);
            sheet.Cell(6,14).Style.DateFormat.Format="dd/MM/yyyy";
            sheet.Cell(2,14).Value="Ngày bàn giao"; // fixed column N
            sheet.Cell(2,12).Value="Ghi chú ngoài";sheet.Cell(3,12).Value="Không sửa";
            book.SaveAs(path);
        }
        var settings=new AppSettings{TractionMachineListPath=path};
        var line=new MaterialLine{Id="test-1",Contract="05492026",
            Project="Công trình A",Company="Công ty A",Material="Máy kéo M1",
            Unit="Bộ",Quantity=1};
        Slip MakeSlip(string serial,string state,string date,string code="TEST")=>new()
        {
            Id=code,SlipCode=code,Status=state,HandoverDate=date,
            Contracts=["05492026"],Lines=[line],
            MotorSerials=[new MotorSerialEntry{LineId=line.Id,Contract=line.Contract,
                Project=line.Project,Company=line.Company,Material=line.Material,
                UnitIndex=1,SerialNumber=serial}]
        };
        var planned=MakeSlip("S-001","planned","2026-10-27");
        var first=MotorHandoverExcel.Sync(settings,[planned],data,archive);
        Check(first.Written==0&&first.Cleared==0,
            "Waiting/planned handover never writes a date");
        var delivered=MakeSlip("S-001","delivered","2026-10-27");
        var result=MotorHandoverExcel.Sync(settings,[delivered,planned],data,archive);
        Check(result.Written==1&&result.Cleared==0&&result.Unmatched==0,
            "Confirmed delivered updates only one physical serial");
        using(var book=new XLWorkbook(path))
        {
            var ws=book.Worksheet(1);
            Check(ws.Cell(2,14).GetString()=="Ngày bàn giao" &&
                ws.Cell(3,14).GetDateTime()==new DateTime(2026,10,27),
                "Original machine list gets a real Excel date under Ngày bàn giao header");
            Check(ws.Cell(4,14).IsEmpty() && ws.Cell(5,14).IsEmpty() &&
                ws.Cell(6,14).GetDateTime()==new DateTime(2026,10,19) &&
                ws.Cell(7,14).IsEmpty() &&
                ws.Cell(3,12).GetString()=="Không sửa",
                "Same contract siblings, different project, manual date and other columns are untouched");
        }
        Check(Directory.EnumerateFiles(archive,"*.xlsx").Count()==1 &&
              File.Exists(Path.Combine(data,"motor_handover_excel_sync.json")),
            "Original machine workbook is backed up before first write; ownership saved separately");
        var again=MotorHandoverExcel.Sync(settings,[delivered],data,archive);
        Check(again.Written==0&&again.Cleared==0 &&
              Directory.EnumerateFiles(archive,"*.xlsx").Count()==1,
            "Re-sync is idempotent and does not create unnecessary workbook backups");
        delivered.HandoverDate="2026-10-28";
        var changed=MotorHandoverExcel.Sync(settings,[delivered],data,archive);
        using(var book=new XLWorkbook(path))
            Check(changed.Written==1 && book.Worksheet(1).Cell(3,14).GetDateTime()
                ==new DateTime(2026,10,28),
                "Editing actual delivery date updates only the application-owned cell");
        delivered.Status="cancelled";
        var cancelled=MotorHandoverExcel.Sync(settings,[delivered],data,archive);
        using(var book=new XLWorkbook(path))
            Check(cancelled.Cleared==1 && book.Worksheet(1).Cell(3,14).IsEmpty() &&
                book.Worksheet(1).Cell(6,14).GetDateTime()==new DateTime(2026,10,19),
                "Cancelling a delivered slip removes only HISA-owned handover date, not human-entered data");
        var manualConflict=MakeSlip("S-004","delivered","2026-10-30","manual");
        var notOverwritten=MotorHandoverExcel.Sync(settings,[manualConflict],data,archive);
        using(var book=new XLWorkbook(path))
            Check(notOverwritten.Written==0&&notOverwritten.Unmatched==1 &&
                book.Worksheet(1).Cell(6,14).GetDateTime()==new DateTime(2026,10,19),
                "Manual handover date conflict is reported and never overwritten");
        var wrongProject=MakeSlip("S-003","delivered","2026-10-30","wrong-project");
        var noMatch=MotorHandoverExcel.Sync(settings,[wrongProject],data,archive);
        Check(noMatch.Written==0&&noMatch.Unmatched==1,
            "Same HĐ and serial with different project/identity never matches incorrectly");
        var missingSerial=MakeSlip("","delivered","2026-10-30","missing-serial");
        var skipped=MotorHandoverExcel.Sync(settings,[missingSerial],data,archive);
        Check(skipped.Written==0,"No serial means no inferred delivery date for all rows on a contract");

        var other=MakeSlip("S-002","delivered","2026-10-29","locked");
        using(var lockFile=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
        {
            bool locked=false;
            try{MotorHandoverExcel.Sync(settings,[other],data,archive);}
            catch(IOException){locked=true;}
            Check(locked,"Locked source Excel prevents overwrite; confirmed slip remains recoverable for retry");
        }
        var retry=MotorHandoverExcel.Sync(settings,[other],data,archive);
        using(var book=new XLWorkbook(path))
            Check(retry.Written==1 &&
                book.Worksheet(1).Cell(4,14).GetDateTime()==new DateTime(2026,10,29),
                "After Excel closes, syncing again writes the correct serial date without duplicates");
        var historyPath=Path.Combine(data,"motor_handover_excel_sync.json");
        var prior=File.ReadAllText(historyPath);
        File.WriteAllText(historyPath,"broken-json");
        bool invalid=false;
        try{MotorHandoverExcel.Sync(settings,[other],data,archive);}
        catch(InvalidDataException){invalid=true;}
        Check(invalid&&File.ReadAllText(historyPath)=="broken-json",
            "Corrupted handover receipt state blocks unsafe write rather than resetting history");
        File.WriteAllText(historyPath,prior);

        // No pre-existing handover column: create at the first genuinely unused
        // position, leaving all original notes untouched.
        var otherPath=Path.Combine(production,"MÁY KÉO 2.xlsx");
        File.Copy(path,otherPath);
        using(var book=new XLWorkbook(otherPath))
        {
            var sheet=book.Worksheet(1);
            for(var row=2;row<=7;row++)
                sheet.Cell(row,14).Clear(XLClearOptions.All);
            book.Save();
        }
        var separateData=Path.Combine(root,"second-data");
        Directory.CreateDirectory(separateData);
        settings.TractionMachineListPath=otherPath;
        var created=MotorHandoverExcel.Sync(settings,[other],separateData,archive);
        using(var book=new XLWorkbook(otherPath))
            Check(created.Written==1 &&
                book.Worksheet(1).Cell(2,14).GetString()=="Ngày bàn giao" &&
                book.Worksheet(1).Cell(4,14).GetDateTime()==new DateTime(2026,10,29) &&
                book.Worksheet(1).Cell(3,12).GetString()=="Không sửa",
                "Without column N header, create exactly N even if legacy dates or notes exist elsewhere");

        // Explicit Chưa làm / Đã làm must write text, then a confirmed slip
        // replaces only that word with a real date. Cancellation restores work.
        var workPath=Path.Combine(production,"MÁY KÉO work.xlsx");
        File.Copy(otherPath,workPath);
        var workData=Path.Combine(root,"work-data");
        Directory.CreateDirectory(workData);
        settings.TractionMachineListPath=workPath;
        var tracker=new MotorHungYenStore(workData);
        var machine=tracker.List(EquipmentListReader.Read(workPath,"Máy kéo"))
            .Single(x=>x.Serial=="S-001");
        tracker.SetWorkStatus(machine,true,DateTime.Today.ToString("yyyy-MM-dd"),"Kiểm thử");
        var workResult=MotorHandoverExcel.Sync(settings,[planned],workData,archive);
        using(var book=new XLWorkbook(workPath))
            Check(workResult.Written==1&&
                book.Worksheet(1).Cell(3,14).GetString()=="Đã làm",
                "Choosing Đã làm writes text to N and not a date");
        var readBack=EquipmentListReader.Read(workPath,"Máy kéo");
        Check(readBack.Single(x=>x.Serial=="S-001").MotorColumnN=="Đã làm" &&
              new MotorHungYenStore(workData).List(readBack)
                  .Single(x=>x.Serial=="S-001").WorkLabel=="Đã làm",
            "Status written to Excel is read back by the tracker, including manual edits");
        var actual=MakeSlip("S-001","delivered","2026-10-27","actual-work");
        MotorHandoverExcel.Sync(settings,[actual],workData,archive);
        using(var book=new XLWorkbook(workPath))
            Check(book.Worksheet(1).Cell(3,14).GetDateTime()==new DateTime(2026,10,27),
                "Confirmed slip replaces Đã làm with actual date in N");
        actual.Status="cancelled";
        MotorHandoverExcel.Sync(settings,[actual],workData,archive);
        using(var book=new XLWorkbook(workPath))
            Check(book.Worksheet(1).Cell(3,14).GetString()=="Đã làm",
                "Cancelling slip restores Đã làm, not an invented delivery date");
        tracker.SetWorkStatus(tracker.List(EquipmentListReader.Read(workPath,"Máy kéo"))
            .Single(x=>x.Serial=="S-001"),false,DateTime.Today.ToString("yyyy-MM-dd"),"Kiểm thử lại");
        MotorHandoverExcel.Sync(settings,[actual],workData,archive);
        using(var book=new XLWorkbook(workPath))
            Check(book.Worksheet(1).Cell(3,14).GetString()=="Chưa làm",
                "Changing back to Chưa làm updates N but keeps the audit history");

        // A pre-used non-handover N column must block the write entirely.
        var blockedPath=Path.Combine(production,"MÁY KÉO blocked.xlsx");
        File.Copy(workPath,blockedPath);
        using(var book=new XLWorkbook(blockedPath))
        {
            book.Worksheet(1).Cell(2,14).Value="Ghi chú riêng";
            book.Save();
        }
        var unchanged=File.ReadAllBytes(blockedPath);
        settings.TractionMachineListPath=blockedPath;
        bool refused=false;
        try{MotorHandoverExcel.Sync(settings,[delivered],Path.Combine(root,"blocked-data"),archive);}
        catch(InvalidDataException){refused=true;}
        Check(refused&&unchanged.SequenceEqual(File.ReadAllBytes(blockedPath)),
            "Occupied N column is never overwritten or silently relocated");
        return checks;
    }
}
