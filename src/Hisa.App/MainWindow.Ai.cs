using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Hisa.App;

public partial class MainWindow
{
    private AiScheduleDocument? _aiDocument;
    private string _aiImagePath = "";
    private string? _aiDispatchDay;
    private bool _aiApproved;

    private static DataGrid ScheduleEditor(List<AiScheduleLine> rows, bool incoming)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(0, 8, 0, 6),
            MinHeight = 240
        };
        void TextColumn(string label, string path, double width, bool edit = true)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = label,
                Binding = new Binding(path)
                {
                    Mode = edit ? BindingMode.TwoWay : BindingMode.OneWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
                },
                IsReadOnly = !edit,
                Width = width < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star)
                                  : new DataGridLength(width)
            });
        }
        TextColumn("Số HĐ", "Contract", 110);
        TextColumn("Công trình", "Project", -1);
        TextColumn("Đơn vị", "Company", 135);
        TextColumn("Ngày dự kiến", "PlannedDate", 118);
        if (incoming)
        {
            TextColumn("Container", "Container", 125);
            TextColumn("Ngày xuất cont", "ContainerExportDate", 125);
        }
        TextColumn("Ghi chú", "Note", 185);
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Khách gấp",
            Binding = new Binding("Urgent") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 80
        });
        TextColumn("Trạng thái lịch xuất", "ScheduleLabel", 158, false);
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Cần kiểm tra",
            Binding = new Binding("NeedsReview") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 95
        });
        TextColumn("Lý do kiểm tra", "ReviewNote", 230);
        return grid;
    }

    private void ShowAiSchedules()
    {
        _page = "ai-schedule";
        var upload = new TextBlock
        {
            Text = _aiImagePath.Length > 0 ? Path.GetFileName(_aiImagePath) : "Chưa chọn ảnh lịch từ Zalo",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8)
        };
        var keySaved = new GeminiCredentialStore().HasKey();
        var status = T(keySaved
            ? "● ĐÃ CẤU HÌNH GEMINI · Khóa được lưu an toàn trong Windows và giữ lại sau khi cập nhật."
            : "○ CHƯA CÓ GEMINI API KEY · Vào Cấu hình & sao lưu → Gemini AI để nhập và lưu.",
            12, true, keySaved ? Brush("#16745C") : Brush("#AF6A38"));
        var choose = B("Chọn ảnh từ Zalo", () =>
        {
            var file = new OpenFileDialog
            {
                Title = "Chọn ảnh chụp lịch nhập / xuất kho",
                Filter = "Ảnh JPG, PNG, WEBP|*.jpg;*.jpeg;*.png;*.webp"
            };
            if (file.ShowDialog(this) == true)
            {
                _aiImagePath = file.FileName;
                _aiDocument = null;
                _aiApproved = false;
                upload.Text = Path.GetFileName(file.FileName);
            }
        }, true);
        var analyze = A("②  Phân tích ảnh với Gemini", async () =>
        {
            if (_aiImagePath.Length == 0)
                throw new InvalidOperationException("Chọn một ảnh lịch nhập/xuất trước.");
            var geminiKey = new GeminiCredentialStore().ReadKey();
            if (string.IsNullOrWhiteSpace(geminiKey))
                throw new InvalidOperationException("Chưa lưu Gemini API Key. Vào Cấu hình & sao lưu → Gemini AI để nhập khóa.");
            status.Text = "Đang phân tích ảnh bằng Gemini...";
            try
            {
                _aiDocument = await AiScheduleReader.AnalyzeAsync(_aiImagePath, geminiKey);
                _aiApproved = false;
                ShowAiSchedules();
            }
            catch (Exception ex)
            {
                // Provider errors are normalized and never include API keys.
                status.Text = ex.Message;\n                status.Foreground = Brush("#AA4D39");
                throw;
            }
        }, true);
        var top = Card(Stack(
            T("GEMINI AI  /  TRỢ LÝ ĐỌC LỊCH XUẤT HÀNG", 18, true),
            T("① Chọn ảnh Zalo     →     ② Gemini đọc bảng     →     ③ Kiểm tra, duyệt     →     ④ Nạp kế hoạch", 13, true, Brush("#177E89")),
            T("HISA tách riêng lịch container dự kiến về Hưng Yên và lịch xuất kho ngày mai. Không tự nhập kho hay tạo phiếu Đã xuất.", 12, false, Muted),
            status,
            Row(choose, upload),
            Row(analyze, B("⚙  Cấu hình Gemini", () => Navigate("settings"))),
            T("Bảo mật: khóa chỉ đọc từ Windows khi phân tích. Ảnh chỉ gửi đến Gemini sau khi anh bấm Phân tích.", 11, false, Muted)
        ));
        var panel = Stack();
        if (_aiDocument == null)
        {
            panel.Children.Add(Alert("Sau khi Gemini phân tích, anh xem, sửa và duyệt từng dòng rồi lưu lịch. Khóa Gemini không nằm trong bộ cài và vẫn giữ khi cập nhật HISA."));
            SetPage("AI lịch nhập – xuất kho", "Đọc ảnh từ Zalo · Gemini AI · duyệt thủ công trước khi lưu", Layout(top,
                new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
            return;
        }

        var doc = _aiDocument;
        var dayArrival = Input(doc.ArrivalDate);
        dayArrival.Width = 150;
        var dayDispatch = Input(doc.DispatchDate);
        dayDispatch.Width = 150;
        var incoming = ScheduleEditor(doc.Arrivals, true);
        var outgoing = ScheduleEditor(doc.Dispatches, false);
        incoming.CellEditEnding += (_, _) => _aiApproved = false;
        outgoing.CellEditEnding += (_, _) => _aiApproved = false;

        var incomingCount = T(doc.Arrivals.Count + " công trình · hàng dự kiến nhập kho", 14, true);
        var outgoingCount = T(doc.Dispatches.Count + " công trình · hàng dự kiến xuất kho", 14, true);
        var incomingBody = Stack(incomingCount, incoming);
        var outgoingBody = Stack(outgoingCount, outgoing);

        // The tri-state "Đã có LX" is never interpreted as driver availability.
        void CycleSchedule(DataGrid grid)
        {
            if (grid.SelectedItem is not AiScheduleLine selected)
            {
                Notify("Chọn một dòng trong bảng để điều chỉnh trạng thái lịch xuất."); return;
            }
            selected.HasExportSchedule = selected.HasExportSchedule switch
            {
                null => true,
                true => false,
                false => null
            };
            _aiApproved = false;
            grid.Items.Refresh();
        }
        incomingBody.Children.Add(Row(B("Đổi trạng thái lịch xuất của dòng chọn", () => CycleSchedule(incoming))));
        outgoingBody.Children.Add(Row(B("Đổi trạng thái lịch xuất của dòng chọn", () => CycleSchedule(outgoing))));

        var warningsBlock = T("", 12, false, Muted);
        void RefreshWarnings()
        {
            Commit(incoming);
            Commit(outgoing);
            doc.ArrivalDate = dayArrival.Text.Trim();
            doc.DispatchDate = dayDispatch.Text.Trim();
            doc.Warnings = doc.Warnings.Distinct().ToList();
            var warnings = AiScheduleReader.ValidateWithWarehouse(doc, _service.Snapshot);
            warningsBlock.Text = warnings.Count == 0
                ? "Không có cảnh báo bổ sung. Vẫn cần xác nhận với ảnh nguồn."
                : string.Join("\n", warnings.Take(60)) + (warnings.Count > 60 ? "\n... còn " + (warnings.Count - 60) + " cảnh báo." : "");
        }
        RefreshWarnings();
        var saved = T(_aiApproved ? "Kế hoạch đã được duyệt trong phiên này." : "Chưa duyệt. Mọi dòng có thể chỉnh sửa trước khi lưu.", 12, true);
        var save = B("Lưu kế hoạch đã duyệt", () =>
        {
            RefreshWarnings();
            if (doc.Arrivals.Count + doc.Dispatches.Count == 0)
                throw new InvalidOperationException("Danh sách trống, không thể duyệt.");
            if (!DateTime.TryParseExact(doc.ArrivalDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _) ||
                !DateTime.TryParseExact(doc.DispatchDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                throw new InvalidOperationException("Sửa cả hai ngày dự kiến theo yyyy-MM-dd trước khi lưu.");
            var review = doc.Arrivals.Concat(doc.Dispatches).Count(r => r.NeedsReview);
            if (!Ask("Duyệt và lưu " + doc.Arrivals.Count + " dòng nhập, " + doc.Dispatches.Count +
                     " dòng xuất?\n" + review + " dòng đang đánh dấu cần kiểm tra.\n\n" +
                     "Đây chỉ là LỊCH DỰ KIẾN; không cập nhật tồn kho, không tự lập phiếu đã xuất.")) return;
            var folder = Path.Combine(_service.DataDirectory, "ai_schedules");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "lich-kho-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                             "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            doc.ApprovedAt = DateTimeOffset.Now.ToString("O");
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(doc,
                    new JsonSerializerOptions(WarehouseService.Json) { WriteIndented = true }));
                File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            _aiApproved = true;
            saved.Text = "Đã lưu kế hoạch: " + Path.GetFileName(path);
            Notify("Đã lưu kế hoạch dự kiến vào:\n" + path +
                "\n\nKhông có phiếu Đã xuất hoặc ngày cont về nào được ghi tự động.");
        }, true);
        var toPlan = B("Nạp các hợp đồng xuất vào kế hoạch", () =>
        {
            if (!_aiApproved) { Notify("Hãy duyệt và lưu bảng trước khi nạp vào kế hoạch."); return; }
            RefreshWarnings();
            if (doc.DispatchDate.Length == 0) { Notify("Thiếu ngày dự kiến xuất."); return; }
            var codes = doc.Dispatches.Select(x => x.Contract).Where(x => x.Length > 0)
                .DistinctBy(Rules.Contract).ToArray();
            if (codes.Length == 0) { Notify("Chưa có mã hợp đồng xuất kho."); return; }
            if (!Ask("Nạp " + codes.Length + " hợp đồng vào màn hình kế hoạch?\n" +
                "Bạn vẫn cần kiểm tra vật tư và bấm lưu phiếu chờ thủ công.")) return;
            _draft = null;
            _draftReport = _service.Search(new SearchFilter { ContractsText = string.Join("\n", codes) });
            _aiDispatchDay = doc.DispatchDate;
            _createPlanMode = true;
            Navigate("plan");
        });
        var tools = Card(Stack(
            T("Ngày và phê duyệt", 16, true),
            Row(Field("Dự kiến nhập kho · yyyy-MM-dd", dayArrival),
                Field("Dự kiến xuất kho · yyyy-MM-dd", dayDispatch),
                B("Kiểm tra cảnh báo", RefreshWarnings)),
            T("Lưu kế hoạch vào thư mục dữ liệu HISA. Bấm Nạp hợp đồng để chuyển sang màn hình kế hoạch cũ; mọi bước xuất kho vẫn cần xác nhận riêng.", 12, false, Muted),
            Row(save, toPlan, B("Kiểm tra sẵn sàng xuất", () =>
            {
                if (!_aiApproved) { Notify("Hãy lưu lịch AI đã duyệt trước."); return; }
                ShowDispatchReadiness(doc.DispatchDate);
            })),
            saved
        ));
        var kpis = Row(
            Card(Stack(T("CONT DỰ KIẾN VỀ", 11, true, Muted),
                T(doc.Arrivals.Count.ToString("N0") + " hợp đồng", 22, true),
                T("Chưa tính là đã nhập kho", 11, false, Muted)), new Thickness(14)),
            Card(Stack(T("KẾ HOẠCH XUẤT", 11, true, Muted),
                T(doc.Dispatches.Count.ToString("N0") + " hợp đồng", 22, true),
                T("Chờ phê duyệt trước khi lưu", 11, false, Muted)), new Thickness(14)),
            Card(Stack(T("CẦN XÁC MINH", 11, true, Muted),
                T(doc.Arrivals.Concat(doc.Dispatches).Count(x => x.NeedsReview).ToString("N0") + " dòng", 22, true),
                T("Đối chiếu ảnh Zalo gốc", 11, false, Muted)), new Thickness(14))
        );
        panel.Children.Add(kpis);
        panel.Children.Add(tools);
        panel.Children.Add(T("Nguồn nhận dạng: " + doc.AnalyzedBy + " · Bản ghi cần xem thủ công: " +
            doc.Arrivals.Concat(doc.Dispatches).Count(x => x.NeedsReview), 12, true));
        panel.Children.Add(T("CẢNH BÁO / ĐỐI CHIẾU", 13, true));
        panel.Children.Add(Card(warningsBlock));
        panel.Children.Add(T("PHẦN 1 · NHẬP KHO", 16, true));
        panel.Children.Add(Card(incomingBody));
        panel.Children.Add(T("PHẦN 2 · XUẤT KHO", 16, true));
        panel.Children.Add(Card(outgoingBody));
        SetPage("AI lịch nhập – xuất kho", "Gemini AI · sửa bảng · duyệt kế hoạch trước khi lưu", Layout(top,
            new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
    }
}
