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
            var navigation=(StackPanel)window.FindName("Navigation")!;
            Check(navigation.Children.OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Cấu hình nguồn dữ liệu")==true),
                "Excel source settings has a dedicated visible navigation entry");
            Call("ShowSettings");
            var configScroll=(ScrollViewer)((Grid)window.FindName("PageHost")!).Children[0];
            var configStack=(StackPanel)configScroll.Content;
            var sourceCard=(Border)configStack.Children[1];
            Check(Children(sourceCard).OfType<TextBlock>().Any(t=>t.Text=="Nguồn dữ liệu Excel"),
                "Source configuration is first on settings page, above Gemini");
            Check(Children(sourceCard).OfType<Button>().Any(b=>b.Content?.ToString()=="Chọn thư mục nguồn"),
                "Source folder browser button is available without scrolling past Gemini");
            var simulated=AiScheduleWorkspace.NewDraft(new DateTime(2026,10,31));
            simulated.Arrivals.Add(new AiScheduleLine{Contract="56062026",Project="Công trình A",Company="HISA",PlannedDate=simulated.ArrivalDate});
            simulated.Dispatches.Add(new AiScheduleLine{Contract="13482026",Project="Công trình B",Company="HISA",PlannedDate=simulated.DispatchDate});
            typeof(MainWindow).GetField("_aiDocument",flags)!.SetValue(window,simulated);
            Call("ShowAiSchedules");
            var aiHost=(Grid)window.FindName("PageHost")!;
            var aiLayout=(Grid)aiHost.Children[0];
            var aiScroll=aiLayout.Children.OfType<ScrollViewer>().Single();
            var aiPanel=(StackPanel)aiScroll.Content;
            var headings=Children(aiPanel).OfType<TextBlock>().Select(x=>x.Text).ToArray();
            Check(headings.Any(x=>x.Contains("1 · CONT DỰ KIẾN VỀ")) &&
                  headings.Any(x=>x.Contains("2 · HỢP ĐỒNG DỰ KIẾN BÀN GIAO")),
                "Gemini result separates potential container arrival and waiting handover slips");
            Check(Children(aiPanel).OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Đối chiếu cont đã về")==true) &&
                  Children(aiPanel).OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Lập phiếu từ hợp đồng")==true),
                "Arrival verification and draft handover have direct buttons next to their lists");
            var aiGrids=Children(aiPanel).OfType<DataGrid>().ToList();
            Check(aiGrids.Count==2 &&
                  aiGrids[0].Columns.OfType<DataGridCheckBoxColumn>().Any(c=>c.Header?.ToString()=="Cont đã về?") &&
                  aiGrids[1].Columns.OfType<DataGridCheckBoxColumn>().Any(c=>c.Header?.ToString()=="Làm phiếu"),
                "Two workflows have independent checkbox columns");
            Check(!simulated.Arrivals[0].SelectedForArrival && !simulated.Dispatches[0].SelectedForHandover &&
                  string.IsNullOrWhiteSpace(simulated.ApprovedAt),
                "Opening Gemini results does not mark arrival, issue a slip or approve schedule");
            Check(Children(aiPanel).OfType<Expander>().Count(x=>!x.IsExpanded)>=2,
                "Optional manual entry and warnings stay below the main flow and are collapsed");
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
