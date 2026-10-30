using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hisa.App;

public sealed class MotorSerialWindow:Window
{
    public List<MotorSerialEntry> ResultSerials {get;private set;}=[];
    public List<MotorSerialEntry> SerialRows {get;}
    private readonly Slip _working;
    private readonly List<Slip> _history;
    private readonly bool _requireAll;
    private readonly DataGrid _grid;
    private readonly TextBlock _error;
    public MotorSerialWindow(Slip slip,IEnumerable<Slip> history,bool requireAll=true)
    {
        _working=WarehouseService.Clone(slip);_history=history.ToList();_requireAll=requireAll;SerialRows=MotorSerials.BuildRows(_working);
        Title=requireAll?"Nhập serial máy kéo trước khi xuất hàng":"Serial máy kéo · nhập và sửa";
        Icon=BitmapFrame.Create(new Uri(BrandingAssets.IconUri));Width=1200;Height=720;MinWidth=900;MinHeight=540;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(243,246,250));FontFamily=new FontFamily("Segoe UI");
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("Theme.xaml",UriKind.Relative)});
        _grid=new DataGrid{ItemsSource=SerialRows,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=true,SelectionUnit=DataGridSelectionUnit.CellOrRowHeader,RowHeight=38};
        var root=new DockPanel{Margin=new Thickness(22)};
        var top=new DockPanel{Margin=new Thickness(0,0,0,15)};
        var logo=new Image{Source=BrandingAssets.Logo,Width=66,Height=66,Margin=new Thickness(0,0,18,0)};DockPanel.SetDock(logo,Dock.Left);top.Children.Add(logo);
        var heading=new StackPanel();heading.Children.Add(new TextBlock{Text="SERIAL MÁY KÉO XUẤT KHO",FontSize=22,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(24,53,77))});
        heading.Children.Add(new TextBlock{Text=$"{SerialRows.Count:N0} máy · {slip.ContractLabel} · {(slip.SlipCode.Length>0?slip.SlipCode:"Phiếu mới")}",Margin=new Thickness(0,6,0,0),TextWrapping=TextWrapping.Wrap});
        heading.Children.Add(new TextBlock{Text=requireAll?"Mỗi máy một serial. Kiểm tra đúng công trình và model; phải nhập đủ trước khi xác nhận Đã xuất.":"Có thể lưu serial còn thiếu ở phiếu chờ. Khi xuất hàng, phần mềm sẽ yêu cầu nhập đủ.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,0)});top.Children.Add(heading);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};
        _error=new TextBlock{Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};bottom.Children.Add(_error);
        var paste=new TextBox{AcceptsReturn=true,Height=58,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,ToolTip="Mỗi dòng một serial, cùng thứ tự các dòng trong bảng"};
        var pastePanel=new StackPanel();pastePanel.Children.Add(new TextBlock{Text="Dán danh sách serial · mỗi dòng một số, theo thứ tự trong bảng",Foreground=Brushes.DimGray});pastePanel.Children.Add(paste);
        pastePanel.Children.Add(ActionButton("Điền danh sách vào bảng",(_,_)=>
        {
            var values=paste.Text.Split(['\r','\n',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            if(values.Length!=SerialRows.Count){_error.Text=$"Danh sách có {values.Length} serial, cần đúng {SerialRows.Count}. Chưa thay đổi bảng.";return;}
            _grid.CommitEdit(DataGridEditingUnit.Cell,true);_grid.CommitEdit(DataGridEditingUnit.Row,true);
            for(int i=0;i<values.Length;i++)SerialRows[i].SerialNumber=values[i];_grid.Items.Refresh();_error.Text="Đã điền vào bảng. Kiểm tra từng hợp đồng / máy trước khi lưu.";
        }));
        bottom.Children.Add(new Expander{Header="Nhập nhanh nhiều serial",Content=pastePanel});
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(ActionButton(requireAll?"Xác nhận serial & tiếp tục":"Lưu serial vào phiếu",(_,_)=>
        {
            try
            {
                _grid.CommitEdit(DataGridEditingUnit.Cell,true);_grid.CommitEdit(DataGridEditingUnit.Row,true);
                _working.MotorSerials=SerialRows.Select(x=>x.Copy()).ToList();MotorSerials.Validate(_working,_history,_requireAll);
                ResultSerials=_working.MotorSerials;DialogResult=true;
            }
            catch(ArgumentException ex){_error.Text=ex.Message;}
        },true));
        actions.Children.Add(ActionButton("Quay lại",(_,_)=>DialogResult=false));bottom.Children.Add(actions);DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        foreach(var column in new[]{("Số hợp đồng","Contract",115d),("Công trình","Project",235d),("Máy kéo / model","Material",285d),("Máy thứ","UnitIndex",75d)})_grid.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new Binding(column.Item2),IsReadOnly=true,Width=column.Item3});
        _grid.Columns.Add(new DataGridTextColumn{Header="Số serial",Binding=new Binding("SerialNumber"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=190});
        root.Children.Add(_grid);Content=root;
    }
    private Button ActionButton(string text,RoutedEventHandler action,bool primary=false)
    {
        var button=new Button{Content=text,Margin=new Thickness(4,6,0,0),Padding=new Thickness(15,8,15,8)};
        if(primary)button.Style=(Style)FindResource("PrimaryButton");button.Click+=action;return button;
    }
}
