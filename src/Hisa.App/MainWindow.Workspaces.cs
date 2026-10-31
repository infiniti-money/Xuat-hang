using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

/// <summary>
/// Workflow-oriented entry points. Existing warehouse screens remain unchanged.
/// No business data is modified by visiting these pages.
/// </summary>
public partial class MainWindow
{
    private static string TodayDay() => DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);

    internal static string PreferredDispatchDay(DispatchReadinessState state)
    {
        var today=TodayDay();
        var tomorrow=DateTime.Today.AddDays(1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        return state.Plans.Any(p=>p.Day==today) && !state.Plans.Any(p=>p.Day==tomorrow)
            ? today : tomorrow;
    }

    private Button WorkflowTile(string number,string title,string description,
        string detail,Action action,bool primary=false)
    {
        var inner=Stack(
            T(number+"   "+title,16,true,primary?Brush("#F9FEFF"):Brush("#18354D")),
            T(description,12,false,primary?Brush("#E8FFFB"):Muted),
            T(detail,11,true,primary?Brush("#D5EFE9"):Brush("#13808A")));
        foreach(FrameworkElement el in inner.Children)el.Margin=new Thickness(0,0,0,8);
        var button=B("",action,primary);
        button.Content=inner;
        button.Width=305;
        button.MinHeight=136;
        button.Margin=new Thickness(0,0,13,13);
        button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        button.VerticalContentAlignment=VerticalAlignment.Center;
        button.Padding=new Thickness(18,14,18,14);
        return button;
    }

    private FrameworkElement HubSection(string title,string description,params Button[] steps) =>
        StyledDashboardPanel(Stack(
            T(title,17,true),
            T(description,12,false,Muted),
            Row(steps)));

    private static FrameworkElement HubHeader(string title,string subtitle) =>
        new Border
        {
            Background=Brush("#E8F6F5"),
            BorderBrush=Brush("#C7E9E4"),
            BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(13),
            Padding=new Thickness(22,18,22,18),
            Margin=new Thickness(0,0,0,16),
            Child=Stack(
                T(title,21,true,Brush("#14566B")),
                T(subtitle,12,false,Brush("#426C7A")))
        };

    private void ShowDispatchHub()
    {
        _page="dispatch-hub";
        var page=Stack();
        page.Children.Add(HubHeader("ĐIỀU HÀNH XUẤT HÀNG · MỘT QUY TRÌNH",
            "Lịch dự kiến → chuẩn bị đúng kiện → chọn hợp đồng → lập phiếu chờ → xác nhận bàn giao"));
        try
        {
            var store=new DispatchReadinessStore(_service.DataDirectory);
            var state=store.Load();
            var day=PreferredDispatchDay(state);
            var statuses=DispatchReadinessStore.Evaluate(state,_service.Snapshot,day);
            var urgent=statuses.Count(x=>x.Plan.Urgent);
            var ready=statuses.Count(x=>x.Status.StartsWith("Sẵn sàng",StringComparison.Ordinal));
            page.Children.Add(StyledDashboardPanel(Stack(
                T("LỊCH XUẤT ĐƯỢC GHI NHẬN  /  "+Rules.Day(day),14,true),
                T(statuses.Count==0
                    ? "Chưa có lịch xuất dự kiến ngày này. Anh hãy nhập ảnh Zalo hoặc thêm hợp đồng phát sinh bằng tay."
                    : statuses.Count+" hợp đồng · "+urgent+" cần ưu tiên · "+ready+
                      " sẵn sàng chờ bàn giao · "+(statuses.Count-ready)+" cần đối chiếu",
                    13,statuses.Count==0, statuses.Count==0?Brush("#A26731"):Muted),
                Row(B("Xem lịch và vật tư",()=>Navigate("readiness"),true),
                    B("Đọc ảnh lịch Zalo",()=>Navigate("ai-schedule"))))));
        }
        catch(Exception ex)
        {
            page.Children.Add(Alert("Chưa đọc được trạng thái lịch xuất: "+ex.Message+
                ". Dữ liệu kho không bị thay đổi.",true));
        }
        page.Children.Add(HubSection("CÁC BƯỚC ĐƯỢC GỘP THEO QUY TRÌNH",
            "Thay vì tìm nhiều mục rời rạc, mở đúng bước cần làm; dữ liệu và phiếu cũ được giữ.",
            WorkflowTile("01","Lịch xuất & vật tư",
                "Nạp lịch Gemini đã duyệt hoặc thêm hợp đồng phát sinh.",
                "Đủ / Thiếu / Chưa cập nhật",()=>Navigate("readiness"),true),
            WorkflowTile("02","Lập phiếu chờ xuất",
                "Chọn hợp đồng, kiểm tra quy cách và serial máy kéo.",
                "Không tự ghi Đã xuất",()=>{_draft=null;_draftReport=null;_createPlanMode=false;Navigate("create");}),
            WorkflowTile("03","Theo dõi phiếu bàn giao",
                "Xem phiếu chờ, hoãn, đã xuất và lịch sử.",
                "Chỉ xác nhận sau khi bàn giao",()=>Navigate("slips"))));
        page.Children.Add(HubSection("TRA CỨU & ĐỐI SOÁT",
            "Các chức năng hỗ trợ dùng lại màn hình hiện có, không tạo bản dữ liệu thứ hai.",
            WorkflowTile("⌕","Tra cứu hợp đồng",
                "Phân biệt cùng số HĐ theo công trình, T1/T2 và kiện.",
                "Từ dữ liệu Tổng hợp.xlsx",()=>Navigate("lookup")),
            WorkflowTile("▥","Báo cáo ngày",
                "Tình trạng xuất theo ngày và các phiếu liên quan.",
                "Dữ liệu đã xác nhận",()=>Navigate("daily"))));
        SetPage("Điều hành xuất hàng",
            "Lịch dự kiến → kiểm tra vật tư → phiếu chờ → bàn giao",
            new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }

    private void ShowArrivalHub()
    {
        _page="arrival-hub";
        var page=Stack();
        page.Children.Add(HubHeader("CONT & KIỆN HƯNG YÊN",
            "Dự kiến về kho không phải đã nhận hàng · chỉ ghi Tổng hợp.xlsx sau khi xác nhận thực tế"));
        try
        {
            var approved=PlannedArrivalWorkflow.LoadLatestApproved(_service.DataDirectory);
            var incoming=approved?.Arrivals.Count??0;
            var received=_service.Snapshot.DetailPackages.Count;
            var cont=_service.Snapshot.ContPackages.Count;
            page.Children.Add(StyledDashboardPanel(Stack(
                T("THÔNG TIN NGUỒN ĐANG TẢI",14,true),
                T(incoming+" dòng công trình dự kiến về theo lịch Gemini đã duyệt gần nhất · "+
                    cont+" kiện trong nguồn cont đi · "+received+" kiện ghi trong chi tiết Tổng hợp.xlsx",
                    12,false,Muted),
                T("Những số liệu trên thuộc các nguồn khác nhau; không được cộng thành số hàng đã nhận.",11,false,Brush("#9E6834")))));
        }
        catch(Exception ex)
        {
            page.Children.Add(Alert("Không đọc được lịch cont đã duyệt: "+ex.Message+
                ". Vẫn có thể mở nhập kho thủ công.",true));
        }
        page.Children.Add(HubSection("QUY TRÌNH CONT ĐI → VỀ KHO",
            "Một nơi cho lịch AI, xác nhận thực tế, nhập tay và kiểm tra kiện. Không nhập kho tự động.",
            WorkflowTile("01","Lịch cont dự kiến",
                "Đọc ảnh Zalo, xem ngày cont rời nhà máy và dự kiến về.",
                "Gemini · cần duyệt",()=>Navigate("ai-schedule"),true),
            WorkflowTile("02","Xác nhận cont đã về",
                "Tích riêng từng kiện đã nhận, đối chiếu đúng nguồn rồi ghi Excel.",
                "Xác nhận trước khi ghi",()=>Navigate("planned-arrivals")),
            WorkflowTile("03","Nhập cont thủ công",
                "Đối chiếu kiện nguồn nếu không có ảnh lịch hoặc cần bổ sung.",
                "Không gộp kiện khác ngày",()=>Navigate("arrival"))));
        page.Children.Add(HubSection("QUẢN LÝ KIỆN",
            "Số HĐ trùng không có nghĩa kiện trùng: phải kiểm tra ngày cont, số lượng, T1/T2 và quy cách.",
            WorkflowTile("▣","Kiện Hưng Yên",
                "Xem các kiện chuẩn bị, số lượng và tên vật tư.",
                "Kiện nguồn",()=>Navigate("packages")),
            WorkflowTile("▤","Kiện cont về",
                "Đối soát kiện thực nhận, chỉnh sửa khi có xác nhận.",
                "Tổng hợp.xlsx",()=>Navigate("arrival-packages")),
            WorkflowTile("⌕","Rà soát kiện cùng HĐ",
                "Chỉ đối chiếu dữ liệu bị nhập sai; trùng số HĐ chưa phải trùng hàng.",
                "Công cụ nâng cao",()=>Navigate("duplicate-review"))));
        SetPage("Cont & kiện Hưng Yên",
            "Lịch cont → xác nhận nhận thực tế → đối soát kiện nguồn",
            new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }

    private void ShowAiHub()
    {
        _page="ai-hub";
        var page=Stack();
        page.Children.Add(HubHeader("TRUNG TÂM GEMINI AI",
            "Một API Key dùng cho trò chuyện và đọc lịch Zalo · AI chỉ tư vấn, không tự ghi kho"));
        bool configured;
        try{configured=new GeminiCredentialStore().HasKey();}
        catch{configured=false;}
        page.Children.Add(StyledDashboardPanel(Stack(
            T(configured?"● GEMINI ĐÃ CẤU HÌNH":"○ CHƯA CÓ GEMINI API KEY",
                14,true,configured?Brush("#13795C"):Brush("#AA7134")),
            T("Khóa được giữ trong Windows Credential Manager khi cài đè HISA trên cùng tài khoản.",12,false,Muted),
            B("Quản lý / xóa Gemini API Key",()=>Navigate("settings")))));
        page.Children.Add(HubSection("CHỌN CÔNG VIỆC CẦN AI HỖ TRỢ",
            "Trò chuyện và đọc ảnh là hai công cụ chung một nơi, dùng chung khóa nhưng giữ quyền thao tác riêng.",
            WorkflowTile("✧","Trò chuyện với Gemini",
                "Hỏi công việc kho, lịch xuất, tra cứu và giải thích quy tắc.",
                "Dữ liệu kho chia sẻ khi anh bật",()=>Navigate("gemini-chat"),true),
            WorkflowTile("✦","Đọc lịch Zalo",
                "Tách lịch cont dự kiến về và lịch dự kiến xuất kho.",
                "Kiểm tra, duyệt rồi mới lưu",()=>Navigate("ai-schedule")),
            WorkflowTile("⚙","Cấu hình AI",
                "Lưu, thay hoặc xóa Gemini API Key.",
                "Giữ khóa khi cập nhật",()=>Navigate("settings"))));
        SetPage("Trung tâm Gemini AI","Trò chuyện · đọc lịch Zalo · cấu hình khóa",
            new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }

    // A single management home for contract flags, handover slips, history and settings.
    // All original screens and their persisted stores remain unchanged.
    private void ShowManagementHub()
    {
        _page="management-hub";
        var page=Stack();
        page.Children.Add(HubHeader("QUẢN LÝ & LỊCH SỬ",
            "Một nơi cho trạng thái hợp đồng, phiếu bàn giao, báo cáo lịch sử và cấu hình"));
        page.Children.Add(StyledDashboardPanel(Stack(
            T("HỒ SƠ ĐANG QUẢN LÝ",14,true),
            T(_service.Flags.Count+" trạng thái hoãn / hủy lấy hàng · "+
              _service.Slips.Count+" phiếu bàn giao",13,false,Muted),
            T("Trạng thái hợp đồng và trạng thái phiếu là hai loại hồ sơ khác nhau; không gộp hoặc xóa các bản ghi cũ.",11,false,Brush("#946E3E")))));
        page.Children.Add(HubSection("TRẠNG THÁI & PHIẾU BÀN GIAO",
            "Quản lý theo số hợp đồng và từng phiếu. Chỉ ghi nhận đã xuất khi anh xác nhận bàn giao.",
            WorkflowTile("01","Trạng thái hợp đồng",
                "Ghi chú hoãn / hủy lấy hàng, lý do và thời điểm cập nhật.",
                "Không thay đổi phiếu đã xuất",()=>Navigate("flags"),true),
            WorkflowTile("02","Phiếu bàn giao",
                "Lọc Chờ xuất, Đã xuất, Tạm hoãn, Đã hủy; sửa và in phiếu.",
                "Có lịch sử trạng thái từng phiếu",()=>Navigate("slips"))));
        page.Children.Add(HubSection("LỊCH SỬ & BÁO CÁO",
            "Xem kết quả theo ngày; muốn xem ai đổi trạng thái một phiếu, mở Phiếu bàn giao → Lịch sử trạng thái.",
            WorkflowTile("03","Lịch sử xuất & báo cáo",
                "Tra cứu phiếu theo ngày, vật tư đã xuất và xuất báo cáo Excel.",
                "Không coi lịch dự kiến là đã xuất",()=>Navigate("daily"))));
        page.Children.Add(HubSection("CẤU HÌNH & CÔNG CỤ",
            "Giữ nguyên đường dẫn dữ liệu và quyền phê duyệt. Sao lưu không có nghĩa là đã phục hồi.",
            WorkflowTile("04","Cấu hình & sao lưu",
                "Nguồn Excel, Gemini API Key, đồng bộ và bản sao lưu ZIP.",
                "Phục hồi từ ZIP chưa có trên v1.9.0",()=>Navigate("settings")),
            WorkflowTile("05","Mẫu phiếu in",
                "Quản lý mẫu phiếu, căn lề và in thử.",
                "Mẫu in dùng chung",()=>Navigate("templates")),
            WorkflowTile("06","Thông tin & cập nhật",
                "Xem phiên bản, kiểm tra và tải bản cập nhật.",
                "Không tự ghi dữ liệu kho",()=>Navigate("information"))));
        SetPage("Quản lý & lịch sử",
            "Trạng thái hợp đồng · phiếu bàn giao · lịch sử · cấu hình & sao lưu",
            new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }

}
