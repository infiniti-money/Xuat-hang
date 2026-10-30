using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Hisa.App;

public partial class MainWindow
{
    private Border LiveContractWarnings(TextBox input,Func<string> excludeId,Func<IReadOnlyList<ContractRow>?>? targets=null)
    {
        var host=new Border{CornerRadius=new CornerRadius(7),Padding=new Thickness(12),Margin=new Thickness(0,8,0,0),Background=Brush("#EDF5F7")};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};long request=0;bool attached=false;
        void schedule()
        {
            request++;timer.Stop();
            if(Rules.Tokens(input.Text).Count==0){host.Child=T("Nhập số hợp đồng để tự kiểm tra dữ liệu, xuất hàng, bàn giao và tạm hoãn.",12,false,Muted);return;}
            host.Child=T("Đang kiểm tra hợp đồng và lịch sử phiếu…",12,false,Muted);timer.Start();
        }
        timer.Tick+=async(_,_)=>
        {
            timer.Stop();var ticket=request;var text=input.Text;var excluded=excludeId();var scope=targets?.Invoke();
            try
            {
                var result=await Task.Run(()=>_service.CheckSlipWarnings(text,excluded,scope));
                if(ticket!=request||!host.IsLoaded)return;
                host.Background=Brush(result.HasWarnings?"#FFF5E8":"#EAF7F4");
                var body=Stack(T(result.Summary,12,true,Brush(result.HasWarnings?"#9A561B":"#147A70")));
                if(result.HasWarnings)
                {
                    var excerpt=string.Join("\n",result.Items.Take(2).Select(x=>$"{x.Contract}: {x.Label}"+(x.SlipCode.Length>0?$" · {x.SlipCode}":"")+(x.Date.Length>0?$" · {x.DateLabel}":"")));
                    if(excerpt.Length>0)body.Children.Add(T(excerpt,11,false,Muted));
                    body.Children.Add(B("Xem toàn bộ cảnh báo",()=>ShowWarningDetails(result,false)));
                }
                host.Child=body;
            }
            catch(Exception ex){if(ticket==request&&host.IsLoaded)host.Child=Alert("Chưa kiểm tra được: "+ex.Message,true);}
        };
        EventHandler updated=(_,_)=>Dispatcher.BeginInvoke(schedule);
        input.TextChanged+=(_,_)=>schedule();
        host.Loaded+=(_,_)=>{if(!attached){_service.Changed+=updated;attached=true;}schedule();};
        host.Unloaded+=(_,_)=>{request++;timer.Stop();if(attached){_service.Changed-=updated;attached=false;}};
        schedule();return host;
    }
    private bool ShowWarningDetails(SlipWarningReport report,bool confirm)
    {
        var dialog=new Window{Owner=this,Title=confirm?"Kiểm tra cảnh báo trước khi lưu":"Chi tiết cảnh báo hợp đồng",Width=1080,Height=640,MinWidth=850,MinHeight=450,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#F3F6FA")};
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("Theme.xaml",UriKind.Relative)});
        var table=Table(report.Items,("Cảnh báo","Label",155),("Số HĐ","Contract",115),("Công trình","Project",220),("Mã phiếu","SlipCode",205),("Ngày","DateLabel",110),("Lý do / chi tiết","Message",360),("Ghi chú","Reason",220));
        var top=Stack(T(report.Summary,16,true));
        if(report.Warnings.Count>0)top.Children.Add(Alert(string.Join("\n",report.Warnings),true));
        top.Children.Add(T(confirm?"Kiểm tra mã phiếu, công trình và ngày trước khi tiếp tục. Phiếu chờ chỉ thể hiện đã lập phiếu; trạng thái Đã xuất mới xác nhận xuất và bàn giao.":"Đối chiếu từng công trình riêng; lịch sử khác công trình không được gắn tự động vào phiếu.",12,false,Muted));
        var buttons=Row();
        if(confirm)buttons.Children.Add(B("Đã kiểm tra · tiếp tục",()=>dialog.DialogResult=true,true));
        buttons.Children.Add(B(confirm?"Quay lại kiểm tra":"Đóng",()=>dialog.DialogResult=false));
        var root=new Grid{Margin=new Thickness(20)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.Children.Add(top);Grid.SetRow(table,1);root.Children.Add(table);Grid.SetRow(buttons,2);root.Children.Add(buttons);dialog.Content=root;
        return dialog.ShowDialog()==true;
    }
    private static List<ContractRow> SlipTargets(Slip slip)=>slip.ContractPackages.Select(x=>new ContractRow{Contract=x.Contract,Project=x.Project,Company=x.Company})
        .Concat(slip.Lines.Select(x=>new ContractRow{Contract=x.Contract,Project=x.Project,Company=x.Company})).Where(x=>x.Contract.Length>0).DistinctBy(x=>x.Identity).ToList();
    private bool ConfirmSlipWarnings(Slip slip)
    {
        var targets=SlipTargets(slip);
        var codes=string.Join("\n",slip.Contracts.Concat(targets.Select(x=>x.Contract)).Where(x=>x.Length>0).DistinctBy(Rules.Contract));
        var report=_service.CheckSlipWarnings(codes,slip.Id,targets);
        return !report.HasWarnings||ShowWarningDetails(report,true);
    }
    private bool ConfirmWarnings(ContractReport report)
    {
        var targets=report.Rows.Where(x=>x.Selected).Concat(report.Missing.Where(x=>x.Selected)).ToList();
        var fresh=_service.CheckSlipWarnings(string.Join("\n",targets.Select(x=>x.Contract).DistinctBy(Rules.Contract)),projectTargets:targets);
        return !fresh.HasWarnings||ShowWarningDetails(fresh,true);
    }
}
