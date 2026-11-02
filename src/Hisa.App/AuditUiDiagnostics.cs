using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Hisa.App;

/// <summary>Offline regression checks for data-entry and draft editing. No windows are shown.</summary>
public static class AuditUiDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var checks=new List<string>();
        void Check(bool valid,string text){if(!valid)throw new InvalidOperationException("FAIL: "+text);checks.Add(text);}
        foreach(var sample in new[]{("1,5",1.5m),("1.5",1.5m),("1.234,50",1234.5m),("1,234.50",1234.5m),("1 234,5",1234.5m),("1.234.567",1234567m)})
            Check(QuantityNumbers.TryParse(sample.Item1,out var value)&&value==sample.Item2,"Quantity input parses safely: "+sample.Item1);
        Check(!QuantityNumbers.TryParse("1,2,3",out _)&&!QuantityNumbers.TryParse("1 2",out _)&&!QuantityNumbers.TryParse("",out _),"Malformed quantities rejected instead of silently changing magnitude");
        var converter=new QuantityValueConverter();
        Check(converter.ConvertBack("1.5",typeof(decimal),new object(),CultureInfo.GetCultureInfo("vi-VN")) is decimal n&&n==1.5m,"Quantity grid accepts decimal dot under Vietnamese culture");
        Check(!new QuantityEntryRule().Validate("-1",CultureInfo.CurrentCulture).IsValid&&!new QuantityEntryRule().Validate("abc",CultureInfo.CurrentCulture).IsValid,"Invalid or negative grid quantity cannot update the bound material");
        Check(new CategoryStat{Units=new Dictionary<string,decimal>{{"mét",0.001m}}}.QuantityLabel.Contains("0,001"),"Category quantities retain small source fractions");
        var scope=new Slip{Status="delivered",Contracts=["123","456"],Lines=[new MaterialLine{Contract="123",Include=true},new MaterialLine{Contract="456",Include=false}]};
        Check(scope.ContractLabel=="123","Exported slip label does not claim unchecked contract was shipped");
        var flags=BindingFlags.Static|BindingFlags.NonPublic;var grid=new DataGrid();
        grid.SetBinding(FrameworkElement.TagProperty,new Binding("Quantity"){Source=new MaterialLine{Quantity=1}});
        var expression=BindingOperations.GetBindingExpression(grid,FrameworkElement.TagProperty)!;
        Validation.MarkInvalid(expression,new ValidationError(new ExceptionValidationRule(),expression,"Số lượng không hợp lệ",null));
        var rejected=false;
        try{typeof(MainWindow).GetMethod("Commit",flags)!.Invoke(null,[grid]);}
        catch(TargetInvocationException ex)when(ex.InnerException is InvalidOperationException){rejected=true;}
        Check(rejected,"Save/print/serial commit stops on a grid validation error");Validation.ClearInvalid(expression);

        var root=SelfTests.CreateDemoWorkspace();using var service=new WarehouseService(root);await service.InitializeAsync();
        var report=service.Search(new SearchFilter{ContractsText="05492026",ArrivalDate="2026-10-16",Categories=["Máy kéo"]});
        var blocked=false;try{await service.ExportReportAsync(service.Settings.MasterPath,report);}catch(ArgumentException){blocked=true;}
        Check(blocked,"Report export cannot overwrite the master workbook");
        blocked=false;try{await service.ExportDailyAsync(service.Settings.ProductExportPath,service.BuildDaily("2026-10-27"));}catch(ArgumentException){blocked=true;}
        Check(blocked,"Daily report export cannot overwrite the canonical shipment ledger");
        var original=service.CreateDraft(report,"internal_summary","2026-10-27");SerialTestData.Fill(original,"AUDIT-UI");
        var saved=await service.SaveSlipAsync(original);saved=WarehouseService.Clone(saved);saved.Name="Phiếu cũ đã sửa";saved.Note="Ghi chú cần giữ";saved.Status="delayed";saved.DelayReason="Chờ xe";
        var fresh=service.CreateDraft(report,"contract_delivery","2026-10-29");fresh.Lines[0].Quantity++;
        var reloaded=MainWindow.ReloadDraftMaterials(saved,fresh);
        Check(reloaded.Id==saved.Id&&reloaded.SlipCode==saved.SlipCode&&reloaded.CreatedAt==saved.CreatedAt,"Reloading materials keeps existing slip identity, code and creation date");
        Check(reloaded.Name==saved.Name&&reloaded.Note==saved.Note&&reloaded.Status=="delayed"&&reloaded.DelayReason=="Chờ xe"&&reloaded.HandoverDate==saved.HandoverDate,"Reloading materials preserves edited slip header and current state");
        Check(reloaded.Lines[0].Quantity==fresh.Lines[0].Quantity&&reloaded.MotorSerials.Count==6&&reloaded.MotorSerials.Count(x=>x.SerialNumber.Length==0)==1,"Reload updates material quantities and retains only matching motor serials");
        Check(saved.Lines[0].Quantity==2&&saved.MotorSerials.Count==5,"Reloaded draft cannot mutate original saved source objects");
        var template=WarehouseService.Clone(service.Templates.Single(t=>t.Id=="internal_summary"));template.IncludeOther=true;await service.SaveTemplateAsync(template);
        saved.TemplateId=template.Id;saved.CancelReason="Khách hủy";
        var window=new MainWindow(service);var methods=BindingFlags.Instance|BindingFlags.NonPublic;
        void Call(string method,params object[] args)=>typeof(MainWindow).GetMethod(method,methods)!.Invoke(window,args);
        try
        {
            Call("EditSlip",saved);
            var groups=Children(window).OfType<CheckBox>()
                .Where(x=>HandoverMaterialGroups.Options
                    .Any(group=>group.Label==x.Content?.ToString())).ToList();
            Check(groups.Count==6 && !Children(window).OfType<CheckBox>()
                .Any(x=>x.Content?.ToString()=="Khác"),
                "Slip filter shows exactly six requested material groups; even a legacy IncludeOther template cannot silently select Khác");
            bool States(ComboBox combo)=>combo.Items.Cast<object>().Any(x=>x.GetType().GetProperty("Value")?.GetValue(x)?.ToString()=="cancelled")&&combo.Items.Count==4;
            var state=Children(window).OfType<ComboBox>().Single(States);
            var reason=Children(window).OfType<TextBox>().Single(x=>x.Text=="Chờ xe");
            state.SelectedValue="cancelled";Check(reason.Text=="Khách hủy","Changing from delay to cancel uses cancellation reason, not delay reason");
            reason.Text="Lý do hủy đã sửa";state.SelectedValue="delayed";Check(reason.Text=="Chờ xe","Switching back restores the independent delay reason");
            var name=Children(window).OfType<TextBox>().Single(x=>x.Text==saved.Name);name.Text="Tên đang sửa chưa lưu";
            Call("Navigate","dashboard");
            var draft=(Slip)typeof(MainWindow).GetField("_draft",methods)!.GetValue(window)!;
            Check(draft.Name=="Tên đang sửa chưa lưu","Navigating away retains current draft header edits");
        }
        finally{window.Close();}
        return checks;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        yield return parent;
        foreach(var item in LogicalTreeHelper.GetChildren(parent))if(item is DependencyObject child)foreach(var nested in Children(child))yield return nested;
    }
}
