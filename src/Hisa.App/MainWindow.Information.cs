using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowInformation()
    {
        _page="information";
        var identity=Stack(T(SoftwareInfo.DisplayName,32,true),T("Quản lý kiện hàng, hợp đồng và bàn giao vật tư",14,false,Muted));
        identity.Children.Add(new Border
        {
            Background=Brush("#E8F5F4"),CornerRadius=new CornerRadius(5),Padding=new Thickness(11,6,11,6),
            HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,15,0,0),
            Child=T("PHIÊN BẢN "+SoftwareInfo.Version,11,true,Brush("#117D83"))
        });
        var brand=new DockPanel();var logo=new Image{Source=BrandingAssets.Logo,Width=105,Height=105,Margin=new Thickness(0,0,25,0),VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(logo,Dock.Left);brand.Children.Add(logo);brand.Children.Add(identity);
        var heading=Card(brand,new Thickness(26));
        var details=Stack(T("Thông tin phần mềm",18,true));
        details.Children.Add(InformationField("Tên phần mềm",SoftwareInfo.DisplayName));
        details.Children.Add(InformationField("Người tạo",SoftwareInfo.Creator));
        details.Children.Add(InformationField("Số điện thoại",SoftwareInfo.Phone,true));
        details.Children.Add(InformationField("Email",SoftwareInfo.Email,true));
        var contacts=Card(details,new Thickness(26));
        var capabilities=Card(Stack
        (
            T("Công cụ quản lý kho hằng ngày",18,true),
            T("Tra cứu theo hợp đồng và kiện Hưng Yên; theo dõi cont về; đối chiếu dữ liệu Excel; lập kế hoạch, tạo và sửa phiếu bàn giao; quản lý các trạng thái xuất hàng; thiết kế mẫu in và tổng hợp báo cáo theo ngày.",13,false,Muted)
        ),new Thickness(26));
        var updating=Card(Stack(
            T("Cập nhật phần mềm",18,true),
            T("Nhấn kiểm tra để HISA tự tìm bản chính thức mới, tải xuống trong nền và xác minh SHA-256. Sau khi tải xong HISA mới hỏi đóng ứng dụng để cài bản mới; chọn Không để làm việc tiếp. Không thay đổi Excel, phiếu hoặc API Key.",12,false,Muted),
            T(AutoUpdater.ReadyVersion is string ready?"ĐÃ SẴN SÀNG CÀI PHIÊN BẢN "+ready:"PHIÊN BẢN ĐANG SỬ DỤNG: "+SoftwareInfo.Version,13,true,Brush("#117D83")),
            Row(
                B("Kiểm tra, tải nền và cập nhật",()=>_ = CheckDownloadAndOfferUpdateAsync(),true),
                B("Cài bản đã tải",()=>
                {
                    var version=AutoUpdater.ReadyVersion;
                    if(version==null){Notify("Chưa có bản cập nhật được tải và xác minh. Chọn Kiểm tra cập nhật trước.");return;}
                    if(!Ask("Cài HISA "+version+" và đóng phần mềm hiện tại?\nWindows có thể yêu cầu quyền quản trị. Dữ liệu đã lưu được giữ nguyên."))return;
                    if(!AutoUpdater.RequestInstallOnExit()){Notify("Tệp cập nhật không còn tồn tại. Vui lòng kiểm tra cập nhật lại.");return;}
                    Close();
                }),
                B("Tải bộ cài thủ công",()=>
                {
                    try { Process.Start(new ProcessStartInfo("https://github.com/infiniti-money/Xuat-hang/releases/latest"){UseShellExecute=true}); }
                    catch(Exception ex){Notify("Không thể mở trình duyệt: "+ex.Message);}
                })
            ),
            T(AutoUpdater.LastFailure is string error?"Cập nhật gần nhất gặp lỗi: "+error:"Tải lỗi hoặc mạng mất kết nối: chọn Kiểm tra cập nhật để thử lại phần tải dở.",12,false,Muted),
            T("Log cài đặt (nếu Inno Setup đã chạy): "+AutoUpdater.InstallLogDirectory+"\\installer-<phiên bản>.log",11,false,Muted),
            B("Mở thư mục log cập nhật",()=>{System.IO.Directory.CreateDirectory(AutoUpdater.InstallLogDirectory);Process.Start(new ProcessStartInfo(AutoUpdater.InstallLogDirectory){UseShellExecute=true});}),
            T("Nếu bạn đang dùng v1.8.1 không có nút cập nhật, hãy tải bản Setup mới từ GitHub và chạy để cài đè. Khi cập nhật, đóng HISA và Excel đang mở. Windows có thể hỏi quyền quản trị (UAC).",11,false,Muted)
        ),new Thickness(26));
        var panel=Stack(heading,contacts,updating,capabilities);
        panel.MaxWidth=920;panel.HorizontalAlignment=HorizontalAlignment.Left;
        SetPage("Thông tin phần mềm",SoftwareInfo.DisplayName+" · Phiên bản "+SoftwareInfo.Version,new ScrollViewer
        {
            Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled
        });
    }

    private FrameworkElement InformationField(string label,string value,bool allowCopy=false)
    {
        var row=new Grid{Margin=new Thickness(0,15,0,0)};
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(165)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var caption=T(label,12,true,Muted);caption.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(caption);
        var content=new TextBox
        {
            Text=value,IsReadOnly=true,BorderThickness=new Thickness(0),Background=Brushes.Transparent,
            Foreground=Ink,FontSize=15,Padding=new Thickness(0,7,16,7),VerticalAlignment=VerticalAlignment.Center,
            MinWidth=300,TextWrapping=TextWrapping.Wrap
        };
        Grid.SetColumn(content,1);row.Children.Add(content);
        if(allowCopy)
        {
            var copy=B("Sao chép",()=>
            {
                try{Clipboard.SetText(value);StatusText.Text="Đã sao chép "+label.ToLowerInvariant()+".";}
                catch(Exception ex){Notify("Chưa sao chép được: "+ex.Message);}
            });
            copy.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(copy,2);row.Children.Add(copy);
        }
        return row;
    }

    private async Task CreatePrintTemplate(string layout="internal_summary",bool returnToList=true)
    {
        var model=CreateNewPrintTemplateModel(layout);
        var editor=new TemplateEditorWindow(model){Owner=this};
        if(editor.ShowDialog()==true)
        {
            await _service.SaveTemplateAsync(editor.ResultTemplate);
            if(returnToList)ShowTemplates();StatusText.Text="Đã tạo mẫu phiếu in: "+editor.ResultTemplate.Name;
        }
    }

    internal static PrintTemplate CreateNewPrintTemplateModel(string layout="internal_summary")
    {
        if(!new[]{"internal_summary","contract_delivery","standard"}.Contains(layout))layout="internal_summary";
        return new PrintTemplate
        {
            Id=Guid.NewGuid().ToString("N"),Layout=layout,
            Name=layout=="contract_delivery"?"Mẫu bàn giao theo hợp đồng mới":layout=="standard"?"Mẫu phiếu bàn giao mới":"Mẫu biên bản nội bộ mới",
            Title=layout=="contract_delivery"?"BIÊN BẢN BÀN GIAO VẬT TƯ":layout=="standard"?"PHIẾU BÀN GIAO VẬT TƯ":"BIÊN BẢN BÀN GIAO NỘI BỘ",
            MinRows=10
        };
    }
}
