using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

/// <summary>Builds native screens offline; never shows a window, clicks a button or writes real data.</summary>
public static class UiWorkflowDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var evidence=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException("FAIL: "+label);evidence.Add(label);}
        var root=SelfTests.CreateDemoWorkspace();using var service=new WarehouseService(root);await service.InitializeAsync();
        var window=new MainWindow(service);
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        void Call(string method,params object[] args)=>typeof(MainWindow).GetMethod(method,flags)!.Invoke(window,args);
        try
        {
            Check(window.Title==SoftwareInfo.DisplayName,"Native window uses requested software name");
            Call("ShowTemplates");
            Check(Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="+ Tạo mẫu phiếu mới"),"Template screen has explicit create-new-template button");
            Check(Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="Sửa mẫu / căn lề"),"Template screen has explicit edit-template button");
            var source=service.Search(new SearchFilter{ContractsText="05492026",Categories=["Máy kéo"]});
            var draft=service.CreateDraft(source,"internal_summary","2026-10-27");
            Call("EditSlip",draft);
            bool HasFourStates(ComboBox combo)
            {
                var values=combo.Items.Cast<object>().Select(x=>x.GetType().GetProperty("Value")?.GetValue(x)?.ToString()).Where(x=>x!=null).ToHashSet();
                return new[]{"planned","delivered","delayed","cancelled"}.All(values.Contains);
            }
            var state=Children(window).OfType<ComboBox>().Single(HasFourStates);
            Check(state.IsEnabled,"Slip editor supports all four selectable statuses");
            var expander=Children(window).OfType<Expander>().First(x=>x.Header?.ToString()?.StartsWith("Hợp đồng / bộ lọc")==true);
            Check(!expander.IsExpanded,"Loaded slip keeps material workspace visible with contract controls collapsed");
            state.SelectedValue="delayed";
            var reason=Children(window).OfType<TextBlock>().Single(x=>x.Text=="Lý do tạm hoãn / hủy");
            Check(((FrameworkElement)reason.Parent).Visibility==Visibility.Visible,"Temporary delay displays reason editor");
            state.SelectedValue="planned";
            Check(((FrameworkElement)reason.Parent).Visibility==Visibility.Collapsed,"Waiting state hides inapplicable reason editor");
            var empty=typeof(MainWindow).GetField("_draft",flags)!;empty.SetValue(window,null);
            typeof(MainWindow).GetField("_draftReport",flags)!.SetValue(window,null);
            Call("ShowCreate",false);
            Check(Children(window).OfType<TextBlock>().Any(x=>x.Text.Contains("Nhập số hợp đồng để tự kiểm tra")),"Create screen contains immediate contract-warning area");
            Call("ShowSlips");
            var labels=Children(window).OfType<Button>().Select(b=>b.Content?.ToString()??"").ToList();
            Check(new[]{"Chờ xuất","✓  Đã xuất","◷  Tạm hoãn","Hủy phiếu"}.All(labels.Contains),"Slip list exposes all four transition buttons");
        }
        finally{window.Close();}
        return evidence;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        yield return parent;
        foreach(var item in LogicalTreeHelper.GetChildren(parent))
            if(item is DependencyObject child)foreach(var descendant in Children(child))yield return descendant;
    }
}
