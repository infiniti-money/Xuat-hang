using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hisa.App;

/// <summary>
/// Explicit project confirmation when one contract in MÁY KÉO.xlsx has
/// multiple different projects. Does not change the slip's project or Excel.
/// </summary>
public sealed class MotorSerialProjectWindow : Window
{
    private readonly ComboBox _projects;
    private readonly TextBlock _error;
    public string SelectedProject { get; private set; } = "";

    public MotorSerialProjectWindow(string contract,string slipProject,
        IReadOnlyList<string> sourceProjects)
    {
        Title="Chọn công trình máy kéo · HĐ "+contract;
        Width=650;Height=365;MinWidth=520;MinHeight=330;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(243,246,250));
        FontFamily=new FontFamily("Segoe UI");
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock
        {
            Text="HỢP ĐỒNG CÓ NHIỀU CÔNG TRÌNH",
            FontSize=19,FontWeight=FontWeights.Bold,
            TextWrapping=TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text="Số HĐ: "+contract+" · Công trình trên phiếu: "+slipProject,
            FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,8),
            TextWrapping=TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text="Chọn công trình đích trong file MÁY KÉO.xlsx (cột B). "+
                 "Nếu tên khác trên phiếu, HISA sẽ hỏi xác nhận riêng; "+
                 "không bao giờ tự chọn công trình chỉ vì trùng số HĐ.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        });
        _projects=new ComboBox
        {
            ItemsSource=sourceProjects,SelectedIndex=-1,
            MinHeight=38,Margin=new Thickness(0,0,0,10)
        };
        panel.Children.Add(_projects);
        _error=new TextBlock
        {
            Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,3,0,10)
        };
        panel.Children.Add(_error);
        var buttons=new StackPanel
        {
            Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right
        };
        var confirm=new Button{Content="Xác nhận công trình",
            Padding=new Thickness(16,8,16,8),Margin=new Thickness(0,0,10,0)};
        confirm.Click+=(_,_) =>
        {
            if(_projects.SelectedItem is not string selected)
            {
                _error.Text="Hãy chọn một công trình trước khi nhập serial.";
                return;
            }
            if(Rules.Code(selected)!=Rules.Code(slipProject))
            {
                var confirm=MessageBox.Show(this,
                    "Công trình trên phiếu: "+slipProject+"\n"+
                    "Công trình máy kéo được chọn: "+selected+"\n\n"+
                    "Tên công trình không giống nhau. Chỉ tiếp tục nếu bạn đã "+
                    "đối chiếu và xác nhận đây là cùng công trình thực tế. "+
                    "Serial mới sẽ ghi vào cột I thuộc công trình đã chọn, "+
                    "không tự đổi tên công trình trên phiếu.",
                    "Xác nhận ghép công trình khác tên",
                    MessageBoxButton.YesNo,MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if(confirm!=MessageBoxResult.Yes)
                {
                    _error.Text="Chưa xác nhận ghép công trình khác tên. "+
                        "Hãy chọn đúng hoặc Bỏ qua.";
                    return;
                }
            }
            SelectedProject=selected;
            DialogResult=true;
        };
        var cancel=new Button{Content="Bỏ qua",Padding=new Thickness(16,8,16,8)};
        cancel.Click+=(_,_)=>DialogResult=false;
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content=new ScrollViewer{Content=panel,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}
