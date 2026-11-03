using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    /// <summary>
    /// An explicit, one-step, read-only Excel source choice for contract numbers
    /// with multiple projects/towers. Never selects every tower by default.
    /// Cancel leaves the current Zalo schedule and the warehouse untouched.
    /// </summary>
    private Dictionary<AiScheduleLine,List<ContractRow>>? ChooseHandoverProjects(
        ContractReport report,IReadOnlyList<AiScheduleLine> selected)
    {
        var ambiguous=selected.Where(x=>
            AiHandoverSelection.RequiresProjectChoice(report,x)).ToList();
        var result=new Dictionary<AiScheduleLine,List<ContractRow>>();
        if(ambiguous.Count==0)return result;

        var dialog=new Window
        {
            Owner=this,Title="Chọn đúng thang/công trình để lập phiếu",
            Width=860,Height=610,MinWidth=660,MinHeight=420,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=Brush("#F3F6FA")
        };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source=new Uri("Theme.xaml",UriKind.Relative) });
        var header=Card(Stack(
            T("CHỌN THANG / CÔNG TRÌNH THEO EXCEL",18,true),
            T("Một số hợp đồng có nhiều thang hoặc công trình trong Tổng hợp.xlsx. "+
              "Tích đúng Thang 01, Thang 02 hoặc cả hai nếu thực tế đều bàn giao. "+
              "Danh sách vật tư chỉ lấy từ những dòng Excel được tích.",12,false,Muted)));
        var optionPanel=Stack();
        var controls=new Dictionary<AiScheduleLine,List<(ContractRow Row,CheckBox Box)>>();
        foreach(var ai in ambiguous)
        {
            var checks=new List<(ContractRow Row,CheckBox Box)>();
            var group=Stack(
                T("HĐ "+ai.Contract+" · "+AiHandoverSelection.SourceOptions(report,ai).Count+
                    " lựa chọn",16,true),
                T("Ảnh Zalo: "+ai.Project+" · "+ai.Company,12,false,Muted));
            foreach(var source in AiHandoverSelection.SourceOptions(report,ai))
            {
                var checkbox=new CheckBox
                {
                    IsChecked=false,
                    Margin=new Thickness(4,9,4,9),
                    FontSize=14,
                    VerticalContentAlignment=VerticalAlignment.Center,
                    Content=Stack(
                        T(source.Project,14,true),
                        T("Đơn vị: "+source.Company+
                          " · "+source.PackageCount+" kiện · "+
                          source.Materials.Count+" dòng vật tư theo nhóm đang chọn",
                          12,false,Muted))
                };
                checks.Add((source,checkbox));
                group.Children.Add(checkbox);
            }
            controls.Add(ai,checks);
            optionPanel.Children.Add(Card(group));
        }
        var buttons=Row(
            B("✓ Dùng công trình đã chọn → Lập phiếu chờ",()=>
            {
                foreach(var pair in controls)
                {
                    var rows=pair.Value.Where(x=>x.Box.IsChecked==true)
                        .Select(x=>x.Row).ToList();
                    if(rows.Count==0)
                    {
                        MessageBox.Show(dialog,"Hãy tích ít nhất một thang/công trình cho HĐ "+
                            pair.Key.Contract+". Nếu chưa rõ thì chọn Hủy để kiểm tra "+
                            "dữ liệu Excel trước khi lập phiếu.",
                            "Chưa chọn thang",MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }
                    result[pair.Key]=rows;
                }
                dialog.DialogResult=true;
            },true),
            B("Hủy · quay về danh sách HĐ",()=>dialog.DialogResult=false));
        var body=new DockPanel {LastChildFill=true};
        DockPanel.SetDock(header,Dock.Top);
        DockPanel.SetDock(buttons,Dock.Bottom);
        body.Children.Add(header);
        body.Children.Add(buttons);
        body.Children.Add(new ScrollViewer
        {
            Content=optionPanel,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto
        });
        dialog.Content=new Border
        {
            Padding=new Thickness(20),
            Child=body
        };
        return dialog.ShowDialog()==true?result:null;
    }
}
