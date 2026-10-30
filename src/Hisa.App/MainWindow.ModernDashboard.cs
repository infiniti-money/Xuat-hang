using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Hisa.App;

public partial class MainWindow
{
    private FrameworkElement DashboardWelcomeCard()
    {
        var greeting = DateTime.Now.Hour < 11 ? "Chào buổi sáng" :
                       DateTime.Now.Hour < 18 ? "Chào buổi chiều" : "Chào buổi tối";
        var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heroText.Children.Add(T("HISA  /  TRUNG TÂM ĐIỀU HÀNH", 11, true, Brush("#85E3D8")));
        heroText.Children.Add(new TextBlock
        {
            Text = greeting + ", chào mừng quay lại",
            FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            Margin = new Thickness(0, 10, 0, 8)
        });
        heroText.Children.Add(T("Kiểm soát kế hoạch xuất, chuẩn bị vật tư và bàn giao đúng lịch.",
            13, false, Brush("#D5E7F0")));
        var actions = Row(
            B("✦  Đọc lịch Zalo bằng Gemini", () => Navigate("ai-schedule")),
            B("▤  Kiểm tra xuất hàng", () => Navigate("readiness")));
        actions.Margin = new Thickness(0, 17, 0, 0);
        heroText.Children.Add(actions);

        var logo = new Border
        {
            Width = 104, Height = 104, Background = Brushes.White,
            CornerRadius = new CornerRadius(22), Padding = new Thickness(9),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Image { Source = BrandingAssets.Logo, Stretch = Stretch.Uniform }
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        layout.Children.Add(heroText);
        Grid.SetColumn(logo, 1);
        layout.Children.Add(logo);
        return new Border
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(19, 65, 83), Color.FromRgb(17, 113, 115), 0),
            Padding = new Thickness(25, 22, 24, 20),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(0, 0, 0, 18),
            Child = layout
        };
    }

    private FrameworkElement DashboardPlanCard(IReadOnlyList<DispatchPlanStatus> plans, string day)
    {
        var total = plans.Count;
        var ready = plans.Count(x => x.Status.StartsWith("Sẵn sàng", StringComparison.Ordinal));
        var shortage = plans.Count(x => x.Status == "Thiếu vật tư");
        var needsAttention = total - ready - shortage;

        var head = new DockPanel { LastChildFill = true };
        var open = B("Xem kế hoạch chi tiết", () => Navigate("readiness"));
        DockPanel.SetDock(open, Dock.Right);
        head.Children.Add(open);
        head.Children.Add(Stack(
            T("KẾ HOẠCH XUẤT  /  " + Rules.Day(day), 18, true),
            T("Vật tư đã chuẩn bị vẫn ở kho Hưng Yên; chỉ bàn giao khi anh xác nhận phiếu xuất.",
                11, false, Muted)));

        var counts = new UniformGrid { Columns = 4, Margin = new Thickness(0, 16, 0, 12) };
        counts.Children.Add(DashboardNumber("HỢP ĐỒNG", total, "#E8F2FA", "#246B9C"));
        counts.Children.Add(DashboardNumber("SẴN SÀNG", ready, "#E9F8F2", "#207B5B"));
        counts.Children.Add(DashboardNumber("BÁO THIẾU", shortage, "#FFF0E9", "#AE6142"));
        counts.Children.Add(DashboardNumber("CẦN KIỂM TRA", needsAttention, "#FFF6DE", "#976D25"));

        var body = Stack(head, counts);
        if (total == 0)
        {
            body.Children.Add(new Border
            {
                Background = Brush("#F5F9FC"),
                BorderBrush = Brush("#DCE9F2"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(17, 16, 17, 14),
                Child = Stack(
                    T("Chưa có lịch xuất dự kiến cho ngày này", 15, true),
                    T("Anh có thể đọc ảnh lịch từ Zalo hoặc nhập tay hợp đồng phát sinh. HISA không tự suy đoán lịch.",
                        12, false, Muted),
                    Row(B("Nhập ảnh Zalo", () => Navigate("ai-schedule"), true),
                        B("+ Thêm hợp đồng phát sinh", () => Navigate("readiness"))))
            });
        }
        else
        {
            var selected = plans.OrderBy(x => x.Status.StartsWith("Sẵn sàng", StringComparison.Ordinal) ? 1 : 0)
                .ThenByDescending(x => x.Plan.Urgent)
                .Take(7).ToList();
            var grid = Table(selected,
                ("Ưu tiên", "Priority", 80),
                ("Số hợp đồng", "Contract", 126),
                ("Công trình", "Project", -1),
                ("Vật tư", "MaterialCount", 70),
                ("Tình trạng chuẩn bị", "Status", 245));
            grid.Height = Math.Min(350, 48 + selected.Count * 45);
            grid.MouseDoubleClick += (_, _) => Navigate("readiness");
            body.Children.Add(grid);
            if (total > selected.Count)
                body.Children.Add(T("Đang hiển thị " + selected.Count + "/" + total +
                    " hợp đồng cần theo dõi. Mở Kế hoạch chi tiết để xem đầy đủ.", 11, false, Muted));
        }
        return StyledDashboardPanel(body);
    }

    private FrameworkElement DashboardNumber(string label, int value, string background, string accent)
    {
        var box = Stack(
            T(label, 10, true, Brush(accent)),
            new TextBlock
            {
                Text = value.ToString("N0"), FontSize = 27,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#142E42"),
                Margin = new Thickness(0, 5, 0, 0)
            });
        return new Border
        {
            Background = Brush(background), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(15, 13, 9, 13), Margin = new Thickness(0, 0, 11, 0),
            Child = box
        };
    }

    private static Border StyledDashboardPanel(UIElement content) => new()
    {
        Child = content,
        Background = Brushes.White,
        BorderBrush = Brush("#E0E9F1"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(22, 19, 22, 19),
        Margin = new Thickness(0, 0, 0, 16)
    };
}
