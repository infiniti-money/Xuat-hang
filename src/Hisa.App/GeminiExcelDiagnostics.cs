using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

public static class GeminiExcelDiagnostics
{
    public static List<string> Run(string workspace)
    {
        var passed=new List<string>();
        void Check(bool ok,string message)
        {if(!ok)throw new InvalidOperationException("AI Excel: "+message);passed.Add("AI Excel: "+message);}
        var dir=Path.Combine(workspace,"gemini-excel-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,"MÁY KÉO.xlsx");
        using(var book=new XLWorkbook())
        {
            var sheet=book.AddWorksheet("DS máy kéo");
            sheet.Cell(2,4).Value="HĐ";sheet.Cell(2,11).Value="Ngày xuất kho";
            sheet.Cell(3,4).Value="05492026";sheet.Cell(3,5).Value="Máy kéo cũ";
            sheet.Cell(3,9).Value="SERIAL-01";sheet.Cell(3,11).Value="Đã làm";
            sheet.Cell(4,5).FormulaA1="E3";
            book.SaveAs(path);
        }
        var settings=new AppSettings{TractionMachineListPath=path};
        var snapshot=GeminiExcelAssistant.Read(path,settings);
        Check(snapshot.Text.Contains("Máy kéo cũ") &&
            snapshot.Cells["3:5"]=="Máy kéo cũ","Read bounded workbook and cell addresses offline");
        var json="{\"summary\":\"Sửa mô tả\",\"changes\":[{\"row\":3,\"column\":5,"+
            "\"value\":\"Máy kéo mới\",\"reason\":\"Đã xác nhận\"}]}";
        var proposal=GeminiExcelAssistant.Parse(json,snapshot);
        Check(proposal.Changes.Count==1 && proposal.Changes[0].Before=="Máy kéo cũ",
            "Gemini JSON is only a proposed cell diff");
        bool blocked=false;
        try{GeminiExcelAssistant.Parse("{\"changes\":[{\"row\":3,\"column\":11,\"value\":\"=1+1\"}]}",snapshot);}
        catch(InvalidDataException){blocked=true;}
        Check(blocked,"Excel formula injection rejected before any write");
        blocked=false;
        try{GeminiExcelAssistant.Apply(snapshot,new GeminiExcelAssistant.Proposal("",
            [new GeminiExcelAssistant.Change(3,11,"Đã làm","Chưa làm","")],""),settings,dir);}
        catch(InvalidOperationException){blocked=true;}
        Check(blocked,"AI cannot modify machine status column K without verified workflow");
        blocked=false;
        try{GeminiExcelAssistant.Apply(snapshot,new GeminiExcelAssistant.Proposal("",
            [new GeminiExcelAssistant.Change(4,5,"[CÔNG THỨC - KHÔNG CHỈNH]","text","")],""),
            settings,dir);}
        catch(IOException){blocked=true;}
        Check(blocked,"AI cannot edit formula cells");
        var backup=GeminiExcelAssistant.Apply(snapshot,proposal,settings,dir);
        using(var book=new XLWorkbook(path))
            Check(book.Worksheet(1).Cell(3,5).GetString()=="Máy kéo mới" &&
                book.Worksheet(1).Cell(3,11).GetString()=="Đã làm" &&
                File.Exists(backup),"Explicitly approved cell written with backup; K untouched");
        blocked=false;
        try{GeminiExcelAssistant.Apply(snapshot,proposal,settings,dir);}
        catch(IOException){blocked=true;}
        Check(blocked,"Stale file fingerprint blocks concurrent or repeated AI writes");
        var master=Path.Combine(dir,"Tổng hợp.xlsx");
        File.Copy(path,master);
        settings.MasterPath=master;
        var masterPreview=GeminiExcelAssistant.Read(master,settings);
        blocked=false;
        try{GeminiExcelAssistant.Apply(masterPreview,new GeminiExcelAssistant.Proposal("",
            [new GeminiExcelAssistant.Change(3,5,"Máy kéo mới","Sửa","")],""),
            settings,dir);}
        catch(InvalidOperationException){blocked=true;}
        Check(blocked,"Canonical warehouse source is not editable by generic AI");
        return passed;
    }
}
