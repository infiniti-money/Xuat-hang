using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ClosedXML.Excel;

namespace Hisa.App;

public static class MotorSerialDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var checks=new List<string>();
        void Check(bool valid,string name){if(!valid)throw new InvalidOperationException("FAIL: "+name);checks.Add(name);}
        void Reject(Action action,string name){try{action();}catch(ArgumentException){checks.Add(name);return;}throw new InvalidOperationException("FAIL: "+name);}
        var root=SelfTests.CreateDemoWorkspace();using var service=new WarehouseService(root);await service.InitializeAsync();
        var report=service.Search(new SearchFilter{ContractsText="05492026",ArrivalDate="2026-10-16",Categories=["Máy kéo"]});
        var draft=service.CreateDraft(report,"contract_delivery","2026-10-27");
        var rows=MotorSerials.BuildRows(draft);
        Check(rows.Count==5&&rows.Count(x=>x.LineId==draft.Lines[0].Id)==2,"One serial row per physical motor, including quantity two");
        Check(rows.All(x=>x.Contract=="05492026"&&x.Project=="Công trình A"),"Serial rows retain exact contract and project");
        draft.MotorSerials=rows;Reject(()=>MotorSerials.Validate(draft),"Missing motor serial blocks export");
        var waiting=await service.SaveSlipAsync(draft);var invalid=WarehouseService.Clone(waiting);invalid.Status="delivered";
        try{await service.SaveSlipAsync(invalid);throw new InvalidOperationException("Export unexpectedly succeeded");}
        catch(ArgumentException){checks.Add("Backend rejects missing serial before modifying saved state");}
        Check(service.Slips.Single(x=>x.Id==waiting.Id).Status=="planned","Failed export retains the waiting slip");
        SerialTestData.Fill(draft,"MOTOR");draft.MotorSerials[0].SerialNumber=" 0000392-A ";
        MotorSerials.Validate(draft);Check(draft.MotorSerials[0].SerialNumber=="0000392-A","Serial leading zeroes and punctuation preserved, outer spaces trimmed");
        var duplicate=WarehouseService.Clone(draft);duplicate.MotorSerials[1].SerialNumber="0000392-a";
        Reject(()=>MotorSerials.Validate(duplicate),"Case-insensitive duplicate serial within a slip rejected");
        var changed=WarehouseService.Clone(draft);changed.Lines[0].Quantity++;
        Check(MotorSerials.BuildRows(changed).Count==6&&MotorSerials.BuildRows(changed).Count(x=>x.SerialNumber.Length==0)==1,"Increasing motor quantity retains old serials and requests new one");
        changed=WarehouseService.Clone(draft);changed.Lines[0].Material="Máy kéo model mới";
        Check(MotorSerials.BuildRows(changed).Count(x=>x.SerialNumber.Length==0)==2,"Changing motor model never transfers serials silently");
        changed=WarehouseService.Clone(draft);changed.Lines[0].Include=false;
        Check(MotorSerials.BuildRows(changed).Count==3,"Excluded motor lines do not reserve serials");
        changed=WarehouseService.Clone(draft);changed.Lines[0].Quantity=1.5m;
        Reject(()=>MotorSerials.BuildRows(changed),"Fractional motor quantities rejected before serial entry");
        draft.Status="delivered";var delivered=await service.SaveSlipAsync(draft);
        Check(delivered.MotorSerials.Count==5&&delivered.MotorSerials[0].SerialNumber=="0000392-A","Export saves complete serial list on the slip");
        MotorSerials.Validate(delivered,service.Slips);checks.Add("Editing own exported slip does not report its serials as duplicates");
        var second=WarehouseService.Clone(delivered);second.Id=Guid.NewGuid().ToString();second.SlipCode="";
        Reject(()=>MotorSerials.Validate(second,service.Slips),"Serial already exported by another active slip rejected");
        using(var reopened=new WarehouseService(root))
        {
            await reopened.InitializeAsync();Check(reopened.Slips.Single(x=>x.Id==delivered.Id).MotorSerials[0].SerialNumber=="0000392-A","Serials persist after reopening the application");
        }
        using(var product=new XLWorkbook(service.Settings.ProductExportPath))
        {
            Check(product.Worksheet(1).RowsUsed().Any(row=>row.Cell(6).GetString().Contains("0000392-A")),"Canonical product ledger includes exported serials");
        }
        var ledgerPath=Path.Combine(root,"XUẤT HÀNG","Sổ theo dõi xuất hàng.xlsx");
        using(var book=new XLWorkbook(ledgerPath))
        {
            Check(book.Worksheet("Serial máy kéo").RowsUsed().Skip(1).Count()==5,"Detailed export ledger contains one row per serial");
            Check(book.Worksheet("Vật tư bàn giao").Cell(1,13).GetString()=="Serial máy kéo"&&book.Worksheet("Vật tư bàn giao").Cell(2,13).GetString().Contains("0000392-A"),"Material export sheet carries serial column without shifting old columns");
        }
        var daily=service.BuildDaily("2026-10-27");var dailyPath=Path.Combine(root,"daily-serial.xlsx");await service.ExportDailyAsync(dailyPath,daily);
        using(var book=new XLWorkbook(dailyPath))Check(book.Worksheets.Any(x=>x.Name=="Serial máy kéo")&&book.Worksheet("Serial máy kéo").RowsUsed().Count()==6,"Daily Excel report retains all five motor serials");
        foreach(var layout in new[]{"internal_summary","contract_delivery","standard"})
        {
            var doc=PrintService.BuildSlipDocument(delivered,new PrintTemplate{Layout=layout});var text=Text(doc);
            Check(delivered.MotorSerials.All(x=>text.Contains(x.SerialNumber)),"All motor serials printed in layout "+layout);
        }
        Check(delivered.MotorSerials.All(x=>Text(PrintService.BuildDailyDocument(daily)).Contains(x.SerialNumber)),"Daily printed report includes motor serial evidence");
        var dialog=new MotorSerialWindow(delivered,service.Slips);
        try
        {
            Check(dialog.SerialRows.Count==5&&dialog.SerialRows[0].SerialNumber=="0000392-A","Serial entry dialog restores existing physical-machine rows");
            dialog.SerialRows[0].SerialNumber="CHUA-LUU";
            Check(delivered.MotorSerials[0].SerialNumber=="0000392-A","Closing serial dialog without confirming cannot change the slip");
        }
        finally{dialog.Close();}
        await service.ChangeSlipStatusAsync(delivered.Id,"cancelled","Trả lại máy vào kho");
        using(var book=new XLWorkbook(service.Settings.ProductExportPath))Check(!book.Worksheet(1).RowsUsed().Any(row=>row.Cell(6).GetString().Contains(delivered.Id)),"Cancelling removes only the physical export entry");
        MotorSerials.Validate(second,service.Slips);checks.Add("Cancelled slip no longer blocks reuse of returned motor serial");
        using(var book=new XLWorkbook(ledgerPath))Check(book.Worksheet("Serial máy kéo").Cell(2,3).GetString()=="Đã hủy","Serial audit sheet retains cancelled status for traceability");
        return checks;
    }
    private static string Text(FixedDocument document)
    {
        var text=new List<string>();
        void Visit(DependencyObject item)
        {
            if(item is TextBlock block)text.Add(block.Text);
            foreach(var child in LogicalTreeHelper.GetChildren(item))if(child is DependencyObject dependency)Visit(dependency);
        }
        foreach(var page in document.Pages)if(page.Child!=null)Visit(page.Child);
        return string.Join("\n",text);
    }
}
