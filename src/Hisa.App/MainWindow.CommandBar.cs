using System.Net.Http;
using System.Windows;

namespace Hisa.App;

public partial class MainWindow
{
    private bool _checkingUpdate;

    private async void OpenUpdateClick(object sender,RoutedEventArgs e) =>
        await CheckDownloadAndOfferUpdateAsync();

    /// <summary>
    /// User-triggered check/download, without disabling warehouse UI.
    /// The verified installer is staged in Temp and only launched after explicit
    /// approval and the application has released Excel files on exit.
    /// </summary>
    private async Task CheckDownloadAndOfferUpdateAsync()
    {
        if(_checkingUpdate)return;
        _checkingUpdate=true;
        TopUpdateButton.IsEnabled=false;
        try
        {
            string? version=AutoUpdater.ReadyVersion;
            if(version==null)
            {
                TopUpdateButton.Content="↻ Đang kiểm tra...";
                StatusText.Text="Đang kiểm tra GitHub Releases và tải bản mới trong nền nếu có...";
                version=await AutoUpdater.CheckAndStageAsync();
            }
            if(version==null)
            {
                // A one-click check with no newer release never interrupts work
                // with a modal "up to date" dialog.
                StatusText.Text="✓ HISA "+SoftwareInfo.Version+" đã là bản mới nhất.";
                return;
            }
            TopUpdateButton.Content="✓ Đã tải "+version;
            StatusText.Text="Đã tải bản "+version+" và xác minh SHA-256. Chưa đóng ứng dụng.";
            if(MessageBox.Show(this,
                "HISA đã tải và xác minh phiên bản "+version+".\n\n"+
                "Bạn muốn đóng HISA ngay để cài đặt bản mới?\n"+
                "Hãy lưu và đóng các file Excel đang mở. Dữ liệu kho và Gemini API Key được giữ nguyên.\n"+
                "Nếu chọn Không, bạn có thể tiếp tục làm việc và cài bản đã tải sau.",
                "HISA · Đã sẵn sàng cập nhật",
                MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)
                return;
            if(!AutoUpdater.RequestInstallOnExit())
            {
                MessageBox.Show(this,"Bản tải nền không còn tồn tại hoặc chưa xác minh. Hãy chọn Cập nhật lại.",
                    "HISA · Cập nhật",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            // Do not launch the installer until OnExit has closed warehouse workbooks.
            Close();
        }
        catch(OperationCanceledException)
        {
            StatusText.Text="Đã hủy kiểm tra bản cập nhật.";
        }
        catch(HttpRequestException ex)
        {
            StatusText.Text="Không thể kết nối GitHub để cập nhật.";
            MessageBox.Show(this,"Không kiểm tra hoặc tải được bản mới: "+ex.Message,
                "HISA · Cập nhật",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        catch(Exception ex)
        {
            StatusText.Text="Cập nhật thất bại; HISA và dữ liệu kho vẫn được giữ nguyên.";
            MessageBox.Show(this,"Không thể tải hoặc xác minh bản cập nhật: "+ex.Message,
                "HISA · Cập nhật",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        finally
        {
            _checkingUpdate=false;
            TopUpdateButton.IsEnabled=true;
            TopUpdateButton.Content=AutoUpdater.ReadyVersion is string ready?
                "✓ Cài bản "+ready:"↑ Cập nhật";
        }
    }

    private void OpenQuickVoiceClick(object sender,RoutedEventArgs e)
    {
        // Exactly one user click opens chat and toggles the microphone.
        // Never auto-start microphone at application launch or page navigation.
        if(_page!="gemini-chat")Navigate("gemini-chat");
        _geminiHandsFreeToggle?.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Button.ClickEvent));
    }

    private void OpenAiClick(object sender,RoutedEventArgs e)=>Navigate("ai-schedule");
    private void OpenReadinessClick(object sender,RoutedEventArgs e)=>Navigate("readiness");
}
