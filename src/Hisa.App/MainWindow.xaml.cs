using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Hisa.App;

public partial class MainWindow : Window
{
    private readonly WarehouseService _service;
    private readonly Dictionary<string,Button> _nav = [];
    private string _page = "dashboard";
    private bool _createPlanMode;
    private bool _running;
    private bool _loaded;
    private ContractReport? _lastReport;
    private SearchFilter _lookupFilter = new();
    private Slip? _draft;
    private ContractReport? _draftReport;
    private string _slipStatus = "all";
    private string _slipQuery = "";
    private string _packageQuery = "";
    private DailyReport? _daily;
    private string _dailyDay = DateTime.Today.ToString("yyyy-MM-dd");
    private static readonly Brush Muted = Brush("#70869B");
    private static readonly Brush Ink = Brush("#18354D");

    public MainWindow(WarehouseService service)
    {
        InitializeComponent();
        _service = service;
        Title = SoftwareInfo.DisplayName;
        VersionText.Text="Phiên bản "+SoftwareInfo.Version;
        AutoUpdater.UpdateStaged+=version=>Dispatcher.BeginInvoke(()=>
        {
            TopUpdateButton.Content="↑  Cập nhật "+version;
            TopUpdateButton.ToolTip="Đã tải và xác minh bản mới. Nhấn để xem và cài đặt.";
        });
        AutoUpdater.UpdateProgress+=message=>Dispatcher.BeginInvoke(()=>
        {
            StatusText.Text=message;
            if(_checkingUpdate && message.StartsWith("Đang tải cập nhật:",StringComparison.Ordinal))
            {
                var percent=message.Split('%')[0].Replace("Đang tải cập nhật:","").Trim();
                TopUpdateButton.Content="↓ Tải "+percent+"%";
            }
        });
        TodayText.Text=DateTime.Now.ToString("dddd, dd/MM/yyyy",CultureInfo.GetCultureInfo("vi-VN"));
        NavCaption("CÔNG VIỆC HÔM NAY");
        AddNav("dashboard", "▦", "Tổng quan / việc cần làm");
        AddNav("dispatch-hub", "▤", "Điều hành xuất hàng");
        AddNav("arrival-hub", "↓", "Cont & kiện Hưng Yên");
        AddNav("motor-hung-yen", "⚙", "Máy kéo gia công tại Hưng Yên");
        NavCaption("TRA CỨU & HỖ TRỢ");
        AddNav("lookup", "⌕", "Tra cứu hợp đồng");
        AddNav("ai-hub", "✧", "Trung tâm Gemini AI");
        NavCaption("QUẢN LÝ & HỒ SƠ");
        AddNav("management-hub", "▥", "Quản lý & lịch sử");
        AddNav("settings", "⚙", "Cấu hình nguồn dữ liệu");
        _service.Progress += message => Dispatcher.BeginInvoke(()=>StatusText.Text = message);
        _service.Changed += (_,_) => Dispatcher.BeginInvoke(()=>
        {
            UpdateStatus();
            if (_loaded && !_running && _page=="dashboard") ShowDashboard();
        });
        Loaded += async (_,_) =>
        {
            InitializeMayBackground();
            ShowDashboard();
            _loaded=true;
            try{await _service.InitializeAsync();if(_page=="dashboard")ShowDashboard();}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Không thể tải dữ liệu",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };
        Closed += (_,_)=>{StopHandsFreeSession();_geminiMicrophone.Dispose();_geminiVoice.Dispose();_service.Dispose();};
        PreviewKeyDown += (_,e)=>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { Navigate("lookup"); e.Handled=true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { _draft=null;_draftReport=null;_createPlanMode=false; Navigate("create"); e.Handled=true; }
            if (Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.D1) { Navigate("dashboard");e.Handled=true; }
            if (Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.D2) { Navigate("dispatch-hub");e.Handled=true; }
            if (Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.D3) { Navigate("arrival-hub");e.Handled=true; }
            if (Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.D4) { Navigate("ai-hub");e.Handled=true; }
            if (e.Key==Key.F5) { _ = Run(()=>_service.RefreshAsync(true)); e.Handled=true; }
        };
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private void AddNav(string key,string icon,string title)
    {
        var button = new Button { Content=$"{icon}    {title}", Style=(Style)FindResource("NavButton"), Tag=key };
        button.Click += (_,_)=>{if(key=="create")_createPlanMode=false;Navigate(key);};
        _nav[key]=button; Navigation.Children.Add(button);
    }
    private void NavCaption(string title) => Navigation.Children.Add(new TextBlock {Text=title, FontSize=10, Foreground=Brush("#729BAF"),FontWeight=FontWeights.SemiBold,Margin=new Thickness(25,20,0,8)});
    private void Navigate(string page)
    {
        if(_page=="create"&&_draft!=null)_captureDraftEditor?.Invoke();
        _captureDraftEditor=null;_captureDraftHeader=null;
        if(_page=="gemini-chat" && !_geminiHandsFreeActive)
        {
            // Manual recording remains page-scoped. A separately consented,
            // explicitly activated Mây hands-free session stays active across
            // HISA screens; its Stop control is always in the top toolbar.
            StopHandsFreeSession();
            _geminiMicrophone.Stop(false);
        }
        _page=page;
        HighlightNavigation(page);
        switch(page)
        {
            case "dashboard":ShowDashboard();break;
            case "dispatch-hub":ShowDispatchHub();break;
            case "arrival-hub":ShowArrivalHub();break;
            case "motor-hung-yen":ShowMotorHungYen();break;
            case "ai-hub":ShowAiHub();break;
            case "management-hub":ShowManagementHub();break;
            case "reports-hub":ShowManagementHub();break; // Legacy navigation link
            case "duplicate-review":ShowDuplicateReview();break;
            case "lookup":ShowLookup();break;
            case "packages":ShowPackages();break;
            case "create":ShowCreate(_createPlanMode);break;
            case "plan":_createPlanMode=true;ShowCreate(true);break;
            case "slips":ShowSlips();break;
            case "flags":ShowFlags();break;
            case "daily":ShowDaily();break;
            case "gemini-chat":ShowGeminiChat();break;
            case "ai-schedule":ShowAiSchedules();break;
            case "readiness":ShowDispatchReadiness();break;
            case "planned-arrivals":ShowPlannedArrivals();break;
            case "gemini-excel":ShowGeminiExcelAssistant();break;
            case "arrival":ShowArrival();break;
            case "arrival-packages":ShowArrivalPackages();break;
            case "templates":ShowTemplates();break;
            case "settings":ShowSettings();break;
            case "information":ShowInformation();break;
        }
    }
    internal static string NavigationGroup(string page) => page switch
    {
        "create" or "plan" or "readiness" => "dispatch-hub",
        "arrival" or "planned-arrivals" or "arrival-packages" or "packages" or
        "duplicate-review" => "arrival-hub",
        "gemini-chat" or "ai-schedule" => "ai-hub",
        "flags" or "slips" or "daily" or "templates" or "information" or
        "reports-hub" => "management-hub",
        _ => page
    };

    private void HighlightNavigation(string page)
    {
        var group=NavigationGroup(page);
        foreach(var pair in _nav)
        {
            var active=pair.Key==group;
            pair.Value.Background=active?Brush("#24445B"):Brushes.Transparent;
            pair.Value.BorderBrush=active?Brush("#52D4C5"):Brushes.Transparent;
            pair.Value.Foreground=active?Brushes.White:Brush("#ADC3D5");
        }
    }
    private void SetPage(string title,string subtitle,FrameworkElement content)
    {
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        BreadcrumbText.Text = "HISA  /  " + title.ToUpper(CultureInfo.GetCultureInfo("vi-VN"));
        TodayText.Text = DateTime.Now.ToString("dddd, dd/MM/yyyy",CultureInfo.GetCultureInfo("vi-VN"));
        HighlightNavigation(_page);
        PageHost.Children.Clear();
        PageHost.Children.Add(content);
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        StatusText.Text=_service.IsBusy?"Đang cập nhật dữ liệu…":!string.IsNullOrEmpty(_service.LastError)?_service.LastError:$"Nguồn chính: {System.IO.Path.GetFileName(_service.Settings.MasterPath)}   ·   {_service.Snapshot.Packages.Count:N0} kiện Hưng Yên   ·   {_service.Warnings.Count} cảnh báo";
        LoadTimeText.Text=$"Tải dữ liệu {_service.LastLoadMs:N0} ms  ·  {DateTime.Now:dd/MM/yyyy}";
    }
    private async Task Run(Func<Task> action)
    {
        if (_running) return;
        _running=true;RefreshButton.IsEnabled=false;PageHost.IsEnabled=false;Navigation.IsEnabled=false;
        try { await action(); }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"Không thể hoàn tất",MessageBoxButton.OK,MessageBoxImage.Warning);StatusText.Text=ex.Message; }
        finally { _running=false;RefreshButton.IsEnabled=true;PageHost.IsEnabled=true;Navigation.IsEnabled=true;UpdateStatus(); }
    }
    private async void RefreshClick(object sender,RoutedEventArgs e)=>await Run(async()=>{await _service.RefreshAsync(true);Navigate(_page);});
    private Button B(string title,Action action,bool primary=false,bool danger=false)
    {
        var b=new Button {Content=title};
        if(primary)b.Style=(Style)FindResource("PrimaryButton");else if(danger)b.Style=(Style)FindResource("DangerButton");
        b.Click+=(_,_)=>action();return b;
    }
    private Button A(string title,Func<Task> action,bool primary=false,bool danger=false)=>B(title,()=>_ = Run(action),primary,danger);
    private static TextBlock T(string text,double size=13,bool bold=false,Brush? color=null)=>new(){Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap};
    private static StackPanel Stack(params UIElement[] children){var p=new StackPanel();foreach(var c in children)p.Children.Add(c);return p;}
    private static WrapPanel Row(params UIElement[] children){var p=new WrapPanel {VerticalAlignment=VerticalAlignment.Center};foreach(var c in children)p.Children.Add(c);return p;}
    private static Border Card(UIElement child,Thickness? padding=null)=>new(){Child=child,Background=Brushes.White,BorderBrush=Brush("#DFE7EF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=padding??new Thickness(18),Margin=new Thickness(0,0,0,14)};
    private static FrameworkElement Field(string label,FrameworkElement control,double width=0){var p=Stack(T(label,11,true,Muted),control);if(width>0)p.Width=width;return p;}
    private static TextBox Input(string value="",bool multi=false,double width=0)
    {
        var input=new TextBox {Text=value,AcceptsReturn=multi,TextWrapping=multi?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=multi?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};
        if(multi){input.Height=74;input.VerticalContentAlignment=VerticalAlignment.Top;}if(width>0)input.Width=width;return input;
    }
    private static DatePicker DateInput(string? day=null)=>new(){SelectedDate=DateTime.TryParse(day,out var d)?d:null,Width=155};
    private static string Day(DatePicker d)=>d.SelectedDate?.ToString("yyyy-MM-dd")??"";
    private static Grid Layout(FrameworkElement top,FrameworkElement body)
    {var g=new Grid();g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});g.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});g.Children.Add(top);Grid.SetRow(body,1);g.Children.Add(body);return g;}
    private static TabControl Tabs(params (string Title,FrameworkElement Body)[] tabs)
    {var t=new TabControl {BorderBrush=Brush("#DFE7EF"),Background=Brushes.White};foreach(var x in tabs)t.Items.Add(new TabItem{Header=x.Title,Content=x.Body});return t;}
    private static DataGrid Table(IEnumerable items,params (string Label,string Path,double Width)[] columns)
    {
        var grid=new DataGrid {ItemsSource=items,Margin=new Thickness(0,4,0,0)};
        foreach(var c in columns)grid.Columns.Add(new DataGridTextColumn {Header=c.Label,Binding=new Binding(c.Path){Mode=BindingMode.OneWay},Width=c.Width<0?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(c.Width),SortMemberPath=c.Path,IsReadOnly=true});
        return grid;
    }
    private static DataGrid ContractTable(IEnumerable<ContractRow> rows,bool selectable=false)
    {
        var g=Table(rows,("Số hợp đồng","Contract",125),("Công trình","Project",-1),("Công ty","Company",150),("Kiện","PackageCount",65),("Dòng VT","ItemCount",75),("Ngày cont về","ArrivalDates",175),("Trạng thái / lịch sử","StatusLabel",210));
        if(selectable){g.IsReadOnly=false;g.Columns.Insert(0,new DataGridCheckBoxColumn {Header="Chọn",Binding=new Binding("Selected"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=55});}
        return g;
    }
    private static DataGrid MaterialsTable(IEnumerable<MaterialLine> lines,bool editable=false)
    {
        var g=Table(lines,("Số HĐ","Contract",115),("Công trình","Project",220),("Tên vật tư","Material",-1),("ĐVT","Unit",75),("Số lượng","Quantity",95),("Nhóm","Group",125),("Ghi chú","Note",180));
        if(editable)
        {
            g.IsReadOnly=false;
            g.Columns.Insert(0,new DataGridCheckBoxColumn {Header="Xuất / in",Binding=new Binding("Include"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=78});
            foreach(var column in g.Columns.OfType<DataGridTextColumn>())if(column.SortMemberPath is "Material" or "Unit" or "Quantity" or "Note")
            {
                column.IsReadOnly=false;var binding=new Binding(column.SortMemberPath){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true};
                if(column.SortMemberPath=="Quantity"){binding.Converter=new QuantityValueConverter();binding.ValidationRules.Add(new QuantityEntryRule());}
                column.Binding=binding;
            }
        }
        return g;
    }
    private static FrameworkElement Alert(string text,bool error=false)
    {return new Border{Background=Brush(error?"#FFF1EC":"#EDF7F6"),BorderBrush=Brush(error?"#F1D1C4":"#C5E4DF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12),Margin=new Thickness(0,0,0,12),Child=T(text,12,false,Brush(error?"#9A5339":"#286E69"))};}
    private static void Commit(DataGrid grid)
    {
        bool invalid(DependencyObject item)
        {
            if(Validation.GetHasError(item))return true;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)if(invalid(VisualTreeHelper.GetChild(item,i)))return true;
            return false;
        }
        if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true)||invalid(grid))throw new InvalidOperationException("Có ô dữ liệu chưa hợp lệ. Kiểm tra các ô đánh dấu đỏ trước khi lưu, in hoặc nhập serial.");
    }
    private static string? SavePath(Window owner,string name,string extension="xlsx")
    {var dialog=new SaveFileDialog{FileName=name,DefaultExt="."+extension,Filter=extension=="zip"?"Bản sao lưu ZIP|*.zip":"Bảng tính Excel|*.xlsx",AddExtension=true};return dialog.ShowDialog(owner)==true?dialog.FileName:null;}
    private static string? OpenPath(Window owner)
    {var dialog=new OpenFileDialog{Filter="Bảng tính Excel|*.xlsx"};return dialog.ShowDialog(owner)==true?dialog.FileName:null;}
    private void Notify(string text)=>MessageBox.Show(this,text,SoftwareInfo.DisplayName,MessageBoxButton.OK,MessageBoxImage.Information);
    private bool Ask(string text)=>MessageBox.Show(this,text,"Kiểm tra trước khi ghi",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;

    private void ShowDashboard()
    {
        _page="dashboard";
        var report=_service.Search(new SearchFilter());
        var p=Stack();
        p.Children.Add(DashboardWelcomeCard());
        // Kế hoạch xuất được duyệt là nguồn chính; không suy ra sẵn hàng từ lịch cont về.
        try
        {
            var readinessStore = new DispatchReadinessStore(_service.DataDirectory);
            var readinessState = readinessStore.Load();
            var tomorrow = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            // Work due today is always visible; when today's plan exists, also show tomorrow.
            // Both are independent forecasts, never confirmation goods have shipped.
            var todayPlans = DispatchReadinessStore.Evaluate(readinessState, _service.Snapshot, today);
            var tomorrowPlans = DispatchReadinessStore.Evaluate(readinessState, _service.Snapshot, tomorrow);
            if (todayPlans.Count>0)
            {
                p.Children.Add(DashboardPlanCard(todayPlans,today));
                p.Children.Add(DashboardPlanCard(tomorrowPlans,tomorrow));
            }
            else p.Children.Add(DashboardPlanCard(tomorrowPlans,tomorrow));
        }
        catch (Exception ex)
        {
            p.Children.Add(Alert("Không đọc được kế hoạch chuẩn bị. File gốc được giữ nguyên: " + ex.Message, true));
        }
        p.Children.Add(DashboardInboundSummary());
        p.Children.Add(StyledDashboardPanel(Stack(
            T("MỞ NHANH THEO CÔNG VIỆC", 12, true, Brush("#658296")),
            Row(B("▤  Điều hành xuất hàng",()=>Navigate("dispatch-hub"),true),
                B("↓  Cont & kiện về kho",()=>Navigate("arrival-hub")),
                B("✧  Trợ lý Gemini AI",()=>Navigate("ai-hub")),
                B("⌕  Tra cứu hợp đồng",()=>Navigate("lookup"))),
            T("① Kiểm tra lịch xuất  →  ② Chuẩn bị vật tư  →  ③ Lập phiếu chờ  →  ④ Duyệt bàn giao",
                11,false,Muted))));
        var kpis=new UniformGrid{Columns=3,Margin=new Thickness(0,12,0,12)};
        Kpi(kpis,"KIỆN HƯNG YÊN",_service.Snapshot.Packages.Count,"Mở danh sách kiện, xem đầy đủ vật tư",()=>Navigate("packages"));
        Kpi(kpis,"SỐ HỢP ĐỒNG",report.ContractCount,"Tách riêng từng công trình cùng số HĐ",()=>Navigate("lookup"));
        Kpi(kpis,"DÒNG VẬT TƯ",_service.Snapshot.Packages.Sum(x=>x.Items.Count),"Dữ liệu chính từ Tổng hợp.xlsx",()=>Navigate("lookup"));
        p.Children.Add(kpis);
        var groupPanel=new UniformGrid {Columns=3};
        foreach(var stat in report.Categories)
        {
            var inner=Stack(T(stat.Category,13,true),T($"{stat.ContractCount:N0} hợp đồng",24,true),T($"{stat.LineCount:N0} dòng · {stat.QuantityLabel}",11,false,Muted));
            foreach(FrameworkElement element in inner.Children)element.Margin=new Thickness(0,0,0,6);
            var cat=stat.Category;
            var button=B("",()=>{_lookupFilter=new SearchFilter{Categories=[cat]};_lastReport=null;Navigate("lookup");});button.Content=inner;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(18);button.Margin=new Thickness(0,0,12,12);groupPanel.Children.Add(button);
        }
        p.Children.Add(new Expander{
            Header="XEM NHÓM VẬT TƯ  /  BẤM ĐỂ MỞ",
            Content=Card(Stack(T("Vật tư theo sáu nhóm bàn giao",16,true),
                T("Bấm vào nhóm để tra cứu hợp đồng và danh mục vật tư.",12,false,Muted),groupPanel)),
            IsExpanded=false
        });
        var pending=_service.Slips.Count(x=>x.Status=="planned");var delayed=_service.Slips.Count(x=>x.Status=="delayed");var delivered=_service.Slips.Count(x=>x.Status=="delivered");
        p.Children.Add(Row(B($"Chờ xuất  {pending:N0}",()=>{_slipStatus="planned";Navigate("slips");}),B($"Hoãn chờ xuất  {delayed:N0}",()=>{_slipStatus="delayed";Navigate("slips");}),B($"Đã xuất  {delivered:N0}",()=>{_slipStatus="delivered";Navigate("slips");})));
        var recent=Table(_service.Slips.OrderByDescending(x=>x.UpdatedAt).Take(12).ToList(),("Mã phiếu","SlipCode",235),("Ngày xuất","DateLabel",110),("Số hợp đồng","ContractLabel",-1),("Trạng thái","StatusLabel",150),("Dòng VT","LineCount",80));recent.Height=255;recent.MouseDoubleClick+=(_,_)=>{if(recent.SelectedItem is Slip s)EditSlip(s);};
        p.Children.Add(new Expander{
            Header="12 PHIẾU GẦN ĐÂY  /  BẤM ĐỂ XEM",
            Content=Card(Stack(T("Phiếu gần đây",16,true),recent)),
            IsExpanded=false
        });
        if(_service.Warnings.Count>0)p.Children.Add(new Expander{Header=$"{_service.Warnings.Count} thông báo cần đối chiếu",Content=Alert(string.Join("\n",_service.Warnings),true),IsExpanded=false});
        SetPage("Tổng quan kho","Hưng Yên · hợp đồng, kiện hàng và tiến độ xuất kho",new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    private void Kpi(Panel parent,string label,int number,string description,Action action)
    {
        var inner=Stack(
            T(label,10,true,Brush("#188A90")),
            new TextBlock {Text=number.ToString("N0"),FontSize=32,
                Foreground=Ink,FontWeight=FontWeights.Bold,Margin=new Thickness(0,9,0,6)},
            T(description,11,false,Muted));
        var button=B("",action);
        button.Content=inner;
        button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        button.Background=Brush("#FFFFFF");
        button.BorderBrush=Brush("#E0E9F1");
        button.Padding=new Thickness(18,17,18,17);
        button.Margin=new Thickness(0,0,11,0);
        parent.Children.Add(button);
    }

    private void ShowLookup()
    {
        _page="lookup";
        var filter=_lookupFilter;
        var codes=Input(filter.ContractsText,true);codes.MinWidth=340;
        var query=Input(filter.Query);var keys=Input(filter.KeywordsText);
        var arrival=DateInput(filter.ArrivalDate);var shipped=DateInput(filter.ShipDate);
        var categories=new Dictionary<string,CheckBox>();var chips=new WrapPanel();
        foreach(var cat in Rules.Categories.Take(6)){var c=new CheckBox{Content=cat,IsChecked=filter.Categories.Contains(cat)};categories[cat]=c;chips.Children.Add(c);}
        var first=new Grid();first.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});first.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        first.Children.Add(Field("MỘT HOẶC NHIỀU SỐ HỢP ĐỒNG · NGĂN CÁCH BẰNG DẤU PHẨY / XUỐNG DÒNG",codes));var right=Stack(Field("Tìm nhanh theo tên công trình / công ty",query),Field("Từ khóa vật tư bổ sung · phân cách dấu phẩy",keys));Grid.SetColumn(right,1);first.Children.Add(right);
        var body=new ContentControl();
        Action search=()=>
        {
            _lookupFilter=new SearchFilter{ContractsText=codes.Text,Query=query.Text,KeywordsText=keys.Text,Categories=categories.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToList(),ArrivalDate=Day(arrival),ShipDate=Day(shipped)};
            var watch=System.Diagnostics.Stopwatch.StartNew();_lastReport=_service.Search(_lookupFilter);watch.Stop();
            foreach(var missing in _lastReport.Missing)missing.Selected=false;
            body.Content=ReportBody(_lastReport,true);StatusText.Text=$"Tra cứu trong {watch.ElapsedMilliseconds} ms · {_lastReport.Summary}";
        };
        var toolbar=Card(Stack(first,chips,Row(Field("Ngày cont về · tùy chọn",arrival,175),Field("Ngày đã xuất · tùy chọn",shipped,175),B("⌕  Tra cứu",search,true),B("Xóa bộ lọc",()=>{_lookupFilter=new();_lastReport=null;ShowLookup();})),T("Tra cứu đúng mã hợp đồng. Bỏ chọn các nhóm để xem toàn bộ vật tư Hưng Yên. Cùng mã nhưng khác công trình được giữ riêng.",11,false,Muted)));
        SetPage("Tra cứu hợp đồng","Ctrl+F · tìm nhiều hợp đồng và nhiều nhóm vật tư cùng lúc",Layout(toolbar,body));
        search();
    }
    private FrameworkElement ReportBody(ContractReport report,bool actions)
    {
        var header=Stack();
        if(!report.DataAvailable)header.Children.Add(Alert("Chưa xác nhận được dữ liệu chính. Hãy kiểm tra nguồn Tổng hợp.xlsx và cập nhật trước khi lập phiếu.",true));
        if(report.Warnings.Count>0)header.Children.Add(new Expander{Header=$"Thông báo nguồn / đối chiếu · {report.Warnings.Count}",Content=Alert(string.Join("\n",report.Warnings),true),IsExpanded=!report.DataAvailable,Margin=new Thickness(0,0,0,8)});
        header.Children.Add(T(report.DataAvailable?report.Summary:$"{report.RequestedCount:N0} mã yêu cầu · chưa xác nhận được dữ liệu từ Tổng hợp.xlsx",14,true));
        header.Children.Add(T($"Đã nhập {report.RequestedCount:N0} mã · {report.WithSlips:N0} HĐ đã lên phiếu · {report.CancelledCount:N0} HĐ có phiếu hủy. Số HĐ được đếm theo mã; một HĐ có thể thuộc nhiều nhóm vật tư và trạng thái.",11,false,Muted));
        var contracts=ContractTable(report.Rows,true);contracts.MouseDoubleClick+=(_,_)=>{if(contracts.SelectedItem is ContractRow r)ContractDetails(r);};
        var categories=Table(report.Categories,("Nhóm vật tư","Category",160),("Số HĐ","ContractCount",100),("Công trình","ProjectCount",100),("Dòng vật tư","LineCount",110),("Tổng theo ĐVT","QuantityLabel",-1));
        categories.MouseDoubleClick+=(_,_)=>{if(categories.SelectedItem is CategoryStat c)ShowDialogGrid(c.Category,ContractTable(c.Contracts));};
        var statusRows=report.StatusDetails.SelectMany(x=>x.Value.Select(r=>new{State=StateName(x.Key),r.Contract,r.Project,r.Company,Slips=string.Join(", ",r.Slips.Select(s=>s.SlipCode)),r.Warning})).ToList();
        var statuses=Table(statusRows,("Trạng thái","State",160),("Số HĐ","Contract",120),("Công trình","Project",-1),("Công ty","Company",140),("Phiếu","Slips",230),("Ghi chú","Warning",240));
        var missing=ContractTable(report.Missing,true);missing.Columns.Add(new DataGridTextColumn{Header="Chi tiết cảnh báo",Binding=new Binding("Warning"),Width=300});
        var noMaterial=ContractTable(report.NoMaterialMatch,actions);noMaterial.Columns.Add(new DataGridTextColumn{Header="Lý do",Binding=new Binding("Warning"),Width=280});
        var excluded=ContractTable(report.DateMismatch);excluded.Columns.Add(new DataGridTextColumn{Header="Lý do",Binding=new Binding("Warning"),Width=280});
        var materials=MaterialsTable(report.Rows.SelectMany(x=>x.Materials).ToList());
        var tabs=Tabs(($"Hợp đồng ({report.Rows.Count:N0})",contracts),($"Không có vật tư ({report.NoMaterialMatch.Count:N0})",noMaterial),("Vật tư chi tiết",materials),("Sáu nhóm vật tư",categories),("Trạng thái / phiếu",statuses),($"{(report.DataAvailable?"Chưa có dữ liệu":"Chưa xác nhận")} ({report.Missing.Count})",missing),("Không khớp ngày",excluded));
        if(actions)
        {
            var bar=Row(B("☑ Chọn tất cả HĐ có vật tư",()=>
            {
                Commit(contracts);
                foreach(var row in report.Rows)row.Selected=row.Materials.Count>0;
                foreach(var row in report.NoMaterialMatch.Concat(report.Missing))row.Selected=false;
                contracts.Items.Refresh();noMaterial.Items.Refresh();missing.Items.Refresh();
            }),B("Bỏ chọn tất cả",()=>
            {
                foreach(var row in report.Rows.Concat(report.NoMaterialMatch).Concat(report.Missing))row.Selected=false;
                contracts.Items.Refresh();noMaterial.Items.Refresh();missing.Items.Refresh();
            }),B("Tạo phiếu từ HĐ đã chọn",()=>{Commit(contracts);Commit(noMaterial);StartDraft(report,false);},true),B("Lập kế hoạch riêng từng HĐ",()=>{Commit(contracts);Commit(noMaterial);Commit(missing);StartDraft(report,true);}),B("In báo cáo",()=>PrintService.PreviewReport(this,report)),A("Xuất Excel",async()=>{var path=SavePath(this,$"Bao cao hop dong {DateTime.Now:yyyyMMdd-HHmm}.xlsx");if(path!=null){await _service.ExportReportAsync(path,report);Notify("Đã xuất báo cáo: "+path);}}));
            header.Children.Add(bar);
        }
        return Layout(header,tabs);
    }
    private static string StateName(string state)=>state switch{"withSlips"=>"Đã lên phiếu","cancelPickup"=>"Hủy lấy hàng",_=>Rules.StatusLabel(state)};
    private void ContractDetails(ContractRow row)
    {
        var history=Stack(T($"{row.Contract} · {row.Project}",19,true),T(row.Company,13,false,Muted),T($"{row.PackageCount} kiện · {row.ItemCount} dòng · ngày về {row.ArrivalDates}",12,false,Muted),T(row.StatusLabel,12,true));
        if(row.Warning.Length>0)history.Children.Add(Alert(row.Warning,true));
        var tabs=Tabs(("Vật tư",MaterialsTable(row.Materials)),("Kiện hàng",PackageGrid(row.Packages)),("Phiếu bàn giao",SlipGrid(row.Slips)),("Lịch sử sổ xuất",Table(row.Shipments,("Ngày","Date",120),("Nguồn","Source",200),("Phiếu","SlipCode",220),("Ghi chú","Note",-1))));
        ShowDialogContent("Chi tiết hợp đồng "+row.Contract,Layout(history,tabs));
    }
    private void ShowPackages()
    {
        _page="packages";var q=Input(_packageQuery);q.Width=420;
        var grid=PackageGrid([]);var count=T("",13,true);
        Action search=()=>{_packageQuery=q.Text;var key=Rules.Code(q.Text);var packages=_service.Snapshot.Packages.Where(p=>key.Length==0||Rules.Code(p.Contract+" "+p.Project+" "+p.Company).Contains(key)||p.Items.Any(i=>Rules.Code(i.Material).Contains(key))||p.ArrivalDate.Contains(q.Text.Trim())).ToList();grid.ItemsSource=packages;count.Text=$"{packages.Count:N0} kiện · {packages.Sum(p=>p.Items.Count):N0} dòng vật tư";};
        q.KeyDown+=(_,e)=>{if(e.Key==Key.Enter)search();};
        grid.MouseDoubleClick+=(_,_)=>{if(grid.SelectedItem is Package p)ShowDialogContent($"Kiện {p.PackageNo} · HĐ {p.Contract}",Layout(Stack(T(p.Project,19,true),T($"{p.Company} · Ngày về {p.ArrivalLabel}",12,false,Muted)),MaterialsTable(p.Items)));};
        var top=Card(Stack(Row(Field("Số HĐ / công trình / công ty / vật tư / ngày về",q),B("⌕  Tìm kiện",search,true)),count,T("Mỗi khối HƯNG YÊN là một kiện riêng. Nhấp đúp vào kiện để xem vật tư.",11,false,Muted)));
        SetPage("Kiện Hưng Yên","Danh sách kiện từ Tổng hợp.xlsx · không gộp các khối kiện trùng nhau",Layout(top,grid));search();
    }
    private static DataGrid PackageGrid(IEnumerable<Package> items)=>Table(items,("Số HĐ","Contract",120),("Công trình","Project",-1),("Công ty","Company",150),("Kiện số","PackageNo",80),("Ngày cont đi","ContLabel",115),("Ngày cont về","ArrivalLabel",115),("Dòng VT","ItemCount",80),("Nguồn","SourceSheet",140));

    private void StartDraft(ContractReport report,bool plan)
    {
        if(!report.DataAvailable){Notify("Chưa đọc được dữ liệu chính. Hãy cập nhật nguồn trước khi lập phiếu.");return;}
        if(!report.Rows.Any(x=>x.Selected)&&!report.NoMaterialMatch.Any(x=>x.Selected)&&!report.Missing.Any(x=>x.Selected)){Notify("Chọn ít nhất một hợp đồng để lập phiếu.");return;}
        var duplicates=report.Rows.Concat(report.NoMaterialMatch).Where(x=>x.Selected).SelectMany(x=>x.Packages).GroupBy(x=>Rules.Contract(x.Contract)).Where(g=>g.Count()>1).ToList();
        foreach(var group in duplicates)
            if(!Ask($"Số HĐ {group.First().Contract} có {group.Count()} kiện trong nguồn. Xác nhận đây là các kiện/công trình riêng cần giữ cùng phiếu?\n\n{string.Join("\n",group.Select(x=>$"Kiện {x.PackageNo}: {x.Project}"))}"))return;
        _draftReport=Copy(report);
        if(!plan){_draft=_service.CreateDraft(_draftReport,_service.Templates.FirstOrDefault()?.Id??"internal_summary",DateTime.Today.ToString("yyyy-MM-dd"));}
        _createPlanMode=plan;Navigate("create");
    }
    private void ShowCreate(bool plan)
    {
        _captureDraftEditor=null;_captureDraftHeader=null;
        _page="create";var planMode=plan;_createPlanMode=plan;
        var report=_draftReport;
        var codes=Input(report?.Filter.ContractsText??_draft?.ContractLabel??"",true);codes.Width=360;
        var keys=Input(report?.Filter.KeywordsText??"");keys.Width=280;
        var arrival=DateInput(report?.Filter.ArrivalDate);
        var categories=new Dictionary<string,CheckBox>();var chips=new WrapPanel();
        foreach(var cat in Rules.Categories){var c=new CheckBox{Content=cat,IsChecked=report==null?cat!="Khác":report.Filter.Categories.Count==0||report.Filter.Categories.Contains(cat)};categories[cat]=c;chips.Children.Add(c);}
        var content=new ContentControl();
        var mode=new ComboBox{ItemsSource=new[]{new Choice("slip","Phiếu bàn giao chung"),new Choice("plan","Kế hoạch riêng từng công trình")},DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=planMode?"plan":"slip",Width=270};
        var template=new ComboBox{ItemsSource=_service.Templates,DisplayMemberPath="Name",SelectedValuePath="Id",Width=300};template.SelectedValue=_draft?.TemplateId??_service.Templates.FirstOrDefault()?.Id;
        if(report==null)categories["Khác"].IsChecked=FindTemplate(template.SelectedValue?.ToString()??"internal_summary").IncludeOther;
        var otherManuallyChanged=false;categories["Khác"].Click+=(_,_)=>otherManuallyChanged=true;
        template.SelectionChanged+=(_,_)=>{if(!otherManuallyChanged)categories["Khác"].IsChecked=FindTemplate(template.SelectedValue?.ToString()??"internal_summary").IncludeOther;};
        var day=DateInput(_draft?.HandoverDate??_aiDispatchDay??DateTime.Today.ToString("yyyy-MM-dd"));
        var top=Stack();
        Expander? sourceControls=null;
        string signature()=>string.Join("|",Rules.Tokens(codes.Text).Select(Rules.Contract).Order())+"/"+string.Join("|",categories.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key))+"/"+keys.Text+"/"+Day(arrival);
        var loadedSignature=signature();
        bool changed()=>loadedSignature!=signature();
        var liveWarnings=LiveContractWarnings(codes,()=>_draft?.Id??"",()=>_draftReport?.Filter.ProjectTargets.Count>0?_draftReport.Filter.ProjectTargets:_draft!=null?SlipTargets(_draft):null);
        Action render=()=>
        {
            if(!planMode&&_draft!=null&&_draftReport==null){content.Content=SlipEditor(_draft,null,day,template,changed);return;}
            if(_draftReport==null){content.Content=Alert("Nhập một hoặc nhiều mã hợp đồng, chọn nhóm vật tư rồi bấm Tải vật tư. Ứng dụng sẽ kiểm tra dữ liệu thiếu và lịch sử phiếu trước khi lưu.");return;}
            if(!_draftReport.DataAvailable){content.Content=ReportBody(_draftReport,false);return;}
            content.Content=planMode?PlanEditor(_draftReport,day,template,changed):SlipEditor(_draft??=_service.CreateDraft(_draftReport,template.SelectedValue?.ToString()??"internal_summary",Day(day)),_draftReport,day,template,changed);
        };
        mode.SelectionChanged+=(_,_)=>{planMode=mode.SelectedValue?.ToString()=="plan";_createPlanMode=planMode;render();};
        Action load=()=>
        {
            if(Rules.Tokens(codes.Text).Count==0){Notify("Hãy nhập ít nhất một số hợp đồng.");return;}
            if(_draft!=null&&_draft.Lines.Count>0&&!planMode&&!Ask("Tải lại sẽ thay danh sách vật tư đang sửa bằng dữ liệu từ Tổng hợp.xlsx. Tiếp tục?"))return;
            if(!planMode)_captureDraftHeader?.Invoke();
            var sameCodes=Rules.Tokens(codes.Text).Select(Rules.Contract).ToHashSet().SetEquals(Rules.Tokens(_draftReport?.Filter.ContractsText).Select(Rules.Contract));
            var targets=sameCodes?_draftReport?.Filter.ProjectTargets??[]:[];
            _draftReport=_service.Search(new SearchFilter{ContractsText=codes.Text,Categories=categories.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToList(),KeywordsText=keys.Text,ArrivalDate=Day(arrival),ProjectTargets=targets});
            foreach(var row in _draftReport.Missing)row.Selected=false;
            if(_draftReport.DataAvailable)_draft=ReloadDraftMaterials(planMode?null:_draft,_service.CreateDraft(_draftReport,template.SelectedValue?.ToString()??"internal_summary",Day(day)));
            loadedSignature=signature();render();
            if(sourceControls!=null&&_draftReport.DataAvailable){sourceControls.Header="Hợp đồng / bộ lọc / ngày / mẫu in · "+_draftReport.Summary;sourceControls.IsExpanded=false;}
        };
        var controls=Card(Stack(Row(Field("CÁCH LẬP",mode),Field("SỐ HỢP ĐỒNG · MỖI DÒNG / DẤU PHẨY MỘT MÃ",codes),Field("Từ khóa vật tư bổ sung",keys),Field("Giới hạn ngày cont về",arrival)),chips,Row(B("↓  Tải vật tư & kiểm tra",load,true),B("Làm mới nội dung",()=>{_draft=null;_draftReport=null;ShowCreate(planMode);})),Row(Field("Mẫu phiếu",template),Field("Ngày kế hoạch / ngày xuất",day),A("+ Tạo mẫu in",async()=>{var previous=template.SelectedValue;var count=_service.Templates.Count;await CreatePrintTemplate(returnToList:false);template.ItemsSource=_service.Templates;template.SelectedValue=_service.Templates.Count>count?_service.Templates.Last().Id:previous;})),liveWarnings,T(planMode?"Chọn từng công trình để tạo phiếu chờ xuất riêng. Hợp đồng thiếu dữ liệu chỉ được lập khi bạn chủ động chọn.":"Phiếu lưu chờ xuất chưa ghi nhận xuất kho. Khi chuyển sang Đã xuất, ứng dụng cập nhật sổ Thành phẩm và lưu đầy đủ kiện của hợp đồng.",11,false,Muted)));
        sourceControls=new Expander{Header="Hợp đồng / bộ lọc / ngày / mẫu in · mở để nhập hoặc thay đổi",IsExpanded=_draft==null&&_draftReport==null,Content=new ScrollViewer{Content=controls,MaxHeight=Math.Max(300,Math.Min(480,ActualHeight*.55)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        top.Children.Add(sourceControls);
        SetPage("Kế hoạch & phiếu bàn giao","Nhập hợp đồng một lần · chọn lập phiếu chung hoặc tạo kế hoạch riêng từng công trình",Layout(top,content));render();
    }
    private FrameworkElement PlanEditor(ContractReport report,DatePicker day,ComboBox template,Func<bool>? scopeChanged=null)
    {
        var contracts=ContractTable(report.Rows,true);var noMaterial=ContractTable(report.NoMaterialMatch,true);var missing=ContractTable(report.Missing,true);
        noMaterial.Columns.Add(new DataGridTextColumn{Header="Ghi chú",Binding=new Binding("Warning"),Width=260});
        missing.Columns.Add(new DataGridTextColumn{Header="Cảnh báo",Binding=new Binding("Warning"),Width=300});
        var body=Tabs(("Chọn công trình có vật tư",contracts),($"Hợp đồng không có vật tư ({report.NoMaterialMatch.Count})",noMaterial),($"Mã thiếu dữ liệu ({report.Missing.Count})",missing),("Báo cáo kiểm tra",ReportBody(report,false)));
        var top=Stack(T(report.Summary,13,true),Row(A("Lưu các phiếu chờ xuất riêng",async()=>
        {
            Commit(contracts);Commit(noMaterial);Commit(missing);
            if(scopeChanged?.Invoke()==true)throw new InvalidOperationException("Số hợp đồng hoặc bộ lọc đã thay đổi. Bấm Tải vật tư & kiểm tra trước khi lưu.");
            if(!report.DataAvailable)throw new InvalidOperationException("Chưa xác nhận được nguồn dữ liệu. Hãy cập nhật lại trước khi lưu kế hoạch.");
            if(!report.Rows.Any(x=>x.Selected)&&!report.NoMaterialMatch.Any(x=>x.Selected)&&!report.Missing.Any(x=>x.Selected))throw new InvalidOperationException("Chọn ít nhất một công trình.");
            if(!ConfirmWarnings(report))return;
            var slips=await _service.SavePlanAsync(report,Day(day),template.SelectedValue?.ToString()??"internal_summary");
            Notify($"Đã tạo {slips.Count:N0} phiếu chờ xuất riêng. Vào Phiếu bàn giao để chốt Đã xuất hoặc Hoãn từng phiếu.");_draftReport=null;_draft=null;_slipStatus="planned";Navigate("slips");
        },true),B("Chọn tất cả có dữ liệu",()=>{foreach(var r in report.Rows.Concat(report.NoMaterialMatch))r.Selected=true;contracts.Items.Refresh();noMaterial.Items.Refresh();}),B("Bỏ chọn",()=>{foreach(var r in report.Rows.Concat(report.NoMaterialMatch).Concat(report.Missing))r.Selected=false;contracts.Items.Refresh();noMaterial.Items.Refresh();missing.Items.Refresh();}),B("Bỏ chọn HĐ đang hoãn",()=>{var delayed=_service.Slips.Where(s=>s.Status=="delayed").SelectMany(s=>s.ContractPackages.Select(p=>Rules.Identity(p.Contract,p.Project,p.Company)).Concat(s.Contracts.Select(c=>Rules.Contract(c)))).ToHashSet();foreach(var r in report.Rows.Concat(report.NoMaterialMatch).Concat(report.Missing))if(delayed.Contains(Rules.Identity(r.Contract,r.Project,r.Company))||delayed.Contains(Rules.Contract(r.Contract)))r.Selected=false;contracts.Items.Refresh();noMaterial.Items.Refresh();missing.Items.Refresh();}),B("In báo cáo kiểm tra",()=>PrintService.PreviewReport(this,report))));
        return Layout(top,body);
    }
    private FrameworkElement SlipEditor(Slip slip,ContractReport? report,DatePicker day,ComboBox template,Func<bool>? scopeChanged=null)
    {
        var name=Input(slip.Name);name.Width=340;
        var note=Input(slip.Note);note.Width=340;
        var priorExists=slip.SlipCode.Length>0;
        var status=new ComboBox{Width=170,ItemsSource=new[]{new Choice("planned","Chờ xuất"),new Choice("delivered","Đã xuất"),new Choice("delayed","Tạm hoãn"),new Choice("cancelled","Đã hủy")},DisplayMemberPath="Label",SelectedValuePath="Value"};status.SelectedValue=slip.Status;
        var reason=Input(slip.Status=="cancelled"?slip.CancelReason:slip.DelayReason);reason.Width=510;
        var lines=MaterialsTable(slip.Lines,true);
        void readHeader(){slip.Name=name.Text;slip.Note=note.Text;slip.HandoverDate=Day(day);slip.TemplateId=template.SelectedValue?.ToString()??"internal_summary";slip.Status=status.SelectedValue?.ToString()??"planned";if(slip.Status=="delayed")slip.DelayReason=reason.Text.Trim();if(slip.Status=="cancelled")slip.CancelReason=reason.Text.Trim();}
        void read(){Commit(lines);readHeader();}
        _captureDraftEditor=read;
        _captureDraftHeader=readHeader;
        var toolbar=Stack(T($"{(slip.SlipCode.Length>0?slip.SlipCode:"Mã phiếu tự cấp khi lưu")} · {slip.ContractPackages.Count} kiện được lưu kèm · {slip.Lines.Count} dòng vật tư",13,true),Row(Field("Tên phiếu",name),Field("Ghi chú",note),Field("Trạng thái phiếu",status)));
        var reasonField=Field("Lý do tạm hoãn / hủy",reason);toolbar.Children.Add(reasonField);
        void showReason()=>reasonField.Visibility=status.SelectedValue?.ToString() is "delayed" or "cancelled"?Visibility.Visible:Visibility.Collapsed;
        var reasonByState=new Dictionary<string,string>{{"delayed",slip.DelayReason},{"cancelled",slip.CancelReason}};var previousReasonState=slip.Status;
        status.SelectionChanged+=(_,_)=>{if(reasonByState.ContainsKey(previousReasonState))reasonByState[previousReasonState]=reason.Text;previousReasonState=status.SelectedValue?.ToString()??"planned";reason.Text=reasonByState.GetValueOrDefault(previousReasonState,"");showReason();};showReason();
        toolbar.Children.Add(Row(A("Lưu phiếu bàn giao",async()=>
        {
            if(scopeChanged?.Invoke()==true)throw new InvalidOperationException("Số hợp đồng hoặc bộ lọc đã thay đổi. Bấm Tải vật tư & kiểm tra trước khi lưu.");
            read();if(!ConfirmSlipWarnings(slip))return;
            var previous=_service.Slips.FirstOrDefault(x=>x.Id==slip.Id);
            if(previous?.Status=="delivered"&&slip.Status!="delivered"&&!Ask("Phiếu đang đã xuất. Chuyển trạng thái sẽ gỡ các dòng xuất do phiếu này ghi trong sổ Excel. Tiếp tục?"))return;
            if(slip.Status=="delivered"&&!Ask($"Ghi nhận phiếu này ĐÃ XUẤT vào Thành phẩm xuất kho.xlsx và ghi Ngày bàn giao cho đúng seri trong MÁY KÉO.xlsx?\n\n{slip.ContractLabel}"))return;
            if(slip.Status=="delivered"&&!ConfirmMotorSerialsForDelivery(slip))return;
            var saved=await _service.SaveSlipAsync(slip);Notify("Đã lưu phiếu "+saved.SlipCode);_draft=null;_draftReport=null;Navigate("slips");
        },true),B("Xem trước / in phiếu",()=>{if(scopeChanged?.Invoke()==true){Notify("Bạn đã đổi mã hợp đồng/bộ lọc. Tải lại vật tư để xem trước đúng phiếu.");return;}read();PrintSlipWithMotorSerials(slip);}),A("Sửa mẫu phiếu in",async()=>{var editor=new TemplateEditorWindow(FindTemplate(template.SelectedValue?.ToString()??slip.TemplateId)){Owner=this};if(editor.ShowDialog()==true){await _service.SaveTemplateAsync(editor.ResultTemplate);Notify("Đã lưu mẫu in. Bấm Xem trước để kiểm tra.");}}),B("+ Thêm vật tư",()=>{var item=EditLine(slip.Lines.FirstOrDefault());if(item!=null){slip.Lines.Add(item);lines.Items.Refresh();}}),B("Chọn in tất cả",()=>{foreach(var l in slip.Lines)l.Include=true;lines.Items.Refresh();}),B("Bỏ chọn dòng đang chọn",()=>{foreach(var l in lines.SelectedItems.OfType<MaterialLine>())l.Include=false;lines.Items.Refresh();})));
        toolbar.Children.Add(Row(
            B("↓ Lấy serial từ MÁY KÉO.xlsx",()=>
            {
                if(scopeChanged?.Invoke()==true){Notify("Hợp đồng/bộ lọc đã thay đổi; tải lại vật tư trước khi lấy serial.");return;}
                read();
                var result=FillMotorSerialsFromSource(slip);
                Notify("Đã điền "+result.Assigned+" serial từ MÁY KÉO.xlsx vào phiếu."+
                    (result.Missing>0?" Còn "+result.Missing+" máy cần kiểm tra.":"")+
                    (result.Warnings.Count>0?"\n"+string.Join("\n",result.Warnings.Take(5)):""));
            },true),
            B("Kiểm tra / sửa serial",()=>
            {
                if(scopeChanged?.Invoke()==true){Notify("Hợp đồng/bộ lọc đã đổi; tải lại vật tư trước khi nhập serial.");return;}
                read();EditMotorSerials(slip,false);
            }),
            T("Phiếu xuất hoặc bàn giao in kèm serial máy kéo lấy từ file nguồn; không ghi ngày xuất khi còn ở trạng thái Chờ xuất.",11,false,Muted)));
        var tabs=report==null?Tabs(("Vật tư trên phiếu",lines),("Toàn bộ kiện lưu kèm",PackageGrid(slip.ContractPackages))):Tabs(("Vật tư trên phiếu",lines),("Báo cáo kiểm tra HĐ",ReportBody(report,false)),("Toàn bộ kiện lưu kèm",PackageGrid(slip.ContractPackages)));
        return Layout(toolbar,tabs);
    }
    private PrintTemplate FindTemplate(string id)=>_service.Templates.FirstOrDefault(x=>x.Id==id)??_service.Templates.FirstOrDefault()??new();
    private void EditSlip(Slip slip)
    {
        _draft=Copy(slip);_draftReport=null;Navigate("create");
    }
    private MaterialLine? EditLine(MaterialLine? reference)
    {
        var contract=Input(reference?.Contract??"");var project=Input(reference?.Project??"");var company=Input(reference?.Company??"");var material=Input();var unit=Input("Bộ");var quantity=Input("1");var note=Input();
        var dialog=new Window{Owner=this,Title="Thêm vật tư bàn giao",Width=580,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#F3F6FA"),ResizeMode=ResizeMode.NoResize};
        var save=B("Thêm dòng",()=>
        {
            if(Rules.CleanContract(contract.Text).Length==0||material.Text.Trim().Length==0||unit.Text.Trim().Length==0){MessageBox.Show(dialog,"Nhập số hợp đồng, tên vật tư và đơn vị tính.");return;}
            if(!QuantityNumbers.TryParse(quantity.Text,out var n)){MessageBox.Show(dialog,"Số lượng không hợp lệ. Ví dụ: 1,5 hoặc 1.5.");return;}
            if(n<0){MessageBox.Show(dialog,"Số lượng không được âm.");return;}dialog.Tag=new MaterialLine{Contract=Rules.CleanContract(contract.Text),Project=project.Text,Company=company.Text,Material=material.Text,Unit=unit.Text,Quantity=n,Note=note.Text,Category=Rules.Classify(material.Text)};dialog.DialogResult=true;
        },true);
        dialog.Content=new Border{Padding=new Thickness(24),Child=Stack(Field("Số hợp đồng",contract),Field("Công trình",project),Field("Công ty",company),Field("Tên vật tư",material),Row(Field("Đơn vị tính",unit,240),Field("Số lượng",quantity,240)),Field("Ghi chú",note),Row(save,B("Đóng",()=>dialog.Close())))};
        return dialog.ShowDialog()==true?dialog.Tag as MaterialLine:null;
    }

    private void ShowSlips()
    {
        _page="slips";
        var query=Input(_slipQuery);query.Width=300;
        var status=new ComboBox{ItemsSource=new[]{new Choice("all","Tất cả phiếu"),new Choice("planned","Chờ xuất"),new Choice("delayed","Tạm hoãn"),new Choice("delivered","Đã xuất"),new Choice("cancelled","Đã hủy")},DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=_slipStatus,Width=190};
        var day=DateInput();var detail=T("Chọn một phiếu để in, sửa hoặc cập nhật trạng thái.",12,false,Muted);var count=T("",13,true);
        var grid=SlipGrid([]);
        Action load=()=>{_slipStatus=status.SelectedValue?.ToString()??"all";_slipQuery=query.Text;var key=Rules.Code(query.Text);var list=_service.Slips.Where(x=>(_slipStatus=="all"||x.Status==_slipStatus)&&(key.Length==0||Rules.Code(x.SlipCode+" "+x.ContractLabel+" "+x.Name+" "+string.Join(" ",x.Lines.Select(l=>l.Project))+" "+string.Join(" ",(x.MotorSerials??[]).Select(m=>m.SerialNumber))).Contains(key))&&(Day(day).Length==0||x.HandoverDate==Day(day))).OrderByDescending(x=>x.HandoverDate).ThenByDescending(x=>x.CreatedAt).ToList();grid.ItemsSource=list;count.Text=$"{list.Count:N0} phiếu · {list.Count(x=>x.Status=="planned")} chờ · {list.Count(x=>x.Status=="delayed")} hoãn · {list.Count(x=>x.Status=="delivered")} đã xuất · {list.Count(x=>x.Status=="cancelled")} hủy";};
        Slip? selected()=>grid.SelectedItem as Slip;
        async Task change(string target)
        {
            if(selected() is not Slip slip){Notify("Chọn một phiếu cần cập nhật.");return;}
            if(target=="delivered"&&!ConfirmSlipWarnings(slip))return;
            string reason="";
            if(target is "delayed" or "cancelled") {var answer=Prompt(target=="delayed"?"Lý do hoãn phiếu":"Lý do hủy phiếu","Nhập lý do để lưu vào lịch sử phiếu.",true);if(answer==null)return;reason=answer;if(reason.Trim().Length==0)throw new InvalidOperationException("Hãy ghi lý do hoãn hoặc hủy phiếu.");}
            if(target=="delivered"&&!Ask($"Xác nhận phiếu {slip.SlipCode} đã xuất hàng?\n\n{slip.ContractLabel}\n\nỨng dụng sẽ cập nhật Thành phẩm xuất kho.xlsx và ghi Ngày bàn giao cho đúng seri trong MÁY KÉO.xlsx."))return;
            if(target=="cancelled"&&slip.Status=="delivered"&&!Ask("Phiếu đã xuất sẽ được đánh dấu hủy và gỡ dòng do phiếu này ghi trong sổ xuất. Tiếp tục?"))return;
            if(target=="delivered")
            {
                var outgoing=Copy(slip);outgoing.Status="delivered";
                if(!ConfirmMotorSerialsForDelivery(outgoing))return;
                await _service.SaveSlipAsync(outgoing);
            }
            else await _service.ChangeSlipStatusAsync(slip.Id,target,reason);
            load();
        }
        grid.SelectionChanged+=(_,_)=>{if(selected() is Slip s)detail.Text=$"{s.SlipCode} · {s.ContractLabel} · {s.ContractPackages.Count} kiện lưu kèm · {s.LineCount} dòng · {s.Note}"+(s.DelayReason.Length>0?$"\nHoãn: {s.DelayReason}":"")+(s.CancelReason.Length>0?$"\nHủy: {s.CancelReason}":"");};
        grid.MouseDoubleClick+=(_,_)=>{if(selected() is Slip s)EditSlip(s);};
        var tools=Card(Stack(Row(Field("Tìm mã phiếu / số HĐ / công trình / serial",query),Field("Trạng thái",status),Field("Ngày kế hoạch / xuất",day),B("⌕  Lọc phiếu",load,true),B("Xóa lọc",()=>{_slipQuery="";_slipStatus="all";ShowSlips();})),count,Row(B("+ Tạo phiếu",()=>{_draft=null;_draftReport=null;Navigate("create");},true),B("Sửa phiếu",()=>{if(selected() is Slip s)EditSlip(s);else Notify("Chọn phiếu cần sửa.");}),B("Xem trước / in",()=>{if(selected() is Slip s)PrintSlipWithMotorSerials(s);else Notify("Chọn phiếu cần in.");}),A("Chờ xuất",()=>change("planned")),A("✓  Đã xuất",()=>change("delivered"),true),A("◷  Tạm hoãn",()=>change("delayed")),A("Hủy phiếu",()=>change("cancelled"),false,true),A("⌫  Xóa phiếu đã hủy",async()=>{if(selected() is not Slip s||s.Status!="cancelled")throw new InvalidOperationException("Chọn một phiếu đang ở trạng thái Đã hủy.");if(!Ask($"Xóa phiếu đã hủy {s.SlipCode} khỏi danh sách? Bản sao lưu JSON sẽ được giữ.") )return;var n=await _service.DeleteCancelledSlipsAsync([s.Id]);Notify($"Đã xóa {n} phiếu đã hủy.");load();},false,true),B("Lịch sử trạng thái",()=>{if(selected() is Slip s)ShowDialogGrid("Lịch sử phiếu "+s.SlipCode,Table(s.StatusHistory.Select(x=>new{At=DateTime.TryParse(x.At,out var at)?at.ToString("dd/MM/yyyy HH:mm"):x.At,State=Rules.StatusLabel(x.Status),x.Reason}),("Thời điểm","At",180),("Trạng thái","State",170),("Lý do","Reason",-1)));else Notify("Chọn phiếu cần xem.");}))));
        ((StackPanel)tools.Child).Children.Add(Row(B("Xem serial máy kéo",()=>{if(selected() is Slip s)ShowMotorSerialHistory(s);else Notify("Chọn phiếu cần xem serial.");})));
        var body=Layout(grid,new Border{Child=detail,Padding=new Thickness(12),Background=Brush("#EAF1F6"),Margin=new Thickness(0,8,0,0)});
        body.RowDefinitions[0].Height=new GridLength(1,GridUnitType.Star);body.RowDefinitions[1].Height=GridLength.Auto;
        SetPage("Phiếu bàn giao","Mỗi phiếu có mã riêng · chốt đã xuất, hoãn hoặc hủy từng phiếu",Layout(tools,body));load();
    }
    private static DataGrid SlipGrid(IEnumerable<Slip> slips)=>Table(slips,("Mã phiếu","SlipCode",235),("Ngày xuất","DateLabel",115),("Tên phiếu","Name",210),("Số hợp đồng","ContractLabel",-1),("Trạng thái","StatusLabel",150),("Dòng VT","LineCount",75));

    private void ShowDaily()
    {
        _page="daily";var day=DateInput(_dailyDay);var content=new ContentControl();
        Action load=()=>
        {
            if(Day(day).Length==0){Notify("Chọn ngày cần báo cáo.");return;}_dailyDay=Day(day);_daily=_service.BuildDaily(_dailyDay);
            var summary=Row();foreach(var state in new[]{"planned","delayed","delivered","cancelled"})summary.Children.Add(Card(Stack(T(Rules.StatusLabel(state),11,true,Muted),T((_daily.StatusCounts.GetValueOrDefault(state)).ToString("N0"),27,true)),new Thickness(20,12,35,12)));
            var cats=Table(_daily.Categories,("Nhóm vật tư trong phiếu","Category",175),("Số HĐ","ContractCount",100),("Công trình","ProjectCount",100),("Dòng VT","LineCount",100),("Tổng số lượng theo ĐVT","QuantityLabel",-1));
            var lines=_daily.Slips.SelectMany(s=>s.Lines.Where(l=>l.Include).Select(l=>new{ s.SlipCode,State=s.StatusLabel,l.Contract,l.Project,l.Material,l.Unit,l.Quantity,l.Note,Serial=MotorSerials.SerialText(s,l)})).ToList();
            var materialGrid=Table(lines,("Phiếu","SlipCode",220),("Trạng thái","State",135),("Số HĐ","Contract",110),("Công trình","Project",210),("Vật tư","Material",-1),("ĐVT","Unit",65),("Số lượng","Quantity",90),("Serial máy kéo","Serial",240));
            var tabs=Tabs(("Phiếu trong ngày",SlipGrid(_daily.Slips)),("Vật tư từng phiếu",materialGrid),("Tổng hợp nhóm vật tư",cats),("HĐ / cảnh báo",ReportBody(_daily.Contracts,false)),($"Hoãn còn chờ ({_daily.OutstandingDelayed.Count})",SlipGrid(_daily.OutstandingDelayed)),("Xuất theo sổ cũ",Table(_daily.LegacyDeliveries,("Ngày","Date",110),("Số HĐ","Contract",120),("Công trình","Project",-1),("Công ty","Company",170),("Nguồn","Source",180),("Ghi chú","Note",240))));
            content.Content=Layout(summary,tabs);
        };
        var tools=Card(Stack(Row(Field("Ngày kế hoạch / ngày xuất",day),B("▥  Xem báo cáo",load,true),B("In báo cáo ngày",()=>{if(_daily!=null)PrintService.PreviewDaily(this,_daily);}),A("Xuất Excel",async()=>{if(_daily==null)return;var path=SavePath(this,$"Bao cao xuat hang {_daily.Date}.xlsx");if(path!=null){await _service.ExportDailyAsync(path,_daily);Notify("Đã xuất báo cáo: "+path);}})),T("Tổng hợp phiếu theo ngày kế hoạch/ngày xuất, dùng trạng thái hiện tại. Phiếu hoãn chưa xuất được giữ riêng để theo dõi.",11,false,Muted)));
        SetPage("Báo cáo tổng hợp theo ngày","In báo cáo chi tiết cuối ngày · kiểm tra phiếu đã đi và phiếu hoãn",Layout(tools,content));load();
    }

    private void ShowArrival()
    {
        _page="arrival";
        var codes=Input("",true);
        codes.Width=510;codes.Height=84;
        var top=Card(Stack(
            T("NHẬP SỐ HỢP ĐỒNG → TÌM ĐÚNG KIỆN HƯNG YÊN",18,true),
            T("Nhập một hoặc nhiều HĐ (mỗi dòng một số). Khi bấm Tìm, HISA quét "+
              "file Excel trong Container theo từng thư mục ngày (vd 26.10), "+
              "chỉ lấy kiện ghi HƯNG YÊN / HUNG YEN đúng HĐ; giữ riêng công trình, "+
              "T1/T2, thang 1/2, hậu tố ngoặc và từng kiện. Không ghi trang Cont đi.",12,false,Muted),
            Field("Số hợp đồng cont về kho Hưng Yên",codes),
            T("Đúng hợp đồng chưa có nghĩa là đã nhận tất cả kiện. Tích từng kiện thực tế đã về, "+
              "chọn ngày về và xác nhận trước khi ghi vào Tổng hợp.xlsx. Không đối chiếu KT.",11,false,Muted)));
        var matching=BuildContDiSourcePanel(
            ()=>ContDiArrivalLookup.FromContracts(codes.Text));
        SetPage("Tìm kiện & nhập cont Hưng Yên",
            "Quét Container khi tìm → xác nhận kiện → chi tiết Tổng hợp.xlsx",
            new ScrollViewer{
                Content=Stack(top,matching),
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto
            });
    }

    private void ShowArrivalPackages()
    {
        _page="arrival-packages";
        var contract=Input();contract.Width=220;
        var day=DateInput();
        var count=T("Lọc theo ngày cont về và số hợp đồng.",12,false,Muted);
        var detailTitle=T("Chọn một kiện để xem vật tư bên trong.",13,true);
        var packages=PackageGrid([]);
        var materials=MaterialsTable([]);
        void LoadRows()
        {
            var rows=_service.GetArrivalPackages(contract.Text,Day(day));
            packages.ItemsSource=rows;
            count.Text=$"{rows.Count:N0} kiện · {rows.Sum(x=>x.Items.Count):N0} dòng vật tư"+(Day(day).Length>0?$" · ngày về {Rules.Day(Day(day))}":" · tất cả ngày");
            packages.SelectedItem=rows.FirstOrDefault();
            ShowSelected();
        }
        void ShowSelected()
        {
            if(packages.SelectedItem is not Package p)
            {
                detailTitle.Text="Chọn một kiện để xem vật tư bên trong.";materials.ItemsSource=Array.Empty<MaterialLine>();return;
            }
            detailTitle.Text=$"HĐ {p.Contract} · Kiện {p.PackageNo} · {p.Project} · ngày về {p.ArrivalLabel} · {p.Items.Count:N0} dòng";
            materials.ItemsSource=p.Items;
        }
        packages.SelectionChanged+=(_,_)=>ShowSelected();
        packages.MouseDoubleClick+=(_,_)=>ShowSelected();
        var filters=Card(Stack(Row(Field("Ngày cont về · để trống xem tất cả",day),Field("Số hợp đồng",contract),B("⌕  Lọc kiện",LoadRows,true),B("Tải lại",()=>{_ = Run(async()=>{await _service.RefreshAsync(false);LoadRows();});})),count));
        var delete=A("⌫  Xóa kiện khỏi Tổng hợp.xlsx",async()=>
        {
            if(packages.SelectedItem is not Package selected)throw new InvalidOperationException("Chọn kiện cần xóa.");
            if(!Ask($"Xóa kiện {selected.PackageNo} · HĐ {selected.Contract} · {selected.Project}, ngày về {selected.ArrivalLabel}, gồm {selected.Items.Count} dòng vật tư khỏi trang chi tiết của Tổng hợp.xlsx?\n\nỨng dụng sẽ tạo bản sao lưu trước khi ghi. Thao tác này không xóa kiện khỏi KT tổng hợp.xlsx."))return;
            var removed=await _service.DeleteArrivalPackageAsync(selected);
            Notify($"Đã xóa kiện khỏi Tổng hợp.xlsx ({removed} dòng vật tư). Bản sao lưu nằm trong thư mục HISA backups.");
            LoadRows();
        },false,true);
        var tools=Card(Stack(Row(delete,B("Lọc lại",LoadRows)),detailTitle));
        var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1.15,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(0.85,GridUnitType.Star)});
        body.Children.Add(packages);var detailScroll=new ScrollViewer{Content=materials,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(detailScroll,1);body.Children.Add(detailScroll);
        SetPage("Quản lý cont về Hưng Yên","Lọc từng kiện theo ngày về hoặc số hợp đồng · xem vật tư · xóa kiện khỏi Tổng hợp.xlsx",Layout(filters,Layout(tools,body)));
        LoadRows();
    }

    private void ShowFlags()
    {
        _page="flags";var codes=Input("",true);codes.Width=340;var project=Input();project.Width=270;var note=Input();note.Width=360;
        var status=new ComboBox{Width=190,ItemsSource=new[]{new Choice("delayed","Hoãn lấy hàng"),new Choice("cancel_pickup","Hủy lấy hàng")},DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue="delayed"};
        var grid=Table(_service.Flags,("Số HĐ","Contract",130),("Công trình","Project",-1),("Trạng thái","StatusLabel",170),("Lý do / ghi chú","Note",-1),("Cập nhật","UpdatedAt",190));
        var top=Card(Stack(Row(Field("SỐ HỢP ĐỒNG · CÓ THỂ NHẬP NHIỀU MÃ",codes),Field("Công trình · để trống áp dụng cả số HĐ",project),Field("Trạng thái",status)),Field("Lý do / ghi chú",note),Row(A("Lưu trạng thái hợp đồng",async()=>{if(Rules.Tokens(codes.Text).Count==0)throw new InvalidOperationException("Nhập số hợp đồng.");if(note.Text.Trim().Length==0)throw new InvalidOperationException("Nhập lý do hoãn hoặc hủy lấy hàng.");await _service.SaveFlagsAsync(codes.Text,project.Text,status.SelectedValue?.ToString()??"delayed",note.Text);ShowFlags();},true),B("Nạp dòng để sửa",()=>{if(grid.SelectedItem is ContractFlag f){if(f.SlipId.Length>0){Notify("Cảnh báo này được tạo từ một phiếu hoãn. Mở Phiếu bàn giao để sửa hoặc khôi phục phiếu đó.");return;}codes.Text=f.Contract;project.Text=f.Project;note.Text=f.Note;status.SelectedValue=f.Status;}else Notify("Chọn một trạng thái cần sửa.");}),A("Xóa trạng thái đã chọn",async()=>{if(grid.SelectedItem is not ContractFlag f)throw new InvalidOperationException("Chọn dòng cần xóa.");if(f.SlipId.Length>0)throw new InvalidOperationException("Cảnh báo này liên kết với phiếu hoãn. Hãy cập nhật phiếu để giữ đúng lịch sử.");if(Ask($"Xóa cảnh báo {f.Contract} · {f.StatusLabel}?")){await _service.RemoveFlagAsync(f.Id);ShowFlags();}},false,true)),T("Cảnh báo hoãn/hủy sẽ hiện khi tra cứu, lập kế hoạch hoặc tạo phiếu. Cùng số HĐ nhưng khác công trình được đối chiếu riêng.",11,false,Muted)));
        SetPage("Hoãn / hủy lấy hàng","Lưu lý do và cảnh báo chủ động khi lên phiếu",Layout(top,grid));
    }
    private void ShowTemplates()
    {
        _page="templates";var grid=Table(_service.Templates,("Tên mẫu","Name",-1),("Kiểu bố cục","Layout",180),("Tiêu đề trên phiếu","Title",-1),("Khổ giấy","PaperSize",100),("Chiều giấy","Orientation",110),("Tiền tố mã","CodePrefix",160));
        async Task edit(bool create)
        {
            PrintTemplate model;if(create){model=Copy(_service.Templates.FirstOrDefault()??new PrintTemplate());model.Id=Guid.NewGuid().ToString();model.Name="Mẫu phiếu mới";}else if(grid.SelectedItem is PrintTemplate t)model=Copy(t);else{Notify("Chọn một mẫu cần sửa.");return;}
            var editor=new TemplateEditorWindow(model){Owner=this};if(editor.ShowDialog()==true){await _service.SaveTemplateAsync(editor.ResultTemplate);ShowTemplates();}
        }
        grid.MouseDoubleClick+=(_,_)=>_ = Run(()=>edit(false));
        var top=Card(Stack(Row(A("+ Tạo mẫu phiếu mới",()=>CreatePrintTemplate(),true),A("Sửa mẫu / căn lề",()=>edit(false)),A("Sao chép mẫu",()=>edit(true)),B("In thử mẫu",()=>
        {
            if(grid.SelectedItem is not PrintTemplate t){Notify("Chọn mẫu cần in thử.");return;}
            var report=_service.Search(new SearchFilter());var first=report.Rows.FirstOrDefault();if(first==null){Notify("Chưa có dữ liệu để in thử. Bạn vẫn có thể chỉnh mẫu.");return;}
            var sample=Copy(report);sample.Rows=[first];var slip=_service.CreateDraft(sample,t.Id,DateTime.Today.ToString("yyyy-MM-dd"));slip.SlipCode="XEM-TRUOC";PrintSlipWithMotorSerials(slip);
        })),T("Biên bản tổng hợp nội bộ và phiếu riêng theo hợp đồng. Chỉnh tiêu đề, nội dung, tên cột, chữ ký, font, lề in và mã phiếu.",12,false,Muted)));
        SetPage("Mẫu phiếu in","Mẫu được lưu riêng · có thể sao chép và chỉnh sửa nội dung",Layout(top,grid));
    }
    private void ShowSettings()
    {
        _page="settings";var model=Copy(_service.Settings);
        // Fresh Program Files installs can have no DataSources entry; show an existing
        // canonical Excel folder as a suggestion without altering saved settings.
        var initialSource=HisaFolderLayout.PreferredSelection(model);
        if(string.IsNullOrWhiteSpace(initialSource)&&System.IO.File.Exists(model.MasterPath))
            initialSource=System.IO.Path.GetDirectoryName(model.MasterPath);
        var sources=Input(initialSource??"");sources.Height=38;
        var discovery=T("",11,false,Muted);
        var auto=new CheckBox{Content="Tự động phát hiện dữ liệu mới",IsChecked=model.AutoRefresh};
        var cont=T("Container chỉ được quét khi bạn chủ động tìm hợp đồng / xử lý ảnh Zalo. Không tự động nhập hoặc di chuyển file.",12,false,Muted);
        var poll=Input(model.PollSeconds.ToString());poll.Width=90;
        void updateDiscovery()
        {
            try
            {
                var layout=HisaFolderLayout.RequireCompleteRoot(sources.Text.Trim(),model);
                discovery.Foreground=Brush("#177E89");
                discovery.Text="✓ ĐÃ NHẬN DIỆN NGUỒN EXCEL (CHỈ CẦN MỘT THƯ MỤC)"+
                    "\n✓ Kho chính · "+System.IO.Path.GetFileName(layout.Master)+
                    "\n✓ Kho chính · "+System.IO.Path.GetFileName(layout.Product)+
                    "\n✓ Danh sách sản xuất · "+System.IO.Path.GetFileName(layout.Motor)+
                    "\n✓ Danh sách sản xuất · "+System.IO.Path.GetFileName(layout.Cabinet)+
                    "\n✓ Danh sách sản xuất · "+System.IO.Path.GetFileName(layout.Door)+
                    "\n✓ Container · chỉ quét khi tìm kiếm tại "+layout.Cont;
            }
            catch(Exception e)
            {
                discovery.Foreground=Brush("#B05A32");
                discovery.Text="⚠ "+e.Message;
            }
        }
        sources.TextChanged+=(_,_)=>updateDiscovery();
        updateDiscovery();

        var p=Stack();
        p.Children.Add(Card(Stack(T("TRUNG TÂM CẤU HÌNH",20,true),
            T("Gemini AI · Nguồn Excel · Đồng bộ · Bảo vệ dữ liệu",12,false,Muted))));
        p.Children.Add(Card(Stack(T("Nguồn dữ liệu Excel",18,true),
            T("Thư mục đang chọn: "+(string.IsNullOrWhiteSpace(initialSource)?"Chưa cấu hình":initialSource),12,false,Muted),
            T("CHỈ CHỌN 1 LẦN thư mục gốc Kho HISA. HISA tự tìm Excel kho chính, danh sách sản xuất và thư mục Container; không quét Container khi khởi động.",12,false,Muted),
            DockWithButton(sources,B("Chọn Kho HISA",()=>{var dialog=new OpenFolderDialog{Title="Chọn duy nhất thư mục gốc Kho HISA"};if(dialog.ShowDialog(this)==true)sources.Text=dialog.FolderName;})),discovery)));
        p.Children.Add(Card(Stack(T("DANH SÁCH SẢN XUẤT · TỰ NHẬN DIỆN",18,true),
            T("MÁY KÉO.xlsx · TỦ ĐIỆN.xlsx · Đầu cửa car T10.2026.xlsx được tự động đọc từ thư mục Danh sách sản xuất.",12,false,Muted),
            T("Bạn không phải chọn từng file hoặc cấu hình thêm đường dẫn. Các bảng sản xuất chỉ đọc, không tự xác nhận hàng đã nhập kho.",11,false,Muted))));
        p.Children.Add(Card(Stack(T("Tốc độ & đồng bộ",18,true),auto,cont,Row(Field("Chu kỳ kiểm tra · giây",poll)),T("Dữ liệu được lập chỉ mục trong bộ nhớ và giữ bộ nhớ đệm để mở ứng dụng nhanh. File Excel đang mở sẽ được báo và thử lại, không ghi dở dang.",12,false,Muted))));
        p.Children.Add(Row(A("Lưu cấu hình & tải lại",async()=>
        {
            if(!int.TryParse(poll.Text,out var seconds)||seconds<5||seconds>3600)throw new InvalidOperationException("Chu kỳ kiểm tra từ 5 đến 3600 giây.");
            // A single choice resolves ALL Excel paths; never reuse stale
            // manually-selected equipment files from a previous warehouse.
            var layout=HisaFolderLayout.RequireCompleteRoot(sources.Text.Trim(),model);
            model=_service.ResolveSettingsFromSourceFolder(layout.Root);
            HisaFolderLayout.Apply(model,layout);
            model.AutoRefresh=auto.IsChecked==true;model.AutoSyncCont=false;
            model.AutoSyncKt=false;model.PollSeconds=seconds;
            await _service.SaveSettingsAsync(model);await _service.RefreshAsync(true);Notify("Đã lưu cấu hình nguồn.");ShowSettings();
        },true),A("Sao lưu vào Thư viện",async()=>{var path=System.IO.Path.Combine(_service.BackupDirectory,$"HISA-sao-luu-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");await _service.BackupAsync(path);Notify("Đã sao lưu vào Thư viện → Tài liệu:\n"+path);}),A("Phục hồi từ ZIP",RestoreFromZipAsync),B("Mở thư mục Backup",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_service.BackupDirectory){UseShellExecute=true})),B("Mở thư mục dữ liệu",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_service.DataDirectory){UseShellExecute=true}))));
        p.Children.Add(GeminiSettingsCard());
        p.Children.Add(Card(Stack(
            T("CẬP NHẬT PHẦN MỀM",18,true),
            T("Nhấn Kiểm tra và tải nền: HISA tự tìm bản mới trên GitHub, tải về và kiểm tra SHA-256. Khi sẵn sàng, ứng dụng hỏi bạn có muốn đóng HISA để cài đặt hay không. Windows UAC vẫn có thể yêu cầu quyền quản trị.",12,false,Muted),
            T(AutoUpdater.ReadyVersion is string staged?
                "Bộ cài phiên bản "+staged+" đã tải xong và xác minh.":"Đang dùng HISA "+SoftwareInfo.Version,12,true),
            Row(B("↑ Kiểm tra, tải nền và cập nhật",()=>_ = CheckDownloadAndOfferUpdateAsync(),true),
                B("Lịch sử & nhật ký cập nhật",()=>Navigate("information")))
        )));
        p.Children.Add(Alert($"Dữ liệu ứng dụng: {_service.DataDirectory}\nSao lưu (ZIP và Excel): {_service.BackupDirectory}\nCác bản sao lưu cũ tại nguồn sẽ được giữ nguyên."));
        SetPage("Cấu hình & sao lưu","Gemini AI · lưu / xóa API Key · đồng bộ dữ liệu Excel · sao lưu",new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    private async Task RestoreFromZipAsync()
    {
        var picker=new Microsoft.Win32.OpenFileDialog
        {
            Title="Chọn bản sao lưu HISA cần phục hồi",
            Filter="Bản sao lưu HISA (*.zip)|*.zip",
            CheckFileExists=true,Multiselect=false,
            InitialDirectory=_service.BackupDirectory
        };
        if(picker.ShowDialog(this)!=true)return;
        var info=await Task.Run(()=>BackupRestore.Inspect(picker.FileName));
        var appData=new CheckBox{Content=$"Phục hồi dữ liệu ứng dụng ({info.DataCount} JSON: phiếu, lịch AI, cấu hình...)",
            IsChecked=true,Margin=new Thickness(0,6,0,10)};
        var excel=new CheckBox{Content=$"Phục hồi file Excel chuẩn ({info.ExcelCount} workbook)",
            IsChecked=false,Margin=new Thickness(0,6,0,10)};
        var warning=info.Legacy?
            "ZIP v1 cũ KHÔNG có SHA-256 và KHÔNG chứa lịch AI đã duyệt. Chỉ các file hiện có được khôi phục.":
            "ZIP v2 có manifest SHA-256; kiểm tra toàn vẹn và cấu trúc trước khi thay đổi dữ liệu.";
        var confirm=new Window
        {
            Title="HISA · Xem trước và chọn phục hồi",Owner=this,Width=740,Height=370,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Background=Brush("#F3F6FA")
        };
        confirm.Content=new Border
        {
            Padding=new Thickness(22),Child=Stack(
                T("PHỤC HỒI DỮ LIỆU TỪ ZIP",19,true),
                T("Nguồn: "+System.IO.Path.GetFileName(picker.FileName),12,false,Muted),
                T("Ngày tạo/ghi file: "+info.CreatedAt+" · "+warning,12,false,Muted),
                appData,excel,
                Alert("HISA sẽ tự tạo bản sao an toàn trước khi ghi; nếu gặp lỗi sẽ hoàn tác. "+
                    "Nên đóng các file Excel đang mở. Phục hồi cấu hình từ máy khác có thể thay đổi đường dẫn nguồn."),
                Row(B("Phục hồi nhóm đã chọn",()=>confirm.DialogResult=true,true),
                    B("Hủy",()=>confirm.DialogResult=false))
            )
        };
        if(confirm.ShowDialog()!=true)return;
        if(appData.IsChecked!=true && excel.IsChecked!=true)
            throw new InvalidOperationException("Chọn dữ liệu ứng dụng và/hoặc Excel trước khi phục hồi.");
        var safety=await _service.RestoreBackupAsync(picker.FileName,appData.IsChecked==true,excel.IsChecked==true);
        MessageBox.Show(this,
            "Phục hồi thành công. Bản sao an toàn trước phục hồi nằm tại:\n"+safety+
            "\n\nHISA sẽ đóng để nạp lại dữ liệu khi mở lần tới.",
            "HISA · Phục hồi hoàn tất",MessageBoxButton.OK,MessageBoxImage.Information);
        System.Windows.Application.Current.Shutdown();
    }

    private static FrameworkElement DockWithButton(FrameworkElement input,Button button)
    {var p=new DockPanel();DockPanel.SetDock(button,Dock.Right);p.Children.Add(button);p.Children.Add(input);return p;}
    private string? Prompt(string title,string message,bool multiline=false)
    {
        var input=Input("",multiline);if(multiline)input.Height=130;
        var dialog=new Window{Title=title,Owner=this,Width=560,Height=multiline?310:220,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#F3F6FA")};
        dialog.Content=new Border{Padding=new Thickness(24),Child=Stack(T(message,13,false,Muted),input,Row(B("Xác nhận",()=>dialog.DialogResult=true,true),B("Quay lại",()=>dialog.DialogResult=false)))};dialog.Loaded+=(_,_)=>input.Focus();
        return dialog.ShowDialog()==true?input.Text:null;
    }
    private void ShowDialogGrid(string title,DataGrid grid)=>ShowDialogContent(title,grid);
    private void ShowDialogContent(string title,FrameworkElement body)
    {
        var dialog=new Window{Owner=this,Title=title,Width=Math.Min(1250,Math.Max(900,ActualWidth-80)),Height=Math.Min(790,Math.Max(600,ActualHeight-80)),MinWidth=800,MinHeight=500,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#F3F6FA")};
        var resources=new ResourceDictionary{Source=new Uri("Theme.xaml",UriKind.Relative)};dialog.Resources.MergedDictionaries.Add(resources);
        dialog.Content=new Border{Padding=new Thickness(20),Child=body};dialog.ShowDialog();
    }
    private record Choice(string Value,string Label);
}
