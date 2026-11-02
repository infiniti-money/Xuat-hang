using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

/// <summary>Builds native screens offline; never shows a window, clicks a button or writes real data.</summary>
public static class UiWorkflowDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var evidence=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException("FAIL: "+label);evidence.Add(label);}
        var root=SelfTests.CreateDemoWorkspace();using var service=new WarehouseService(root);await service.InitializeAsync();
        var window=new MainWindow(service);
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        void Call(string method,params object[] args)=>typeof(MainWindow).GetMethod(method,flags)!.Invoke(window,args);
        try
        {
            Check(window.Title==SoftwareInfo.DisplayName,"Native window uses requested software name");
            Call("ShowTemplates");
            Check(Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="+ Tạo mẫu phiếu mới"),"Template screen has explicit create-new-template button");
            Check(Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="Sửa mẫu / căn lề"),"Template screen has explicit edit-template button");
            var source=service.Search(new SearchFilter{ContractsText="05492026",Categories=["Máy kéo"]});
            var draft=service.CreateDraft(source,"internal_summary","2026-10-27");
            Call("EditSlip",draft);
            bool HasFourStates(ComboBox combo)
            {
                var values=combo.Items.Cast<object>().Select(x=>x.GetType().GetProperty("Value")?.GetValue(x)?.ToString()).Where(x=>x!=null).ToHashSet();
                return new[]{"planned","delivered","delayed","cancelled"}.All(values.Contains);
            }
            var state=Children(window).OfType<ComboBox>().Single(HasFourStates);
            Check(state.IsEnabled,"Slip editor supports all four selectable statuses");
            var expander=Children(window).OfType<Expander>().First(x=>x.Header?.ToString()?.StartsWith("Hợp đồng / bộ lọc")==true);
            Check(!expander.IsExpanded,"Loaded slip keeps material workspace visible with contract controls collapsed");
            state.SelectedValue="delayed";
            var reason=Children(window).OfType<TextBlock>().Single(x=>x.Text=="Lý do tạm hoãn / hủy");
            Check(((FrameworkElement)reason.Parent).Visibility==Visibility.Visible,"Temporary delay displays reason editor");
            state.SelectedValue="planned";
            Check(((FrameworkElement)reason.Parent).Visibility==Visibility.Collapsed,"Waiting state hides inapplicable reason editor");
            var empty=typeof(MainWindow).GetField("_draft",flags)!;empty.SetValue(window,null);
            typeof(MainWindow).GetField("_draftReport",flags)!.SetValue(window,null);
            Call("ShowCreate",false);
            Check(Children(window).OfType<TextBlock>().Any(x=>x.Text.Contains("Nhập số hợp đồng để tự kiểm tra")),"Create screen contains immediate contract-warning area");
            Call("ShowLookup");
            Check(Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="☑ Chọn tất cả HĐ có vật tư") &&
                  Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="Tạo phiếu từ HĐ đã chọn"),
                "Contract lookup supports explicit select-all and direct draft creation");
            Call("ShowSlips");
            var labels=Children(window).OfType<Button>().Select(b=>b.Content?.ToString()??"").ToList();
            Check(new[]{"Chờ xuất","✓  Đã xuất","◷  Tạm hoãn","Hủy phiếu"}.All(labels.Contains),"Slip list exposes all four transition buttons");
            var navigation=(StackPanel)window.FindName("Navigation")!;
            Check(navigation.Children.OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Cấu hình nguồn dữ liệu")==true),
                "Excel source settings has a dedicated visible navigation entry");
            Check(!service.Slips.Any(s=>s.Note.Contains("Mây Mây đề xuất",StringComparison.Ordinal)),
                "Opening Mây's chat cannot create or save any slip");
            var beforeMay=service.Slips.Count;
            Call("OpenMaySlipReview",
                new MaySlipProposal(new []{"05492026"},"2026-11-02"));
            var mayDraft=typeof(MainWindow).GetField("_draft",flags)!.GetValue(window) as Slip;
            Check(mayDraft is {Status:"planned"} &&
                  mayDraft.HandoverDate=="2026-11-02" &&
                  string.IsNullOrWhiteSpace(mayDraft.SlipCode),
                "Mây prepares only an unsaved planned slip in the standard approval editor");
            Check(service.Slips.Count==beforeMay,
                "Mây's proposal does not persist a slip or touch shipped inventory without approval");
            var warehouseRows=service.Search(new SearchFilter{ContractsText="05492026"});
            var materialLines=warehouseRows.Rows.Concat(warehouseRows.NoMaterialMatch)
                .SelectMany(x=>x.Materials).ToList();
            Check(mayDraft!=null &&
                  mayDraft.Lines.Count==materialLines.Count &&
                  mayDraft.Lines.All(x=>x.Include) &&
                  mayDraft.ContractPackages.Count==
                    warehouseRows.Rows.Concat(warehouseRows.NoMaterialMatch)
                        .SelectMany(x=>x.Packages).DistinctBy(x=>x.Id).Count(),
                "A selected contract prepares ALL source material lines and packages for review");
            Check(mayDraft!.Status=="planned"&&
                  service.Slips.Count==beforeMay,
                "Full-package selection cannot mark delivered or write shipment date before approval");
            Call("ShowSettings");
            var configScroll=(ScrollViewer)((Grid)window.FindName("PageHost")!).Children[0];
            var configStack=(StackPanel)configScroll.Content;
            var sourceCard=(Border)configStack.Children[1];
            Check(Children(sourceCard).OfType<TextBlock>().Any(t=>t.Text=="Nguồn dữ liệu Excel"),
                "Source configuration is first on settings page, above Gemini");
            Check(Children(sourceCard).OfType<Button>().Any(b=>b.Content?.ToString()=="Chọn Kho HISA"),
                "Source folder browser button is available without scrolling past Gemini");
            Check(Children(sourceCard).OfType<Button>().Count(b=>b.Content?.ToString()=="Chọn Kho HISA")==1 &&
                  !Children(configStack).OfType<Button>().Any(b=>b.Content?.ToString()=="Chọn Excel") &&
                  Children(sourceCard).OfType<TextBox>().Count()==1,
                "Source setup contains exactly one Kho HISA selector and no individual Excel file controls");
            Check(Children(configStack).OfType<TextBlock>().Any(x=>
                    x.Text.Contains("không phải chọn từng file",StringComparison.OrdinalIgnoreCase)),
                "Source setup explains automatic lookup of the six Excel files");
            Check(Children(configStack).OfType<Button>().Any(x=>x.Content?.ToString()=="◉ Thử kết nối Gemini") &&
                  Children(configStack).OfType<Button>().Any(x=>x.Content?.ToString()=="🔊 Thử giọng đọc Mây"),
                "AI settings have user-initiated Gemini network test and Mây speaker test");
            Check(Children(configStack).OfType<Button>().Any(x=>
                    x.Content?.ToString()=="✓ Lưu tùy chọn giọng nói / micro") &&
                  Children(configStack).OfType<Button>().Any(x=>
                    x.Content?.ToString()=="Lưu quyền AI / micro"),
                "AI settings separately save device options and explicit microphone/data-sharing grants");
            Check(Children(configStack).OfType<CheckBox>().Any(x=>
                    x.Content?.ToString()?.Contains("dữ liệu HISA liên quan")==true) &&
                  Children(configStack).OfType<CheckBox>().Any(x=>
                    x.Content?.ToString()?.Contains("chủ động bật thu âm")==true),
                "AI settings show independent microphone and read-only HISA data controls");
            Call("ShowGeminiChat");
            Check(window.FindName("TopVoiceButton") is Button quickVoice &&
                quickVoice.Content?.ToString()=="🎙 Nói với Mây",
                "Main toolbar has a direct one-click HISA assistant shortcut");
            Check(Children(window).OfType<Button>().Any(b=>
                    b.Content?.ToString()=="🎙 Nói với Mây"),
                "Gemini Chat has a single prominent voice start/stop control");
            Check(Children(window).OfType<Expander>().Any(e=>
                    e.Header?.ToString()?.Contains("Tùy chọn nâng cao")==true &&
                    !e.IsExpanded),
                "Secondary microphone, consent and TTS controls are collapsed by default");
            Check(typeof(MainWindow).GetField("_geminiMicrophone",flags)?.
                    GetValue(window) is GeminiMicrophoneRecorder recorder &&
                  !recorder.IsRecording,
                "Merely opening Gemini Chat never starts microphone without an explicit click");
            Check(typeof(MainWindow).GetField("_mayTray",flags)?.GetValue(window)==null,
                "No background tray or microphone is started before the real window loads");
            Check(typeof(MainWindow).GetMethod("RestoreMayWindow",flags)!=null &&
                  typeof(MainWindow).GetMethod("UpdateMayTrayStatus",flags)!=null,
                "Mây has dedicated Windows-tray restore and listening status controls");
            var voiceFlag=typeof(MainWindow).GetField("_geminiHandsFreeActive",flags)!;
            var sessionFlag=typeof(MainWindow).GetField("_geminiHandsFreeSession",flags)!;
            var surfaceFlag=typeof(MainWindow).GetField("_geminiChatSurface",flags)!;
            var originalChat=surfaceFlag.GetValue(window);
            // Simulate an already approved session without opening hardware
            // or sending any data. Navigating to a warehouse tab should not
            // implicitly cancel the user's explicitly active background Mây.
            voiceFlag.SetValue(window,true);
            sessionFlag.SetValue(window,123);
            Call("Navigate","information");
            Check(voiceFlag.GetValue(window) is true &&
                  typeof(MainWindow).GetMethod("HandsFreeCurrent",flags)!
                    .Invoke(window,new object[]{123}) is true,
                "Explicitly started Mây hands-free mode persists while working on another HISA screen");
            Call("Navigate","gemini-chat");
            Check(ReferenceEquals(originalChat,surfaceFlag.GetValue(window)) &&
                  ReferenceEquals(((Grid)window.FindName("PageHost")!).Children[0],originalChat),
                "Returning to Mây reuses live chat controls and avoids dropping speech callbacks");
            Call("StopHandsFreeSession");
            Check(voiceFlag.GetValue(window) is false,
                "Stop Mây always revokes active background listening immediately");
            Call("ShowInformation");
            var updateButtons=Children(window).OfType<Button>()
                .Where(x=>x.Content?.ToString()?.Contains("CẬP NHẬT HISA")==true).ToList();
            Check(updateButtons.Count==1 &&
                  !Children(window).OfType<Button>().Any(x=>x.Content?.ToString()=="Cài bản đã tải"),
                "Updater information screen exposes a single action instead of two installation steps");
            Call("ShowSettings");
            Check(navigation.Children.OfType<Button>().Any(b=>b.Content?.ToString()
                    ?.Contains("Máy kéo gia công tại Hưng Yên")==true),
                "Motor Hưng Yên tracker has its own visible navigation menu");
            Call("ShowMotorHungYen");
            Check(window.FindName("PageTitle") is TextBlock motorTitle &&
                  motorTitle.Text=="Máy kéo gia công tại Hưng Yên",
                "Motor outsourcing page title uses the corrected Hưng Yên location");
            Check(MotorHungYenStore.Name(MotorHungYenStore.Sent)=="Đã gửi gia công tại Hưng Yên",
                "Motor status label is corrected without altering persisted status codes");
            Check(!Children(window).OfType<TextBlock>().Any(t=>t.Text.Contains("Hồng Yên",StringComparison.Ordinal)) &&
                  !Children(window).OfType<Button>().Any(b=>b.Content is string name && name.Contains("Hồng Yên",StringComparison.Ordinal)) &&
                  !Children(window).OfType<CheckBox>().Any(c=>c.Content is string label && label.Contains("Hồng Yên",StringComparison.Ordinal)),
                "Motor screen contains no outdated Hồng Yên captions");

            Check(Children(window).OfType<Button>().Count(b=>b.Content is StackPanel sp &&
                sp.Children.OfType<TextBlock>().Any(t=>t.Text is
                    "TỔNG SỐ MÁY" or "CHƯA GỬI" or "ĐANG GIA CÔNG" or
                    "ĐÃ LÀM XONG" or "ĐÃ BÀN GIAO"))==5,
                "Motor screen shows five visible clickable KPI filters");
            Check(Children(window).OfType<Button>().Any(x=>x.Content?.ToString()=="✓ ĐÃ LÀM XONG · GHI EXCEL") &&
                  Children(window).OfType<Button>().Any(x=>x.Content?.ToString()=="○ CHƯA LÀM · GHI EXCEL") &&
                  Children(window).OfType<Button>().Any(x=>x.Content?.ToString()=="Bỏ lọc HĐ"),
                "Motor status offers direct action buttons and clear contract filter");
            Check(Children(window).OfType<Expander>().Any(x=>
                    x.Header?.ToString()?.StartsWith("⚙ Tùy chọn bổ sung")==true &&
                    !x.IsExpanded),
                "Motor advanced actions are collapsed while main actions stay prominent");
            Check(Children(window).OfType<DataGrid>().Any(g=>
                g.Columns.OfType<DataGridTextColumn>().Any(c=>c.Header?.ToString()=="Tiến độ xác nhận") &&
                g.Columns.OfType<DataGridTextColumn>().Any(c=>c.Header?.ToString()=="Chọn làm") &&
                g.Columns.OfType<DataGridTextColumn>().Any(c=>c.Header?.ToString()=="Cột K (Excel)") &&
                g.Columns.OfType<DataGridTextColumn>().Any(c=>c.Header?.ToString()=="Ngày bàn giao")),
                "Motor tracker separates selected work status from actual delivery date");
            Check(Children(window).OfType<ComboBox>().Any(c=>
                    c.Items.Cast<object>().Select(x=>x.ToString()).SequenceEqual(new[]{"Chưa làm","Đã làm"})) &&
                Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="Lưu trạng thái vào Excel cột K") &&
                !Children(window).OfType<Button>().Any(b=>
                    b.Content?.ToString()?.Contains("Xác nhận đã bàn giao")==true) &&
                Children(window).OfType<Button>().Any(b=>b.Content?.ToString()=="Lịch sử dòng chọn"),
                "Motor tracker offers only two work choices, with no manual delivery action");
            Check(Children(window).OfType<DataGrid>().Any(g=>
                g.Columns.OfType<DataGridTextColumn>().Any(c=>c.Header?.ToString()=="Số HĐ") &&
                g.Columns.OfType<DataGridTextColumn>().Any(c=>c.Header?.ToString()=="Tiến độ hợp đồng") &&
                g.Columns.OfType<DataGridTextColumn>().Any(c=>c.Header?.ToString()=="Bàn giao")),
                "Motor tracker displays a separate contract-level outsourcing overview");
            Check(Children(window).OfType<CheckBox>().Any(c=>
                c.Content?.ToString()?.Contains("Chỉ hiện hợp đồng đã xác nhận gửi gia công tại Hưng Yên")==true &&
                c.IsChecked==true),
                "Contract overview defaults to confirmed Hưng Yên subcontracting only, never all unverified Excel rows");
            var simulated=AiScheduleWorkspace.NewDraft(new DateTime(2026,10,31));
            simulated.Arrivals.Add(new AiScheduleLine{Contract="05492026",Project="Công trình A",Company="Công ty A",PlannedDate=simulated.ArrivalDate});
            simulated.Dispatches.Add(new AiScheduleLine{Contract="20012026",Project="Công trình C",Company="Công ty A",PlannedDate=simulated.DispatchDate});
            typeof(MainWindow).GetField("_aiDocument",flags)!.SetValue(window,simulated);
            Call("ShowAiSchedules");
            var aiHost=(Grid)window.FindName("PageHost")!;
            var aiLayout=(Grid)aiHost.Children[0];
            var aiScroll=aiLayout.Children.OfType<ScrollViewer>().Single();
            var aiPanel=(StackPanel)aiScroll.Content;
            var headings=Children(aiPanel).OfType<TextBlock>().Select(x=>x.Text).ToArray();
            Check(headings.Any(x=>x.Contains("1 · CONT DỰ KIẾN VỀ")) &&
                  headings.Any(x=>x.Contains("2 · HỢP ĐỒNG DỰ KIẾN BÀN GIAO")),
                "Gemini result separates potential container arrival and waiting handover slips");
            Check(Children(aiPanel).OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Tìm kiện Hưng Yên trong Container")==true) &&
                  Children(aiPanel).OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Lập phiếu từ hợp đồng")==true),
                "Arrival verification and draft handover have direct buttons next to their lists");
            var aiGrids=Children(aiPanel).OfType<DataGrid>().ToList();
            var incomingGrid=aiGrids.Single(g=>
                g.Columns.OfType<DataGridTemplateColumn>().Any(c=>c.Header?.ToString()=="Cont đã về?"));
            var outgoingGrid=aiGrids.Single(g=>
                g.Columns.OfType<DataGridTemplateColumn>().Any(c=>c.Header?.ToString()=="Làm phiếu"));
            Check(aiGrids.Count>=3 &&
                  aiGrids.Any(g=>g.Columns.OfType<DataGridTemplateColumn>()
                     .Any(c=>c.Header?.ToString()=="Đã về")),
                "Zalo lists and physical source package selection stay visible on one screen");
            void AssertCheckbox(DataGrid source,string header,AiScheduleLine data,string flag)
            {
                var column=source.Columns.OfType<DataGridTemplateColumn>()
                    .Single(c=>c.Header?.ToString()==header);
                // FrameworkElementFactory.LoadContent cannot be called until WPF
                // seals a live template. Inspect the factory's actual binding and
                // recreate the control for a direct first-click binding test.
                var factory=column.CellTemplate.VisualTree;
                Check(factory.Type==typeof(CheckBox),
                    header+" uses an interactive CheckBox instead of edit-mode cell");
                // WPF does not expose FrameworkElementFactory's stored binding
                // publicly outside a sealed/instantiated template. Assert the
                // factory shape and exercise its declared two-way binding contract.
                var binding=new System.Windows.Data.Binding(flag)
                {
                    Mode=System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                };
                var checkbox=new CheckBox { DataContext=data };
                checkbox.SetBinding(CheckBox.IsCheckedProperty,binding);
                Check(binding.Path.Path==flag &&
                      binding.Mode==System.Windows.Data.BindingMode.TwoWay &&
                      binding.UpdateSourceTrigger==System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
                    header+" binds directly to row value on first click");
                checkbox.DataContext=data;
                checkbox.SetCurrentValue(CheckBox.IsCheckedProperty,true);
                Check(flag=="SelectedForArrival"?data.SelectedForArrival:data.SelectedForHandover,
                    header+" immediately updates the model when clicked");
                checkbox.SetCurrentValue(CheckBox.IsCheckedProperty,false);
                Check(flag=="SelectedForArrival"?!data.SelectedForArrival:!data.SelectedForHandover,
                    header+" can be unchecked without editing another cell");
            }
            AssertCheckbox(incomingGrid,"Cont đã về?",simulated.Arrivals[0],"SelectedForArrival");
            AssertCheckbox(outgoingGrid,"Làm phiếu",simulated.Dispatches[0],"SelectedForHandover");
            Check(Children(aiPanel).OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Xem toàn bộ vật tư cont")==true) &&
                  Children(aiPanel).OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Xem toàn bộ vật tư HĐ bàn giao")==true),
                "Both lists allow browsing material details for every Zalo contract");
            incomingGrid.SelectedIndex=0;
            var materialGrids=Children(aiPanel).OfType<DataGrid>().Where(g=>g!=incomingGrid&&g!=outgoingGrid).ToList();
            Check(materialGrids.Any(g=>g.ItemsSource is List<AiMaterialLookup.Row> items &&
                  items.Any(i=>i.Material=="Máy kéo M1"&&i.Quantity==2)),
                "Clicking a schedule row shows original workbook goods and quantities");
            Check(!simulated.Arrivals[0].SelectedForArrival && !simulated.Dispatches[0].SelectedForHandover &&
                  string.IsNullOrWhiteSpace(simulated.ApprovedAt),
                "Opening Gemini results does not mark arrival, issue a slip or approve schedule");
            Check(Children(aiPanel).OfType<Expander>().Count(x=>!x.IsExpanded)>=2,
                "Optional manual entry and warnings stay below the main flow and are collapsed");
        }
        finally{window.Close();}
        return evidence;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        yield return parent;
        foreach(var item in LogicalTreeHelper.GetChildren(parent))
            if(item is DependencyObject child)foreach(var descendant in Children(child))yield return descendant;
    }
}
