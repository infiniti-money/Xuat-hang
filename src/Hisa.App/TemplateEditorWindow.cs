using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hisa.App;

/// <summary>Editable native template designer; modifying a template never modifies a saved slip.</summary>
public sealed class TemplateEditorWindow : Window
{
    public PrintTemplate ResultTemplate { get; private set; }
    private readonly Dictionary<string,FrameworkElement> controls=[];
    private readonly Dictionary<string,TextBox> signatures=[];
    private readonly TextBlock error;
    public TemplateEditorWindow(PrintTemplate template)
    {
        ResultTemplate=Clone(template);
        Title="Thiết kế mẫu phiếu in · "+template.Name;Width=1020;Height=840;MinWidth=800;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FontFamily=new FontFamily("Segoe UI");FontSize=13;Background=new SolidColorBrush(Color.FromRgb(242,246,250));
        var root=new DockPanel();
        var heading=new StackPanel{Margin=new Thickness(24,20,24,15)};
        heading.Children.Add(new TextBlock{Text="THIẾT KẾ MẪU PHIẾU IN",FontWeight=FontWeights.Bold,FontSize=22,Foreground=new SolidColorBrush(Color.FromRgb(20,48,70))});
        heading.Children.Add(new TextBlock{Text="Chỉnh nội dung, cột bảng, chữ ký và kích thước trang. Ngày, mã phiếu và thông tin hợp đồng được tự động điền khi in.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0),Foreground=Brushes.DimGray});
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var bottom=new Border{Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(219,229,237)),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(22,14,22,14)};
        var bottomGrid=new DockPanel();
        var save=Button("Lưu mẫu",true);save.Click+=Save;DockPanel.SetDock(save,Dock.Right);bottomGrid.Children.Add(save);
        var cancel=Button("Hủy",false);cancel.Click+=(_,_)=>DialogResult=false;DockPanel.SetDock(cancel,Dock.Right);bottomGrid.Children.Add(cancel);
        var preview=Button("Xem trước với dữ liệu mẫu",false);preview.Click+=Preview;DockPanel.SetDock(preview,Dock.Left);bottomGrid.Children.Add(preview);
        error=new TextBlock{Text="",Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,12,0)};bottomGrid.Children.Add(error);bottom.Child=bottomGrid;DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var tabs=new TabControl{Margin=new Thickness(22,0,22,18)};
        var general=Tab(tabs,"Nội dung phiếu");
        Add(general,"Tên mẫu",nameof(PrintTemplate.Name));
        AddCombo(general,"Bố cục phiếu",nameof(PrintTemplate.Layout),[("internal_summary","Biên bản nội bộ · bảng nhiều hợp đồng"),("contract_delivery","Bàn giao vật tư · riêng từng công trình"),("standard","Phiếu bàn giao vật tư")]);
        Add(general,"Tên công ty",nameof(PrintTemplate.Company),true);
        Add(general,"Chi nhánh / kho",nameof(PrintTemplate.Branch));
        Add(general,"Tiêu đề lớn",nameof(PrintTemplate.Title),true);
        Add(general,"Dòng phụ dưới tiêu đề",nameof(PrintTemplate.Subtitle),true);
        Add(general,"Địa điểm bàn giao",nameof(PrintTemplate.Location));
        Add(general,"Nội dung mở đầu",nameof(PrintTemplate.OpeningText),true);
        Add(general,"Tiêu đề danh sách bổ sung",nameof(PrintTemplate.SupplementaryLabel));
        Add(general,"Nội dung xác nhận",nameof(PrintTemplate.Note),true);
        Add(general,"Chân phiếu",nameof(PrintTemplate.FooterText),true);
        var columns=Tab(tabs,"Cột bảng & mã phiếu");
        Add(columns,"Tiền tố mã phiếu mới",nameof(PrintTemplate.CodePrefix));
        Add(columns,"Nhãn mã phiếu",nameof(PrintTemplate.CodeLabel));
        Add(columns,"Nhãn ngày xuất",nameof(PrintTemplate.DateLabel));
        Add(columns,"Nhãn tên công trình",nameof(PrintTemplate.ProjectLabel));
        Add(columns,"Nhãn công ty / đơn vị",nameof(PrintTemplate.CompanyLabel));
        Add(columns,"Nhãn số hợp đồng",nameof(PrintTemplate.ContractLabel));
        Add(columns,"Nhãn tên vật tư",nameof(PrintTemplate.MaterialLabel));
        Add(columns,"Nhãn đơn vị tính",nameof(PrintTemplate.UnitLabel));
        Add(columns,"Nhãn số lượng",nameof(PrintTemplate.QuantityLabel));
        Add(columns,"Số lượng kế toán",nameof(PrintTemplate.AccountingQtyLabel));
        Add(columns,"Số lượng thực tế",nameof(PrintTemplate.ActualQtyLabel));
        Add(columns,"Nhãn ghi chú",nameof(PrintTemplate.NotesLabel));
        Add(columns,"Trước tên nhóm vật tư",nameof(PrintTemplate.CategoryPrefix));
        Add(columns,"Sau tên nhóm vật tư",nameof(PrintTemplate.CategorySuffix));
        Add(columns,"Dòng tối thiểu (phiếu đơn)",nameof(PrintTemplate.MinRows));
        AddCheck(columns,"In tiêu đề từng nhóm vật tư",nameof(PrintTemplate.ShowCategory));
        AddCheck(columns,"In đơn vị tính",nameof(PrintTemplate.ShowUnit));
        AddCheck(columns,"In số lượng",nameof(PrintTemplate.ShowQuantity));
        AddCheck(columns,"Thêm nguồn / số kiện vào ghi chú",nameof(PrintTemplate.ShowSource));
        AddCheck(columns,"Mặc định lấy thêm vật tư khác",nameof(PrintTemplate.IncludeOther));
        var page=Tab(tabs,"Trang giấy & căn chỉnh");
        AddCombo(page,"Khổ giấy",nameof(PrintTemplate.PaperSize),[("A4","A4 · 210 × 297 mm"),("A5","A5 · 148 × 210 mm")]);
        AddCombo(page,"Chiều giấy",nameof(PrintTemplate.Orientation),[("landscape","Ngang"),("portrait","Dọc")]);
        AddCombo(page,"Phông chữ",nameof(PrintTemplate.FontFamily),[("Times New Roman","Times New Roman"),("Arial","Arial"),("Calibri","Calibri"),("Tahoma","Tahoma"),("Segoe UI","Segoe UI")]);
        Add(page,"Cỡ chữ nội dung (pt)",nameof(PrintTemplate.FontSize));
        Add(page,"Cỡ chữ trong bảng (pt)",nameof(PrintTemplate.TableFontSize));
        Add(page,"Cỡ chữ tiêu đề (pt)",nameof(PrintTemplate.TitleFontSize));
        Add(page,"Lề trên (mm)",nameof(PrintTemplate.MarginTop));
        Add(page,"Lề dưới (mm)",nameof(PrintTemplate.MarginBottom));
        Add(page,"Lề trái (mm)",nameof(PrintTemplate.MarginLeft));
        Add(page,"Lề phải (mm)",nameof(PrintTemplate.MarginRight));
        AddAlign(page,"Căn tên công ty",nameof(PrintTemplate.CompanyAlign));
        AddAlign(page,"Căn tiêu đề",nameof(PrintTemplate.TitleAlign));
        AddAlign(page,"Căn thông tin hợp đồng",nameof(PrintTemplate.MetaAlign));
        AddAlign(page,"Căn nội dung bảng",nameof(PrintTemplate.TableAlign));
        AddAlign(page,"Căn chữ ký",nameof(PrintTemplate.SignatureAlign));
        var sign=Tab(tabs,"Chữ ký");
        sign.Children.Add(new TextBlock{Text="Ba vị trí ký được đặt cùng một trang. Có thể đổi tên chức danh theo quy trình bàn giao của kho.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16),Foreground=Brushes.DimGray});
        for(var i=0;i<3;i++)
        {
            var box=new TextBox{Text=template.Signatures.ElementAtOrDefault(i)??"",Padding=new Thickness(10,8,10,8)};
            signatures["signature"+i]=box;AddRow(sign,"Vị trí ký "+(i+1),box);
        }
        Add(sign,"Dòng hướng dẫn ký",nameof(PrintTemplate.SignatureCaption));
        root.Children.Add(tabs);Content=root;
    }
    private static PrintTemplate Clone(PrintTemplate template)=>JsonSerializer.Deserialize<PrintTemplate>(JsonSerializer.Serialize(template))!;
    private Button Button(string text,bool primary)=>new(){Content=text,Padding=new Thickness(18,9,18,9),Margin=new Thickness(6,0,0,0),Background=primary?new SolidColorBrush(Color.FromRgb(14,116,132)):Brushes.White,Foreground=primary?Brushes.White:new SolidColorBrush(Color.FromRgb(32,57,79)),BorderBrush=new SolidColorBrush(Color.FromRgb(203,216,227)),BorderThickness=new Thickness(1),MinHeight=38};
    private static StackPanel Tab(TabControl tabs,string title)
    {
        var panel=new StackPanel{Margin=new Thickness(20)};
        tabs.Items.Add(new TabItem{Header=title,Padding=new Thickness(12,9,12,9),Content=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=panel}});return panel;
    }
    private void Add(StackPanel parent,string label,string property,bool multiline=false)
    {
        var value=typeof(PrintTemplate).GetProperty(property)!.GetValue(ResultTemplate);
        var box=new TextBox{Text=Convert.ToString(value,CultureInfo.InvariantCulture)??"",Padding=new Thickness(9,7,9,7),MinHeight=34,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,AcceptsReturn=multiline,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};if(multiline)box.MinHeight=58;
        controls[property]=box;AddRow(parent,label,box);
    }
    private void AddCombo(StackPanel parent,string label,string property,(string Key,string Label)[] values)
    {
        var combo=new ComboBox{Padding=new Thickness(9,7,9,7),MinHeight=34,ItemsSource=values.Select(x=>new KeyValuePair<string,string>(x.Key,x.Label)).ToList(),DisplayMemberPath="Value",SelectedValuePath="Key",SelectedValue=typeof(PrintTemplate).GetProperty(property)!.GetValue(ResultTemplate)};
        controls[property]=combo;AddRow(parent,label,combo);
    }
    private void AddAlign(StackPanel parent,string label,string property)=>AddCombo(parent,label,property,[("left","Trái"),("center","Giữa"),("right","Phải")]);
    private void AddCheck(StackPanel parent,string label,string property)
    {
        var check=new CheckBox{Content=label,IsChecked=(bool)typeof(PrintTemplate).GetProperty(property)!.GetValue(ResultTemplate)!,Padding=new Thickness(0,8,0,8),Margin=new Thickness(235,2,0,2)};controls[property]=check;parent.Children.Add(check);
    }
    private static void AddRow(StackPanel parent,string label,FrameworkElement control)
    {
        var row=new Grid{Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(235)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var text=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,16,0),Foreground=new SolidColorBrush(Color.FromRgb(49,70,89))};row.Children.Add(text);Grid.SetColumn(control,1);row.Children.Add(control);parent.Children.Add(row);
    }
    private PrintTemplate Read()
    {
        var result=Clone(ResultTemplate);
        foreach(var (name,control) in controls)
        {
            var property=typeof(PrintTemplate).GetProperty(name)!;object? value;
            if(control is CheckBox check)value=check.IsChecked==true;
            else if(control is ComboBox combo)value=combo.SelectedValue?.ToString()??"";
            else
            {
                var text=((TextBox)control).Text.Trim();
                if(property.PropertyType==typeof(double)){if(!double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))throw new ArgumentException("Giá trị không hợp lệ ở trường "+name);value=n;}
                else if(property.PropertyType==typeof(int)){if(!int.TryParse(text,out var n))throw new ArgumentException("Số dòng tối thiểu phải là số nguyên.");value=n;}
                else value=text;
            }
            property.SetValue(result,value);
        }
        if(result.Name.Length==0)throw new ArgumentException("Hãy nhập tên mẫu.");
        if(result.Title.Length==0)throw new ArgumentException("Hãy nhập tiêu đề phiếu.");
        if(result.CodePrefix.Length==0)throw new ArgumentException("Hãy nhập tiền tố mã phiếu.");
        if(result.FontSize<6||result.FontSize>24||result.TableFontSize<6||result.TableFontSize>20||result.TitleFontSize<10||result.TitleFontSize>32)throw new ArgumentException("Cỡ chữ nội dung 6–24 pt, bảng 6–20 pt, tiêu đề 10–32 pt.");
        if(new[]{result.MarginTop,result.MarginBottom,result.MarginLeft,result.MarginRight}.Any(x=>x<3||x>40))throw new ArgumentException("Các lề in phải từ 3 đến 40 mm.");
        if(result.MinRows<0||result.MinRows>100)throw new ArgumentException("Số dòng tối thiểu từ 0 đến 100.");
        result.Signatures=signatures.Values.Select(x=>x.Text.Trim()).ToList();if(result.Signatures.All(string.IsNullOrWhiteSpace))throw new ArgumentException("Hãy nhập ít nhất một vị trí ký.");
        PrintService.ValidateTemplate(result);
        return result;
    }
    private void Save(object sender,RoutedEventArgs e){try{ResultTemplate=Read();DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}}
    private void Preview(object sender,RoutedEventArgs e)
    {
        try
        {
            var template=Read();error.Text="";
            var sample=new Slip{SlipCode=template.CodePrefix+"-"+DateTime.Today.ToString("yyyyMMdd")+"-001",Name="Phiếu mẫu kiểm tra bố cục",HandoverDate=DateTime.Today.ToString("yyyy-MM-dd"),TemplateId=template.Id,Contracts=["32622026"],Lines=[new(){Contract="32622026",Project="Công ty Cổ phần Đầu tư và Du lịch Liêm Sơn · Dự án Trung tâm thương mại dịch vụ tại Khu công nghiệp Thái Hà, tỉnh Ninh Bình",Company="Đức An",Material="Đầu cửa tầng 1000 OCM01-C · 2 cánh (Ningbo)",Unit="Bộ",Quantity=3,Category="Đầu cửa tầng"},new(){Contract="32622026",Project="Công ty Cổ phần Đầu tư và Du lịch Liêm Sơn · Dự án Trung tâm thương mại dịch vụ tại Khu công nghiệp Thái Hà, tỉnh Ninh Bình",Company="Đức An",Material="Ray dẫn hướng T78 (cây/5m)",Unit="mét",Quantity=30,Category="Ray dẫn hướng",Note="Đối chiếu số lượng thực tế khi giao"}]};
            PrintService.PreviewSlip(this,sample,template);
        }
        catch(Exception ex){error.Text=ex.Message;}
    }
}
