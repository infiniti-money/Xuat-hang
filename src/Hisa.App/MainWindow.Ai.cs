using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace Hisa.App;

public partial class MainWindow
{
    private AiScheduleDocument? _aiDocument;
    private string _aiImagePath = "";
    private string? _aiDispatchDay;
    // Step 1: incoming containers; step 2: outgoing slips.
    private bool _aiShowDispatch;
    // Checked material groups survive switching between Cont and dispatch steps.
    private List<string> _aiHandoverGroups=HandoverMaterialGroups.All;

    // DataGridCheckBoxColumn makes operators click once to edit and again to
    // check. This display template contains a real, immediately interactive
    // CheckBox and writes to the per-row flag on the very first mouse click.
    private static DataGridTemplateColumn OneClickCheckColumn(
        string header,string path,double width,Action? onDataEdited=null)
    {
        var factory=new FrameworkElementFactory(typeof(CheckBox));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        factory.SetValue(FrameworkElement.LayoutTransformProperty,new ScaleTransform(1.4,1.4));
        var binding=new Binding(path)
        {
            Mode=BindingMode.TwoWay,
            UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,
            NotifyOnSourceUpdated=onDataEdited!=null
        };
        factory.SetBinding(CheckBox.IsCheckedProperty,binding);
        if(onDataEdited!=null)
            factory.AddHandler(Binding.SourceUpdatedEvent,
                new EventHandler<DataTransferEventArgs>((_,_)=>onDataEdited()));
        return new DataGridTemplateColumn
        {
            Header=header,
            CellTemplate=new DataTemplate {VisualTree=factory},
            Width=width,
            IsReadOnly=true
        };
    }

    private static DataGrid ScheduleEditor(List<AiScheduleLine> rows, bool incoming,Action? onDataEdited=null)
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
        // Selection flags are transient and must NOT invalidate approval.
        grid.Columns.Add(incoming
            ? OneClickCheckColumn("Cont đã về?","SelectedForArrival",110)
            : OneClickCheckColumn("Làm phiếu","SelectedForHandover",100));
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
        grid.Columns.Add(OneClickCheckColumn("Khách gấp","Urgent",86,onDataEdited));
        TextColumn("Trạng thái lịch xuất", "ScheduleLabel", 158, false);
        grid.Columns.Add(OneClickCheckColumn("Cần kiểm tra","NeedsReview",108,onDataEdited));
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
                _aiShowDispatch = false;
                _aiHandoverGroups=HandoverMaterialGroups.All;
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
                _aiShowDispatch = false;
                _aiHandoverGroups=HandoverMaterialGroups.All;
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
            _aiShowDispatch=false;
            _aiHandoverGroups=HandoverMaterialGroups.All;
            _aiImagePath="";
            ShowAiSchedules();
        });
        var top = Card(Stack(
            T("ẢNH ZALO → NHẬN CONT / LÀM PHIẾU", 18, true),
            T("① Chọn ảnh Zalo  →  ② Tích hợp đồng cont về  →  ③ Tiếp theo: Lập phiếu xuất.",13,true,Brush("#177E89")),
            T("Ngày cont dự kiến về chỉ là dự báo, KHÔNG phải hàng đã nhập. Nhận cont và làm phiếu là hai thao tác độc lập.",12,false,Muted),
            status,
            Row(choose,upload,analyze),
            Row(createManual,B("⚙  Cấu hình Gemini", () => Navigate("settings"))),
            T("Chỉ dùng lịch đang làm việc trong phiên này; không nạp lại lịch AI đã duyệt cũ. Mọi nhập kho phải xác nhận đúng kiện thực tế.",11,false,Muted)
        ));
        var panel = Stack();
        if (_aiDocument == null)
        {
            panel.Children.Add(Alert("Chưa có lịch: chọn ảnh Zalo để Gemini đọc, hoặc bấm 'Tạo lịch bằng tay' để nhập số hợp đồng không xuất hiện trong ảnh. Không cần duyệt lịch; nhập kho và xuất thực tế vẫn cần xác nhận riêng."));
            SetPage("AI lịch nhập – xuất kho", "Đọc ảnh Zalo · chọn cont trước, sau đó lập phiếu", Layout(top,
                new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
            return;
        }

        var doc = _aiDocument;
        var dayArrival = Input(doc.ArrivalDate);
        dayArrival.Width = 150;
        var dayDispatch = Input(doc.DispatchDate);
        dayDispatch.Width = 150;
        Action? invalidateEditedCheck = null;
        var incoming = ScheduleEditor(doc.Arrivals, true,()=>invalidateEditedCheck?.Invoke());
        var outgoing = ScheduleEditor(doc.Dispatches, false,()=>invalidateEditedCheck?.Invoke());
        var saved=T("Chọn hợp đồng trực tiếp, không cần lưu/duyệt lịch Zalo.",12,true);
        // Changing a working list never implies approval of a physical movement.
        void MarkWorkingSchedule()
        {
            doc.ApprovedAt="";
            saved.Text="Lịch đang xử lý · không cần phê duyệt trước khi lập phiếu.";
        }
        invalidateEditedCheck=MarkWorkingSchedule;
        dayArrival.TextChanged+=(_,_)=>MarkWorkingSchedule();
        dayDispatch.TextChanged+=(_,_)=>MarkWorkingSchedule();
        incoming.CellEditEnding+=(_,e)=>{
            if(e.Column.Header?.ToString()!="Cont đã về?")MarkWorkingSchedule();
        };
        outgoing.CellEditEnding+=(_,e)=>{
            if(e.Column.Header?.ToString()!="Làm phiếu")MarkWorkingSchedule();
        };

        var incomingCount = T(doc.Arrivals.Count + " cont/hợp đồng dự kiến về · chưa nhập kho", 14, true);
        var outgoingCount = T(doc.Dispatches.Count + " hợp đồng dự kiến bàn giao · chưa xuất kho", 14, true);
        var incomingBody = Stack(incomingCount);
        var outgoingBody = Stack(outgoingCount);

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
            MarkWorkingSchedule();
            grid.Items.Refresh();
        }
        incomingBody.Children.Add(T("Nếu cont THỰC TẾ đã về, tích đúng hợp đồng ở cột 'Cont đã về?', rồi đối chiếu và chọn từng kiện. Ngày dự kiến không phải ngày thực tế.",12,false,Muted));
        incomingBody.Children.Add(Row(
            B("☑ Chọn tất cả cont Hưng Yên",()=>
            {
                Commit(incoming);
                AiScheduleBulkSelection.SetAll(doc,true,true);
                incoming.Items.Refresh();
                saved.Text="Đã chọn "+doc.Arrivals.Count+" HĐ để đối chiếu kiện. CHƯA xác nhận hàng đã về hoặc ghi Excel.";
            },true),
            B("Bỏ tích cont",()=>{AiScheduleBulkSelection.SetAll(doc,true,false);incoming.Items.Refresh();}),
            B("Trạng thái lịch xuất",()=>CycleSchedule(incoming))
        ));
        incomingBody.Children.Add(T("Nhấp MỘT LẦN vào ô vuông để tích. Chọn một dòng bất kỳ để xem từng vật tư, số lượng và tình trạng thực nhận ngay bên dưới.",12,false,Muted));
        incomingBody.Children.Add(incoming);
        // Exact source search is available as soon as the photo is analyzed
        // or incoming contract codes are entered. Approval/receipt is a separate
        // explicit action: the source file, date and each package are verified.
        if(doc.Arrivals.Count>0)
        {
            incomingBody.Children.Add(T("TÌM KIỆN HƯNG YÊN TỪ CONTAINER · XÁC NHẬN VỀ KHO",16,true));
            incomingBody.Children.Add(BuildContDiSourcePanel(
                ()=>doc.Arrivals.Where(x=>x.SelectedForArrival).ToList(),
                ()=>Commit(incoming),searchWhenLoaded:false));
        }

        EquipmentListReader.Catalog? productionCache=null;
        EquipmentListReader.Catalog Production()=>
            productionCache??=EquipmentListReader.Load(_service.Settings);
        var arrivalMaterials=new ContentControl{Margin=new Thickness(0,9,0,10)};
        var dispatchMaterials=new ContentControl{Margin=new Thickness(0,9,0,10)};
        FrameworkElement MaterialDetail(AiScheduleLine line)
        {
            var match=AiMaterialLookup.ForContract(line,_service.Snapshot);
            var grid=Table(match.Items,
                ("Số HĐ","Contract",112),("Công trình","Project",175),
                ("Tên vật tư","Material",-1),("ĐVT","Unit",70),("SL","Quantity",85),
                ("Kiện","PackageNo",65),("Ngày cont đi","SourceDate",115),
                ("Ngày cont về","ArrivalDate",115),("Trạng thái","Status",155),
                ("Ghi chú","Note",220));
            grid.MinHeight=108;
            grid.MaxHeight=265;
            var view=Stack(
                T("VẬT TƯ CỦA HĐ "+line.Contract+" · "+line.Project,14,true),
                T(match.Message,12,false,match.Exact?Brush("#177E89"):Brush("#AB6A37")),
                grid);
            var production=Production();
            var related=production.Find(line);
            if(related.Count>0)
            {
                var equipmentGrid=Table(related,
                    ("Nhóm","Category",100),("Công trình","Project",170),
                    ("Công ty","Company",125),("Tên thiết bị","Material",-1),
                    ("ĐVT","Unit",64),("Số lượng","Quantity",85),
                    ("Seri","Serial",115),("Ngày SX","ProductionDate",112),
                    ("Đối chiếu","Verification",215),
                    ("Ghi chú","Note",240));
                equipmentGrid.IsReadOnly=true;
                equipmentGrid.MaxHeight=245;
                view.Children.Add(T("THIẾT BỊ TỪ 3 DANH SÁCH SẢN XUẤT (CHỈ XEM)",14,true));
                view.Children.Add(T("Tủ điện · Máy kéo · Đầu cửa car. Có tên trong danh sách KHÔNG xác nhận đã nhập kho; kiểm tra công trình/đơn vị trước khi dùng thông tin.",11,false,Muted));
                view.Children.Add(equipmentGrid);
                var machines=new MotorHungYenStore(_service.DataDirectory)
                    .List(production.Rows.Where(x=>x.Category=="Máy kéo"));
                var statuses=MayWarehouseAudit.MotorsFor(line,machines,_service.Slips);
                if(statuses.Count>0)
                {
                    var motorGrid=Table(statuses,
                        ("Serial máy kéo","Serial",125),("Máy kéo","Material",165),
                        ("Tình trạng làm","WorkStatus",135),
                        ("Ghi chú/lịch làm (J)","FactoryNote",210),
                        ("Lịch xuất","PlannedWork",155),
                        ("Ngày đã xuất (K)","DispatchDate",115),
                        ("Ngày bàn giao","HandoverDate",112),
                        ("Đối chiếu","Verification",240));
                    motorGrid.IsReadOnly=true;motorGrid.MaxHeight=220;
                    view.Children.Add(T("ĐỐI CHIẾU MÁY KÉO THEO SERIAL · CỘT K FILE MÁY KÉO.xlsx",14,true));
                    view.Children.Add(motorGrid);
                }
            }
            else if(_service.Settings.ElectricalCabinetListPath.Length+
                _service.Settings.TractionMachineListPath.Length+
                _service.Settings.CarDoorListPath.Length>0)
            {
                view.Children.Add(T("Chưa thấy HĐ này trong ba danh sách Tủ điện/Máy kéo/Đầu cửa car đã liên kết.",11,false,Muted));
            }
            if(production.Warnings.Count>0)
                view.Children.Add(T("Lỗi nguồn danh sách thiết bị: "+string.Join(" | ",production.Warnings),11,false,Brush("#AB6A37")));
            return Card(view);
        }
        void UpdateMaterialDetail(DataGrid source,ContentControl host)
        {
            host.Content=source.SelectedItem is AiScheduleLine line
                ? MaterialDetail(line)
                : Alert("Chọn một dòng hợp đồng trong bảng để xem danh sách vật tư từ Tổng hợp.xlsx.");
        }
        incoming.SelectionChanged+=(_,_)=>UpdateMaterialDetail(incoming,arrivalMaterials);
        outgoing.SelectionChanged+=(_,_)=>UpdateMaterialDetail(outgoing,dispatchMaterials);
        incoming.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(
            new Action(()=>UpdateMaterialDetail(incoming,arrivalMaterials)));
        outgoing.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(
            new Action(()=>UpdateMaterialDetail(outgoing,dispatchMaterials)));
        void OpenAllMaterials(List<AiScheduleLine> lines,string title)
        {
            Commit(incoming);
            Commit(outgoing);
            var materialRows=AiMaterialLookup.ForAll(lines,_service.Snapshot);
            var allGrid=Table(materialRows,
                ("Số HĐ","Contract",115),("Công trình","Project",185),
                ("Đơn vị","Company",130),("Tên vật tư","Material",-1),
                ("ĐVT","Unit",70),("Số lượng","Quantity",95),
                ("Kiện","PackageNo",66),("Cont đi","SourceDate",110),
                ("Cont về","ArrivalDate",110),("Tình trạng","Status",155),
                ("Ghi chú đối chiếu","Note",230));
            allGrid.IsReadOnly=true;
            var dialogBody=new Grid();
            dialogBody.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            dialogBody.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            dialogBody.Children.Add(Card(Stack(
                T(title,17,true),
                T(lines.Count+" hợp đồng · "+materialRows.Count+" dòng vật tư / cảnh báo. Đây là dữ liệu đọc từ Excel, không phải số lượng Gemini dự đoán.",12,false,Muted))));
            Grid.SetRow(allGrid,1);
            dialogBody.Children.Add(allGrid);
            ShowDialogContent(title,dialogBody);
        }
        void OpenMotorStatuses(List<AiScheduleLine> schedules,string title)
        {
            var catalog=Production();
            var machines=new MotorHungYenStore(_service.DataDirectory)
                .List(catalog.Rows.Where(x=>x.Category=="Máy kéo"));
            var rows=schedules.SelectMany(x=>MayWarehouseAudit.MotorsFor(x,machines,_service.Slips))
                .GroupBy(x=>x.Contract+"|"+x.Project+"|"+x.Company+"|"+x.Serial+
                    "|"+x.PlannedWork).Select(g=>g.First()).ToList();
            var table=Table(rows,
                ("Hợp đồng","Contract",110),("Công trình","Project",195),
                ("Đơn vị","Company",130),("Serial","Serial",125),
                ("Loại máy","Material",175),("Tình trạng làm","WorkStatus",125),
                ("Nguồn/lịch làm (J)","FactoryNote",190),
                ("Lịch xuất","PlannedWork",150),
                ("Ngày đã xuất (K)","DispatchDate",115),
                ("Ngày bàn giao","HandoverDate",115),("Khớp nguồn","Verification",250));
            table.IsReadOnly=true;
            ShowDialogContent(title,Layout(Card(Stack(
                T(title,17,true),
                T(schedules.Count+" HĐ trong lịch · "+rows.Count+" máy kéo trong file. "+
                  "Nếu sai công trình/đơn vị hoặc thiếu serial, phải đối chiếu trước khi xác nhận xuất. "+
                  "Đã có lịch làm hoặc đã có LX KHÔNG chứng minh đã xuất thực tế.",12,false,Muted),
                T(catalog.Warnings.Count==0?"Nguồn máy kéo đã đọc.":
                    string.Join(" | ",catalog.Warnings),11,false,Muted))),table));
        }
        void OpenEquipmentLists(List<AiScheduleLine> schedules,string title)
        {
            var catalog=Production();
            var contracts=schedules.Select(x=>Rules.Contract(x.Contract)).ToHashSet(StringComparer.Ordinal);
            var rows=catalog.Rows.Where(x=>contracts.Contains(Rules.Contract(x.Contract)))
                .OrderBy(x=>x.Category).ThenBy(x=>x.Contract).ToList();
            var table=Table(rows,
                ("Nhóm","Category",103),("Hợp đồng","Contract",100),
                ("Công trình","Project",210),("Đơn vị","Company",150),
                ("Thiết bị","Material",-1),("ĐVT","Unit",66),("SL","Quantity",76),
                ("Seri","Serial",110),("Lô","Lot",115),
                ("Ngày SX","ProductionDate",105),("Mã VT","Code",90),
                ("Tên file","Source",165),("Sheet","Sheet",75),
                ("Ghi chú","Note",280));
            table.IsReadOnly=true;
            var top=Card(Stack(
                T(title,18,true),
                T(schedules.Count+" HĐ trong lịch Zalo · "+rows.Count+" dòng sản xuất khớp số HĐ.",12,true),
                T("Đây là dữ liệu làm hàng/danh sách sản xuất, KHÔNG tự nhận là đã nhập kho; các HĐ cùng số nhưng khác công trình hoặc đơn vị phải kiểm tra.",12,false,Muted),
                T(catalog.Warnings.Count==0?"Nguồn danh sách: "+(catalog.Rows.Count>0?"Đã tải 3 nhóm thiết bị đã cấu hình":"Chưa liên kết danh sách. Vào Cấu hình nguồn dữ liệu → Danh sách thiết bị để chọn 3 file Excel.")
                    :string.Join(" | ",catalog.Warnings),11,false,Muted)));
            ShowDialogContent(title,Layout(top,table));
        }
        incomingBody.Children.Add(Row(
            B("▤ Xem 3 danh sách: tủ điện / máy kéo / đầu cửa car",
                ()=>OpenEquipmentLists(doc.Arrivals,"Thiết bị của các HĐ cont dự kiến")),
            B("▤ Xem toàn bộ vật tư cont dự kiến",()=>OpenAllMaterials(doc.Arrivals,"Vật tư các hợp đồng cont dự kiến về")),
            B("⚙ Đối chiếu serial / trạng thái máy kéo",()=>OpenMotorStatuses(doc.Arrivals,"Máy kéo các HĐ cont về"))));
        incomingBody.Children.Add(arrivalMaterials);
        UpdateMaterialDetail(incoming,arrivalMaterials);

        outgoingBody.Children.Add(T("Tích ô 'Làm phiếu' của các HĐ cần xuất/bàn giao. Khi lập phiếu, HISA tự lấy số serial máy kéo từ MÁY KÉO.xlsx điền vào từng máy trên phiếu và IN KÈM serial. Mây không tự ghi ngày xuất hoặc xác nhận Đã xuất.",12,false,Muted));
        outgoingBody.Children.Add(Row(
            B("☑ Chọn tất cả HĐ xuất Hưng Yên",()=>
            {
                Commit(outgoing);
                AiScheduleBulkSelection.SetAll(doc,false,true);
                outgoing.Items.Refresh();
                saved.Text="Đã chọn "+doc.Dispatches.Count+" HĐ dự kiến xuất để đối chiếu. CHƯA lập phiếu/ghi sổ xuất.";
            },true),
            B("Bỏ tích làm phiếu",()=>{AiScheduleBulkSelection.SetAll(doc,false,false);outgoing.Items.Refresh();}),
            B("Trạng thái lịch xuất",()=>CycleSchedule(outgoing))
        ));

        var materialGroupChecks=new List<CheckBox>();
        var materialGroupChips=new WrapPanel();
        var groupSummary=T("",12,true,Brush("#177E89"));
        void SaveMaterialGroups()
        {
            _aiHandoverGroups=materialGroupChecks
                .Where(x=>x.IsChecked==true).Select(x=>x.Tag?.ToString()??"")
                .Where(x=>x.Length>0).ToList();
            groupSummary.Text=_aiHandoverGroups.Count==0
                ? "Chưa chọn nhóm vật tư · không thể lập phiếu."
                : "Đã chọn "+_aiHandoverGroups.Count+"/6 nhóm: "+
                    string.Join(", ",HandoverMaterialGroups.Options
                        .Where(x=>_aiHandoverGroups.Contains(x.Key)).Select(x=>x.Label));
        }
        foreach(var group in HandoverMaterialGroups.Options)
        {
            var checkbox=new CheckBox
            {
                Content=group.Label,Tag=group.Key,
                IsChecked=_aiHandoverGroups.Contains(group.Key),
                Margin=new Thickness(0,5,22,8),MinWidth=130,
                FontSize=13
            };
            checkbox.Checked+=(_,_)=>SaveMaterialGroups();
            checkbox.Unchecked+=(_,_)=>SaveMaterialGroups();
            materialGroupChecks.Add(checkbox);
            materialGroupChips.Children.Add(checkbox);
        }
        SaveMaterialGroups();
        var materialGroupCard=Card(Stack(
            T("CHỌN NHÓM VẬT TƯ LÊN PHIẾU",15,true),
            T("Chỉ các nhóm đã tích được đưa vào phiếu bàn giao/xuất hàng. "+
              "Không tự lấy nhóm Khác. Chọn một hoặc nhiều nhóm.",12,false,Muted),
            materialGroupChips,
            Row(B("☑ Chọn tất cả 6 nhóm",()=>{
                    foreach(var x in materialGroupChecks)x.IsChecked=true;
                    SaveMaterialGroups();
                }),
                B("Bỏ chọn 6 nhóm",()=>{
                    foreach(var x in materialGroupChecks)x.IsChecked=false;
                    SaveMaterialGroups();
                })),
            groupSummary)); 

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
                MarkWorkingSchedule();
                Notify("Đã bổ sung "+added+" dòng "+(isArrival?"cont dự kiến về":"dự kiến bàn giao")+
                    ". Kiểm tra công trình, đơn vị và ngày trước khi thao tác. Không cần lưu duyệt lịch.");
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
            AiScheduleWorkspace.RefreshManualReview(doc);
            doc.Warnings = doc.Warnings.Distinct().ToList();
            var warnings = AiScheduleReader.ValidateWithWarehouse(doc, _service.Snapshot);
            warningsBlock.Text = warnings.Count == 0
                ? "Không có cảnh báo bổ sung. Vẫn cần xác nhận với ảnh nguồn."
                : string.Join("\n", warnings.Take(60)) + (warnings.Count > 60 ? "\n... còn " + (warnings.Count - 60) + " cảnh báo." : "");
        }
        RefreshWarnings();
        var toPlan = A("✓  Lập phiếu từ hợp đồng đã tích", async () =>
        {
            Commit(outgoing);
            RefreshWarnings();
            if(!DateTime.TryParseExact(doc.DispatchDate,"yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,out _))
                throw new InvalidOperationException("Ngày dự kiến xuất không hợp lệ. Sửa ngày yyyy-MM-dd rồi lập phiếu.");
            var chosen = doc.Dispatches.Where(x => x.SelectedForHandover).ToList();
            if (chosen.Count == 0)
            {
                Notify("Tích ô 'Làm phiếu' của hợp đồng cần bàn giao ở danh sách số 2.");
                return;
            }
            var codes = chosen.Select(x => x.Contract).DistinctBy(Rules.Contract).ToArray();
            SaveMaterialGroups();
            var filter=HandoverMaterialGroups.BuildFilter(string.Join("\n",codes),_aiHandoverGroups);
            var report=_service.Search(filter);
            report=AiHandoverSelection.Build(report,doc,chosen);
            HandoverMaterialGroups.EnsureSelectedContractsHaveMaterials(report);
            if(report.Missing.Any(x=>x.Selected))
                throw new InvalidOperationException("Hợp đồng chưa có dữ liệu Tổng hợp.xlsx: "+
                    string.Join(", ",report.Missing.Where(x=>x.Selected).Select(x=>x.Contract))+
                    ". Hãy đối chiếu Excel trước khi lập phiếu.");
            _draftReport=report;
            _aiDispatchDay=doc.DispatchDate;
            _draft=_service.CreateDraft(report,
                _service.Templates.FirstOrDefault()?.Id??"internal_summary",doc.DispatchDate);
            _draft.Status="planned";
            // The filtered report yields only the selected categories. Never
            // restore unchecked groups through a full-contract material reload.
            foreach(var line in _draft.Lines)line.Include=true;
            _createPlanMode=false;
            Navigate("create");
            await Task.CompletedTask;
        }, true);
        outgoingBody.Children.Add(outgoing);
        outgoingBody.Children.Add(materialGroupCard);
        outgoingBody.Children.Add(Row(toPlan));
        outgoingBody.Children.Add(Row(
            B("▤ Xem 3 danh sách thiết bị",()=>OpenEquipmentLists(doc.Dispatches,"Thiết bị của các HĐ làm phiếu")),
            B("▤ Xem toàn bộ vật tư HĐ bàn giao",()=>OpenAllMaterials(doc.Dispatches,"Vật tư các hợp đồng làm phiếu")),
            B("🔢 Xem serial nguồn sẽ điền vào phiếu",()=>OpenMotorStatuses(doc.Dispatches,"Serial máy kéo của các HĐ dự kiến xuất"))));
        outgoingBody.Children.Add(dispatchMaterials);
        UpdateMaterialDetail(outgoing,dispatchMaterials);
        // Exactly one primary list is shown at a time.
        // This only changes UI state; nothing is saved to Excel here.
        var dispatchStep=_aiShowDispatch || doc.Arrivals.Count==0;
        if(!dispatchStep)
        {
            panel.Children.Add(T("1 · CHỌN HỢP ĐỒNG CONT VỀ HƯNG YÊN",16,true));
            panel.Children.Add(Card(incomingBody));
            panel.Children.Add(Card(Stack(
                Row(B("Tiếp theo: Lập phiếu xuất →",()=>{
                    Commit(incoming);
                    RefreshWarnings();
                    _aiShowDispatch=true;
                    ShowAiSchedules();
                },true)),
                T("Chọn HĐ cont về và đối chiếu kiện nguồn ở trên. Chuyển bước "+
                  "không tự nhập kho; bảng lập phiếu xuất hiện ở bước 2.",12,false,Muted))));
        }
        else
        {
            panel.Children.Add(T("2 · CHỌN HỢP ĐỒNG LẬP PHIẾU XUẤT",16,true));
            panel.Children.Add(Card(Stack(
                Row(B("← Quay lại chọn cont về",()=>{
                    _aiShowDispatch=false;ShowAiSchedules();
                })),
                outgoingBody)));
        }
        panel.Children.Add(new Expander
        {
            Header="Ngày dự kiến và kiểm tra cảnh báo (tùy chọn)",
            IsExpanded=false,
            Content=Card(Stack(
                Row(Field("Dự kiến nhập kho · yyyy-MM-dd",dayArrival),
                    Field("Dự kiến xuất kho · yyyy-MM-dd",dayDispatch),
                    B("Kiểm tra cảnh báo",RefreshWarnings)),
                T("Lịch dự kiến không phải xác nhận nhập/xuất. "+
                  "Chỉ xác nhận cont đã nhận hoặc phiếu Đã xuất mới ghi dữ liệu thực tế.",12,false,Muted),
                saved))
        });
        panel.Children.Add(new Expander
        {
            Header="＋ Bổ sung hợp đồng phát sinh bằng tay (tùy chọn)",
            IsExpanded=false,
            Content=manualTools,
            Margin=new Thickness(0,8,0,12)
        });
        panel.Children.Add(new Expander
        {
            Header="⚠ Cảnh báo và đối chiếu Excel · "+doc.Arrivals.Concat(doc.Dispatches).Count(x=>x.NeedsReview)+" dòng cần xem",
            IsExpanded=false,
            Content=Card(Stack(T("Nguồn: "+doc.AnalyzedBy,12,true),warningsBlock)),
            Margin=new Thickness(0,0,0,12)
        });
        SetPage("Ảnh Zalo → nhập cont / làm phiếu", "Hai danh sách độc lập · chỉ ghi khi xác nhận thực tế", Layout(top,
            new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
    }
}
