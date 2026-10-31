using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowDispatchReadiness(string? selectedDay = null)
    {
        _page = "readiness";
        var store = new DispatchReadinessStore(_service.DataDirectory);
        DispatchReadinessState state;
        try { state = store.Load(); }
        catch (Exception ex)
        {
            SetPage("Sẵn sàng xuất hàng", "Không thể đọc trạng thái chuẩn bị",
                Alert("Không đọc được dữ liệu theo dõi chuẩn bị. Chưa ghi đè file cũ: " + ex.Message, true));
            return;
        }
        var tomorrow = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var day = selectedDay ?? (state.Plans.Any(p => p.Day == today) && !state.Plans.Any(p => p.Day == tomorrow) ? today : tomorrow);
        var result = DispatchReadinessStore.Evaluate(state, _service.Snapshot, day);
        var chooseDay = DateInput(day);
        chooseDay.SelectedDateChanged += (_, _) =>
        {
            if (Day(chooseDay).Length > 0) ShowDispatchReadiness(Day(chooseDay));
        };

        var top = Stack(T("ĐIỀU HÀNH XUẤT HÀNG THEO NGÀY", 17, true),
            T("Vật tư đã chuẩn bị vẫn nằm ở kho Hưng Yên. Chỉ phiếu bàn giao được xác nhận Đã xuất mới cập nhật sổ.", 12, false, Muted),
            Row(Field("Ngày dự kiến xuất", chooseDay),
                B("Đọc ảnh lịch từ Zalo", () => Navigate("ai-schedule"))),
                        T("Ngày container xuất nhà máy và ngày cont dự kiến về KHÔNG phải ngày xuất kho. Kế hoạch xuất chỉ lấy từ phần lịch xuất đã duyệt.", 11, false, Muted));
        var overview = Stack();
        if (result.Count == 0)
            overview.Children.Add(Alert("Chưa có lịch dự kiến ngày " + Rules.Day(day) +
                ". Anh muốn nhập ảnh Zalo hay bổ sung hợp đồng phát sinh bằng tay? Không tự suy đoán danh sách xuất."));
        else
            overview.Children.Add(T("Tổng cộng " + result.Count + " hợp đồng · " +
                result.Count(x => x.Status.StartsWith("Sẵn sàng", StringComparison.Ordinal)) + " sẵn sàng · " +
                result.Count(x => x.Status == "Thiếu vật tư") + " báo thiếu · " +
                result.Count(x => x.Status.StartsWith("Chưa", StringComparison.Ordinal) ||
                    x.Status.StartsWith("Cần", StringComparison.Ordinal)) + " cần kiểm tra", 13, true));

        var manualCode = Input(); manualCode.Width = 160;
        var manualProject = Input(); manualProject.Width = 255;
        var manualCompany = Input(); manualCompany.Width = 225;
        var manualReason = Input(); manualReason.Width = 235;
        var manualNote = Input(); manualNote.Width = 255;
        var destination = new ComboBox { Width = 190, ItemsSource = new[] { "Kho thành phẩm", "Xuất trực tiếp khách" }, SelectedIndex = 0 };
        var manual = Card(Stack(T("Hợp đồng phát sinh · nhập thủ công", 15, true),
            Row(Field("Số hợp đồng", manualCode), Field("Công trình (nếu có)", manualProject),
                Field("Đơn vị (nếu có)", manualCompany)),
            Row(Field("Nơi bàn giao", destination), Field("Lý do (tùy chọn)", manualReason),
                Field("Ghi chú (tùy chọn)", manualNote)),
            Row(A("+ Thêm hợp đồng phát sinh", async () =>
            {
                DispatchReadinessStore.AddManual(state, day, manualCode.Text, manualProject.Text,
                    manualCompany.Text, destination.SelectedItem?.ToString() ?? "Kho thành phẩm",
                    manualReason.Text, manualNote.Text);
                store.Save(state);
                ShowDispatchReadiness(day);
                await Task.CompletedTask;
            }, true)),
            T("Hợp đồng nhập tay được gắn nguồn riêng. Không cần ghi lý do nếu không phát sinh gấp.", 11, false, Muted)));

        var plansGrid = Table(result, ("Ưu tiên", "Priority", 70), ("Số HĐ", "Contract", 120),
            ("Công trình", "Project", -1), ("Nguồn", "Origin", 130),
            ("Nơi nhận", "Destination", 155), ("Số VT", "MaterialCount", 65),
            ("Tình trạng", "Status", 225), ("Cần xem", "Review", 120));
        plansGrid.MaxHeight = 310;
        plansGrid.MinHeight = 150;
        var materialHost = new ContentControl();
        void ShowMaterialDetails()
        {
            if (plansGrid.SelectedItem is not DispatchPlanStatus selected)
            {
                materialHost.Content = Alert("Chọn một hợp đồng ở bảng trên để xem từng vật tư.");
                return;
            }
            var detail = Stack(T("Vật tư cần chuẩn bị: " + selected.Contract + " — " + selected.Project, 15, true),
                T("Các kích thước 700, 750 và model khác nhau được phân biệt theo tên vật tư đầy đủ. Chưa cập nhật không đồng nghĩa đã thiếu.", 11, false, Muted));
            if (selected.Materials.Count == 0)
            {
                detail.Children.Add(Alert(selected.Status + ". Hãy kiểm tra file Tổng hợp hoặc chọn đúng công trình; không tạo vật tư giả."));
                materialHost.Content = Card(detail);
                return;
            }
            var grid = Table(selected.Materials, ("Tên vật tư / quy cách", "Material", -1),
                ("Nhóm", "Group", 125), ("SL cần", "RequiredLabel", 75),
                ("Đã chuẩn bị", "PreparedLabel", 90), ("ĐVT", "Unit", 70),
                ("Tình trạng", "Status", 185));
            grid.MaxHeight = 330;
            grid.MinHeight = 140;
            var quantity = Input(); quantity.Width = 100;
            var serials = Input("", true); serials.Width = 330; serials.Height = 66;
            grid.SelectionChanged += (_, _) =>
            {
                if (grid.SelectedItem is not DispatchMaterialStatus item) return;
                quantity.Text = item.Prepared?.ToString(CultureInfo.InvariantCulture) ?? "";
                serials.Text = item.Serials;
            };
            detail.Children.Add(grid);
            detail.Children.Add(Row(Field("SL đã chuẩn bị", quantity),
                Field("Serial máy kéo (mỗi dòng một serial)", serials),
                A("Xác nhận vật tư đã chuẩn bị", async () =>
                {
                    if (grid.SelectedItem is not DispatchMaterialStatus item)
                        throw new InvalidOperationException("Chọn dòng vật tư cần cập nhật.");
                    if (!decimal.TryParse(quantity.Text.Trim().Replace(',', '.'), NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var number))
                        throw new InvalidOperationException("Nhập số lượng hợp lệ.");
                    DispatchReadinessStore.UpdatePrepared(state, item, number, serials.Text);
                    store.Save(state);
                    ShowDispatchReadiness(day);
                    await Task.CompletedTask;
                }, true)));
            detail.Children.Add(T("Máy kéo chỉ báo đủ khi có số serial tương ứng. Các số lượng chuẩn bị do anh xác nhận, không suy ra từ lịch cont về.", 11, false, Muted));
            materialHost.Content = Card(detail);
        }
        plansGrid.SelectionChanged += (_, _) => ShowMaterialDetails();
        var buttons = Row(A("Xóa hợp đồng đang chọn khỏi lịch", async () =>
        {
            if (plansGrid.SelectedItem is not DispatchPlanStatus selected)
                throw new InvalidOperationException("Chọn hợp đồng muốn xóa khỏi lịch.");
            if (!Ask("Xóa " + selected.Contract + " khỏi kế hoạch ngày " + Rules.Day(day) +
                "?\nDữ liệu vật tư đã chuẩn bị và lịch sử bàn giao không bị xóa.")) return;
            state.Plans.RemoveAll(p => p.Id == selected.Plan.Id);
            store.Save(state);
            ShowDispatchReadiness(day);
            await Task.CompletedTask;
        }, false, true),
            B("Đến lập phiếu chờ / bàn giao", () => Navigate("create")));
        var content = Stack(Card(top), Card(overview), manual,
            T("DANH SÁCH DỰ KIẾN XUẤT NGÀY " + Rules.Day(day), 15, true),
            Card(Stack(plansGrid, buttons)), materialHost);
        SetPage("Sẵn sàng xuất hàng", "Kế hoạch ngày · chuẩn bị vật tư · chỉ bàn giao theo xác nhận",
            new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (result.Count > 0) plansGrid.SelectedIndex = 0;
        else ShowMaterialDetails();
    }
}
