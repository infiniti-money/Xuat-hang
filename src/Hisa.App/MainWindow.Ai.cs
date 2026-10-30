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
        if (incoming)
        {
            grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header="Chọn nhận",
                Binding=new Binding("SelectedForArrival")
                {
                    Mode=BindingMode.TwoWay,
                    UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged
                },
                Width=88
            });
        }
        else
        {
            grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Làm phiếu",
                Binding = new Binding("SelectedForHandover") {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = 88
            });
        }
        TextColumn("Nguồn", "EntrySource", 92, false);
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
        // Manual scheduling must stay usable when Gemini is unconfigured or
        // Windows Credential Manager is temporarily unavailable.
        bool keySaved;
        try { keySaved=new GeminiCredentialStore().HasKey(); }
        catch { keySaved=false; }
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
                status.Text = ex.Message;
                status.Foreground = Brush("#AA4D39");
                throw;
            }
        }, true);
        var createManual=B("＋ Tạo lịch bằng tay",()=>{
            if(_aiDocument!=null && !Ask("Bắt đầu lịch mới? Những chỉnh sửa chưa lưu sẽ mất."))return;
            _aiDocument=AiScheduleWorkspace.NewDraft(DateTime.Today.AddDays(1));
            _aiApproved=false;
            _aiImagePath="";
            ShowAiSchedules();
        });
        var openApproved=B("Mở lịch đã duyệt",()=>{
            var loaded=AiScheduleWorkspace.LatestApproved(_service.DataDirectory);
            if(loaded==null){Notify("Chưa có lịch nhập hoặc xuất đã duyệt được lưu.");return;}
            if(_aiDocument!=null && !Ask("Mở lịch đã lưu? Những chỉnh sửa chưa lưu sẽ mất."))return;
            _aiDocument=loaded;
            _aiApproved=true;
            _aiImagePath="";
            ShowAiSchedules();
        });
        var top = Card(Stack(
            T("LỊCH NHẬP – XUẤT KHO  /  ĐIỀU HÀNH TỪ HAI NGUỒN", 18, true),
            T("① Ảnh Zalo hoặc nhập số HĐ thủ công  →  ② Đối chiếu  →  ③ Duyệt lịch  →  ④ Chọn nhập kho / phiếu bàn giao",13,true,Brush("#177E89")),
            T("HISA tách riêng lịch container dự kiến về Hưng Yên và lịch xuất kho ngày mai. Không tự nhập kho hay tạo phiếu Đã xuất.", 12, false, Muted),
            status,
            Row(choose, upload),
            Row(analyze,createManual,openApproved,B("⚙  Cấu hình Gemini", () => Navigate("settings"))),
            T("Bảo mật: khóa chỉ đọc từ Windows khi phân tích. Ảnh chỉ gửi đến Gemini sau khi anh bấm Phân tích.", 11, false, Muted)
        ));
        var panel = Stack();
        if (_aiDocument == null)
        {
            panel.Children.Add(Alert("Chưa có lịch: chọn ảnh Zalo để Gemini đọc, hoặc bấm 'Tạo lịch bằng tay' để nhập số hợp đồng không xuất hiện trong ảnh. Cả hai luồng đều phải duyệt và xác nhận nghiệp vụ."));
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
        var saved = T(_aiApproved ? "Kế hoạch đã được duyệt trong phiên này." :
            "Chưa duyệt. Mọi dòng có thể chỉnh sửa trước khi lưu.", 12, true);
        // An approved snapshot is immutable. Changing even just the header date
        // invalidates approval, otherwise a new unsaved arrival/dispatch date could
        // be used to create handover slips from the previously approved schedule.
        void InvalidateApproval()
        {
            _aiApproved = false;
            doc.ApprovedAt = "";
            saved.Text = "Lịch đã thay đổi sau lần duyệt. Kiểm tra và lưu duyệt lại trước khi lập phiếu hoặc nhập kho.";
        }
        dayArrival.TextChanged += (_, _) => InvalidateApproval();
        dayDispatch.TextChanged += (_, _) => InvalidateApproval();
        incoming.CellEditEnding += (_, e) =>
        {
            if(e.Column.Header?.ToString() != "Chọn nhận")InvalidateApproval();
        };
        outgoing.CellEditEnding += (_, e) =>
        {
            if (e.Column.Header?.ToString() != "Làm phiếu") InvalidateApproval();
        };

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
            InvalidateApproval();
            grid.Items.Refresh();
        }
        incomingBody.Children.Add(Row(
            B("Chọn tất cả HĐ nhập",()=>{foreach(var r in doc.Arrivals)r.SelectedForArrival=true;incoming.Items.Refresh();}),
            B("Bỏ chọn nhập",()=>{foreach(var r in doc.Arrivals)r.SelectedForArrival=false;incoming.Items.Refresh();}),
            B("✓  Đối chiếu các HĐ đã chọn · cont thực tế đã về", () =>
            {
                if (!_aiApproved || string.IsNullOrWhiteSpace(doc.ApprovedAt))
                {
                    Notify("Hãy kiểm tra và lưu lịch Gemini đã duyệt trước khi ghi nhận cont thực tế.");
                    return;
                }
                Commit(incoming);
                if(doc.Arrivals.All(x=>!x.SelectedForArrival))
                {
                    Notify("Tích ô 'Chọn nhận' cho các HĐ muốn nhận. Sau đó chọn riêng từng kiện thực tế ở màn xác nhận cont về.");
                    return;
                }
                ShowPlannedArrivals(true);
            }, true),
            B("Xem toàn bộ cont dự kiến",()=>ShowPlannedArrivals()),
            B("Đổi trạng thái lịch xuất của dòng chọn", () => CycleSchedule(incoming))
        ));
        outgoingBody.Children.Add(Row(
            B("Chọn tất cả dòng xuất", () => { foreach(var line in doc.Dispatches) line.SelectedForHandover = true; outgoing.Items.Refresh(); }),
            B("Bỏ chọn", () => { foreach(var line in doc.Dispatches) line.SelectedForHandover = false; outgoing.Items.Refresh(); }),
            B("Đổi trạng thái lịch xuất của dòng chọn", () => CycleSchedule(outgoing))));

        var manualCodes=Input("",true);manualCodes.Width=310;manualCodes.Height=98;
        var manualProject=Input();manualProject.Width=225;
        var manualCompany=Input();manualCompany.Width=225;
        var manualCont=Input();manualCont.Width=180;
        var manualDeparture=Input();manualDeparture.Width=160;
        var manualNote=Input();manualNote.Width=250;
        var manualMode=new ComboBox{
            Width=195,ItemsSource=new[]{"Cont dự kiến về Hưng Yên","Hợp đồng dự kiến bàn giao"},
            SelectedIndex=1
        };
        var manualUrgent=new CheckBox{Content="Khách cần gấp",VerticalAlignment=VerticalAlignment.Center};
        var manualTools=Card(Stack(
            T("＋ THÊM SỐ HỢP ĐỒNG THỦ CÔNG",16,true),
            T("Một số HĐ nằm trên ảnh Zalo, số phát sinh nhập tại đây. Dán nhiều số HĐ (mỗi dòng một mã); hệ thống chỉ tự điền công trình/đơn vị khi dữ liệu khớp duy nhất.",12,false,Muted),
            Row(Field("Thêm vào danh sách",manualMode),Field("Các số hợp đồng",manualCodes),
                Field("Công trình (nếu biết)",manualProject),Field("Đơn vị (nếu biết)",manualCompany)),
            Row(Field("Số container (hàng nhập)",manualCont),
                Field("Ngày cont đi yyyy-MM-dd",manualDeparture),
                Field("Ghi chú",manualNote),manualUrgent),
            B("＋ Bổ sung vào lịch đang xem",()=>{
                Commit(incoming);Commit(outgoing);
                doc.ArrivalDate=dayArrival.Text.Trim();doc.DispatchDate=dayDispatch.Text.Trim();
                var isArrival=manualMode.SelectedIndex==0;
                var added=AiScheduleWorkspace.AddManual(doc,_service.Snapshot,isArrival,
                    manualCodes.Text,manualProject.Text,manualCompany.Text,
                    manualCont.Text,manualDeparture.Text,manualNote.Text,
                    manualUrgent.IsChecked==true);
                if(added==0){Notify("Các số hợp đồng này đã có trong lịch cùng công trình, cont và ngày; không tạo dòng trùng.");return;}
                InvalidateApproval();
                Notify("Đã bổ sung "+added+" dòng "+(isArrival?"cont dự kiến về":"dự kiến bàn giao")+
                    ". Kiểm tra công trình, đơn vị và ngày trước khi bấm Lưu kế hoạch đã duyệt.");
                ShowAiSchedules();
            },true),
            T("HĐ nhập tay không tự nhập kho và không tự tạo phiếu. Nếu trùng số HĐ nhưng khác T1/T2, cont hoặc ngày cont đi, thêm thành dòng riêng và đối chiếu vật tư trước khi xác nhận.",11,false,Muted)));
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
        var toPlan = A("✓  Lập phiếu từ hợp đồng đã tích", async () =>
        {
            Commit(outgoing);
            if (!_aiApproved || string.IsNullOrWhiteSpace(doc.ApprovedAt))
            {
                Notify("Hãy kiểm tra, duyệt và lưu lịch Gemini trước khi tạo phiếu bàn giao.");
                return;
            }
            var chosen = doc.Dispatches.Where(x => x.SelectedForHandover).ToList();
            if (chosen.Count == 0)
            {
                Notify("Tích chọn ô 'Làm phiếu' của những hợp đồng cần bàn giao trong phần HÀNG DỰ KIẾN XUẤT KHO.");
                return;
            }
            var codes = chosen.Select(x => x.Contract).DistinctBy(Rules.Contract).ToArray();
            var report = _service.Search(new SearchFilter { ContractsText = string.Join("\n", codes) });
            report = AiHandoverSelection.Build(report, doc, chosen);
            if (!Ask("Tạo kế hoạch phiếu CHỜ XUẤT cho " + chosen.Count +
                     " dòng đã tích?\nNgày dự kiến: " + Rules.Day(doc.DispatchDate) +
                     "\n\nĐây là bước chuẩn bị phiếu. Anh kiểm tra vật tư và lưu phiếu chờ trước khi bàn giao.")) return;
            _draft = null;
            _draftReport = report;
            _aiDispatchDay = doc.DispatchDate;
            _createPlanMode = true;
            Navigate("plan");
            await Task.CompletedTask;
        }, true);
        var tools = Card(Stack(
            T("Ngày và phê duyệt", 16, true),
            Row(Field("Dự kiến nhập kho · yyyy-MM-dd", dayArrival),
                Field("Dự kiến xuất kho · yyyy-MM-dd", dayDispatch),
                B("Kiểm tra cảnh báo", RefreshWarnings)),
            T("Tích chọn hợp đồng ở bảng PHẦN 2 · XUẤT KHO, bấm Lập phiếu đã tích để kiểm tra vật tư và tạo phiếu chờ. Không tự ghi Đã xuất.", 12, false, Muted),
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
        panel.Children.Add(manualTools);
        panel.Children.Add(T("Nguồn nhận dạng: " + doc.AnalyzedBy + " · Bản ghi cần xem thủ công: " +
            doc.Arrivals.Concat(doc.Dispatches).Count(x => x.NeedsReview), 12, true));
        panel.Children.Add(T("CẢNH BÁO / ĐỐI CHIẾU", 13, true));
        panel.Children.Add(Card(warningsBlock));
        panel.Children.Add(T("PHẦN 1 · CONT DỰ KIẾN VỀ · TÍCH CHỌN HĐ RỒI XÁC NHẬN TỪNG KIỆN THỰC TẾ",16,true));
        panel.Children.Add(Card(incomingBody));
        panel.Children.Add(T("PHẦN 2 · XUẤT KHO · TÍCH CHỌN HỢP ĐỒNG ĐỂ LÀM PHIẾU", 16, true));
        panel.Children.Add(Card(outgoingBody));
        SetPage("AI lịch nhập – xuất kho", "Gemini AI · sửa bảng · duyệt kế hoạch trước khi lưu", Layout(top,
            new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
    }
}
