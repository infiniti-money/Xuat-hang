using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hisa.App;

public partial class MainWindow
{
    // The key is stored outside all install/data folders: re-installing HISA does not
    // touch Windows Credential Manager, and the value is never echoed into the UI.
    private FrameworkElement GeminiSettingsCard()
    {
        var credentials = new GeminiCredentialStore();
        bool saved = false;
        string? loadError = null;
        try { saved = credentials.HasKey(); }
        catch (Exception ex) { loadError = ex.Message; }

        var keyInput = new PasswordBox
        {
            Width = 410, MaxWidth = 530, MinHeight = 42,
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 14, Margin = new Thickness(0, 6, 12, 8),
            BorderBrush = Brush("#BDCDD9"),
            ToolTip = "Dán Gemini API Key mới. Khóa đã lưu không được hiển thị trên màn hình."
        };

        var status = T(loadError ?? (saved ?
            "● ĐÃ LƯU GEMINI API KEY · Sẵn sàng phân tích ảnh Zalo" :
            "○ CHƯA LƯU GEMINI API KEY · Nhập khóa bên dưới để sử dụng"), 13, true,
            loadError != null ? Brush("#AA4D39") :
            saved ? Brush("#14765C") : Brush("#A26C38"));

        var instructions = Stack(
            T("GEMINI AI  /  CẤU HÌNH KẾT NỐI", 18, true),
            T("Một lần thiết lập · Không phải nhập lại khóa mỗi ngày", 12, false, Muted),
            new Border
            {
                Background = Brush("#EEF8F6"), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14), Margin = new Thickness(0, 12, 0, 12),
                Child = status
            },
            T("Gemini API Key", 12, true),
            keyInput,
            T("Ô nhập luôn để trống để bảo mật, kể cả khi đã lưu khóa. Để thay khóa, dán khóa mới rồi bấm Lưu. Không dùng mật khẩu tài khoản Google.", 11, false, Muted)
        );

        var save = A("✓  Lưu / thay Gemini API Key", async () =>
        {
            var candidate = keyInput.Password;
            if (string.IsNullOrWhiteSpace(candidate))
                throw new InvalidOperationException("Nhập Gemini API Key mới trước khi bấm Lưu.");
            credentials.SaveKey(candidate);
            keyInput.Clear();
            status.Text = "● ĐÃ LƯU GEMINI API KEY · Sẵn sàng phân tích ảnh Zalo";
            status.Foreground = Brush("#14765C");
            Notify("Gemini API Key đã lưu trong Windows Credential Manager. Khóa được giữ lại khi cập nhật hoặc cài đè HISA trên cùng tài khoản Windows.");
            await Task.CompletedTask;
        }, true);

        var delete = A("✕  Xóa Gemini API Key", async () =>
        {
            if (!credentials.HasKey())
            {
                Notify("Chưa có Gemini API Key được lưu.");
                return;
            }
            if (!Ask("Xóa Gemini API Key đã lưu trên máy này?\n\nSau khi xóa, HISA sẽ không thể dùng Gemini cho đến khi anh lưu khóa mới. Thao tác này KHÔNG xóa dữ liệu Excel, lịch AI hoặc phiếu bàn giao."))
                return;
            credentials.DeleteKey();
            keyInput.Clear();
            status.Text = "○ ĐÃ XÓA GEMINI API KEY · Cần nhập khóa mới để phân tích ảnh";
            status.Foreground = Brush("#A26C38");
            Notify("Đã xóa Gemini API Key khỏi Windows Credential Manager. Các dữ liệu kho và lịch AI vẫn được giữ nguyên.");
            await Task.CompletedTask;
        }, false, true);

        return Card(Stack(instructions,
            Row(save, delete),
            T("BẢO MẬT & NÂNG CẤP", 12, true),
            T("Khóa được bảo vệ trong Windows Credential Manager theo tài khoản Windows hiện tại. Tắt HISA, cài bản mới hoặc nâng cấp tự động không xóa khóa. Khóa không được ghi vào Excel, JSON, file cấu hình, ZIP sao lưu hoặc bộ cài .exe.", 12, false, Muted),
            Row(B("Đến AI đọc ảnh Zalo", () => Navigate("ai-schedule")),
                B("Mở Google AI Studio", () =>
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aistudio.google.com/apikey")
                    { UseShellExecute = true })))));
    }
}
