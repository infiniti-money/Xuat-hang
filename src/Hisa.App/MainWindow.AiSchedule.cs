using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Hisa.App;

public partial class MainWindow
{
    private string? _aiPlanDate;

    private void ShowAiSchedule()
    {
        _page = "ai-schedule";
        var root = new StackPanel { Orientation = Orientation.Vertical };
        var heading = new TextBlock
        {
            Text = "Ảnh bảng Zalo / Excel → GPT + Gemini → kiểm tra → kế hoạch chờ xuất",
            FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(heading);

        var privacy = new TextBlock
        {
            Text = "Ảnh được gửi lên nhà cung cấp AI có API key. HISA không lưu key, không tự đọc tin nhắn Zalo và không ghi sổ kho từ ảnh. Hai AI khác nhau sẽ được đối chiếu.",
            TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(privacy);

        var imagePath = new TextBox { Width = 520, IsReadOnly = true, Margin = new Thickness(0, 0, 8, 8), Height = 30 };
        var pickImage = new Button { Content = "Chọn ảnh JPG / PNG / WEBP", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 10, 8) };
        pickImage.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Chọn ảnh lịch từ Zalo", Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.webp" };
            if (picker.ShowDialog(this) == true) imagePath.Text = picker.FileName;
        };
        var select = new WrapPanel();
        select.Children.Add(pickImage);
        select.Children.Add(imagePath);
        root.Children.Add(select);

        var keyOpenAI = new PasswordBox { Width = 265, Height = 28, Margin = new Thickness(0, 0, 18, 5) };
        var keyGemini = new PasswordBox { Width = 265, Height = 28, Margin = new Thickness(0, 0, 18, 5) };
        var keys = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var a = new StackPanel();
        a.Children.Add(new TextBlock { Text = "OpenAI API key (có thể dùng biến HISA_OPENAI_API_KEY)" });
        a.Children.Add(keyOpenAI);
        var b = new StackPanel();
        b.Children.Add(new TextBlock { Text = "Gemini API key (có thể dùng biến HISA_GEMINI_API_KEY)" });
        b.Children.Add(keyGemini);
        keys.Children.Add(a);
        keys.Children.Add(b);
        root.Children.Add(keys);

        var consent = new CheckBox
        {
            Content = "Tôi cho phép gửi ảnh đã chọn tới OpenAI và/hoặc Google Gemini qua Internet để trích xuất dữ liệu.",
            Margin = new Thickness(0, 0, 0, 12), IsChecked = false
        };
        root.Children.Add(consent);

        var action = new Button { Content = "AI đọc và đối chiếu ảnh", Padding = new Thickness(15, 9, 15, 9), HorizontalAlignment = HorizontalAlignment.Left };
        var status = new TextBlock { Margin = new Thickness(0, 9, 0, 14), TextWrapping = TextWrapping.Wrap, Foreground = Muted };
        var preview = new StackPanel { Orientation = Orientation.Vertical };
        root.Children.Add(action);
        root.Children.Add(status);
        root.Children.Add(preview);

        action.Click += async (_, _) =>
        {
            if (consent.IsChecked != true)
            {
                MessageBox.Show(this, "Bạn cần xác nhận việc gửi ảnh tới nhà cung cấp AI trước khi đọc.", "Quyền riêng tư",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(imagePath.Text))
            {
                MessageBox.Show(this, "Chọn ảnh lịch Excel từ Zalo trước.", "Chưa có ảnh",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var openAIKey = keyOpenAI.Password;
            if (string.IsNullOrWhiteSpace(openAIKey)) openAIKey = Environment.GetEnvironmentVariable("HISA_OPENAI_API_KEY") ?? "";
            var geminiKey = keyGemini.Password;
            if (string.IsNullOrWhiteSpace(geminiKey)) geminiKey = Environment.GetEnvironmentVariable("HISA_GEMINI_API_KEY") ?? "";
            action.IsEnabled = false;
            preview.Children.Clear();
            status.Text = "Đang đọc hai phần nhập / xuất; chưa thay đổi dữ liệu kho…";
            try
            {
                var result = await AiScheduleReader.AnalyzeAsync(imagePath.Text, openAIKey, geminiKey);
                ShowAiSchedulePreview(preview, result);
                status.Text = $"Đã đọc bằng {result.Providers}. Hãy đối chiếu ảnh gốc và sửa trước khi tạo kế hoạch.";
            }
            catch (Exception ex)
            {
                status.Text = "Không thể đọc ảnh; dữ liệu kho không thay đổi.";
                MessageBox.Show(this, ex.Message, "Lỗi đọc ảnh AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                keyOpenAI.Clear();
                keyGemini.Clear();
                openAIKey = "";
                geminiKey = "";
                action.IsEnabled = true;
            }
        };
        SetPage("AI đọc lịch từ ảnh Zalo", "Nhập dự kiến / Xuất dự kiến · AI chỉ gợi ý, người dùng quyết định",
            new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    private void ShowAiSchedulePreview(StackPanel host, AiScheduleResult result)
    {
        var countIncoming = result.Incoming.Sum(x => x.Rows.Count);
        var urgent = result.Outgoing.Count(x => x.Urgent);
        host.Children.Add(new TextBlock
        {
            Text = $"Ngày kế hoạch: {result.Date}  |  {result.Incoming.Count} container / {countIncoming} dòng nhập dự kiến  |  {result.Outgoing.Count} dòng xuất dự kiến  |  {urgent} hợp đồng cần gấp cũng có trong lịch xuất",
            FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12)
        });
        if (result.Warnings.Count > 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "CẦN KIỂM TRA: " + string.Join("   •   ", result.Warnings),
                Foreground = Brush("#B54708"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }
        host.Children.Add(new TextBlock
        {
            Text = "PHẦN 1 — CONT DỰ KIẾN VỀ (chỉ đối chiếu, chưa xác nhận nhập kho)",
            FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 8)
        });
        var incoming = result.Incoming.SelectMany(cont => cont.Rows.Select(row => new
        {
            Container = cont.Container, SourceDate = cont.SourceDate, row.Contract,
            row.Project, row.Company, row.Note, Urgent = row.Urgent ? "CẦN GẤP" : ""
        })).ToArray();
        var incomingGrid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, ItemsSource = incoming, Height = 225,
            CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single
        };
        incomingGrid.Columns.Add(AiColumn("Container", "Container", 135));
        incomingGrid.Columns.Add(AiColumn("Ngày xuất ở nguồn", "SourceDate", 135));
        incomingGrid.Columns.Add(AiColumn("Số HĐ", "Contract", 105));
        incomingGrid.Columns.Add(AiColumn("Công trình", "Project", 360));
        incomingGrid.Columns.Add(AiColumn("Đơn vị", "Company", 145));
        incomingGrid.Columns.Add(AiColumn("Ghi chú", "Note", 220));
        incomingGrid.Columns.Add(AiColumn("Ưu tiên", "Urgent", 90));
        host.Children.Add(incomingGrid);

        host.Children.Add(new TextBlock
        {
            Text = "PHẦN 2 — HÀNG DỰ KIẾN XUẤT (có thể sửa số HĐ, công trình, công ty, ghi chú và lựa chọn)",
            FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 8)
        });
        var outgoing = new ObservableCollection<AiOutgoingRow>(result.Outgoing);
        foreach (var row in outgoing) row.Selected = row.HasSchedule && !string.IsNullOrEmpty(Rules.Contract(row.Contract));
        var outgoingGrid = new DataGrid
        {
            AutoGenerateColumns = false, ItemsSource = outgoing, Height = 290,
            CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single
        };
        outgoingGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Lập kế hoạch", Binding = new Binding(nameof(AiOutgoingRow.Selected))
            { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 112
        });
        outgoingGrid.Columns.Add(AiColumn("Số HĐ", nameof(AiOutgoingRow.Contract), 110, false));
        outgoingGrid.Columns.Add(AiColumn("Công trình", nameof(AiOutgoingRow.Project), 355, false));
        outgoingGrid.Columns.Add(AiColumn("Công ty", nameof(AiOutgoingRow.Company), 140, false));
        outgoingGrid.Columns.Add(AiColumn("Ghi chú gốc", nameof(AiOutgoingRow.Note), 145, false));
        outgoingGrid.Columns.Add(AiColumn("Trạng thái", nameof(AiOutgoingRow.ScheduleLabel), 190));
        outgoingGrid.Columns.Add(AiColumn("Ưu tiên", nameof(AiOutgoingRow.UrgentLabel), 85));
        host.Children.Add(outgoingGrid);
        host.Children.Add(new TextBlock
        {
            Text = "“Đã có lx” = ĐÃ CÓ LỊCH XUẤT, không phải đã có lái xe. Dòng trống = chưa rõ trạng thái lịch xuất. Chỉ các dòng có dấu lịch xuất được tích sẵn; bạn có thể sửa lựa chọn.",
            Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10)
        });

        var buttons = new WrapPanel { Margin = new Thickness(0, 3, 0, 10) };
        var selectAll = new Button { Content = "Chọn toàn bộ lịch dự kiến", Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(0, 0, 10, 0) };
        selectAll.Click += (_, _) =>
        {
            outgoingGrid.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (var row in outgoing) row.Selected = !string.IsNullOrWhiteSpace(row.Contract);
            outgoingGrid.Items.Refresh();
        };
        var clear = new Button { Content = "Bỏ chọn", Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(0, 0, 10, 0) };
        clear.Click += (_, _) => { foreach (var row in outgoing) row.Selected = false; outgoingGrid.Items.Refresh(); };
        var handoff = new Button
        {
            Content = "Chuyển mục đã chọn sang KẾ HOẠCH CHỜ XUẤT",
            Padding = new Thickness(15, 8, 15, 8)
        };
        handoff.Click += (_, _) =>
        {
            outgoingGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            outgoingGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var selected = outgoing.Where(x => x.Selected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Hãy chọn ít nhất một công trình xuất dự kiến.", "Chưa chọn",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (selected.Any(x => string.IsNullOrWhiteSpace(x.Contract)))
            {
                MessageBox.Show(this, "Có dòng chưa có số hợp đồng. Hãy sửa lại theo ảnh gốc.", "Thiếu số hợp đồng",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var review = result.Warnings.Count == 0 ? "" :
                "\n\nCảnh báo hai AI chưa nhất trí:\n" + string.Join("\n", result.Warnings);
            if (!Ask($"Chuyển {selected.Count} hợp đồng dự kiến xuất ngày {result.Date} sang màn hình lập phiếu CHỜ XUẤT?\n" +
                    "Ứng dụng sẽ đối chiếu dữ liệu gốc và yêu cầu xác nhận lần nữa trước khi lưu. Không ghi nhận ĐÃ XUẤT." + review))
                return;
            var filter = new SearchFilter { ContractsText = string.Join("\n", selected.Select(x => x.Contract).DistinctBy(Rules.Contract)) };
            var report = _service.Search(filter);
            foreach (var row in report.Rows.Concat(report.NoMaterialMatch))
            {
                var match = selected.Where(x => Rules.Contract(x.Contract) == Rules.Contract(row.Contract)).ToArray();
                row.Selected = match.Length == 1 && (string.IsNullOrWhiteSpace(match[0].Project) ||
                    Rules.ProjectCode(match[0].Project) == Rules.ProjectCode(row.Project));
            }
            foreach (var row in report.Missing) row.Selected = false;
            _draft = null;
            _draftReport = report;
            _aiPlanDate = result.Date;
            _createPlanMode = true;
            Navigate("plan");
        };
        buttons.Children.Add(selectAll);
        buttons.Children.Add(clear);
        buttons.Children.Add(handoff);
        host.Children.Add(buttons);
    }

    private static DataGridTextColumn AiColumn(string header, string property, double width, bool readOnly = true) => new()
    {
        Header = header, Binding = new Binding(property) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width, IsReadOnly = readOnly
    };
}
