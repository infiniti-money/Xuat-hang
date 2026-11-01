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

        var connectionResult=T("Chưa chạy kiểm tra kết nối. Chỉ kiểm tra khi bạn chủ động bấm nút.",12,false,Muted);
        var testConnection=A("◉ Thử kết nối Gemini",async()=>
        {
            connectionResult.Text="Đang kiểm tra kết nối bằng một câu hỏi ngắn (có sử dụng API)...";
            connectionResult.Foreground=Muted;
            try
            {
                var key=credentials.ReadKey();
                if(string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Chưa có Gemini API Key. Lưu khóa trước khi kiểm tra.");
                var result=await GeminiChatService.AskAsync(key,[],
                    "Mây ơi, trả lời đúng một câu ngắn để kiểm tra kết nối tiếng Việt.",
                    null);
                connectionResult.Text="✓ KẾT NỐI THÀNH CÔNG · "+
                    result[..Math.Min(result.Length,130)];
                connectionResult.Foreground=Brush("#14765C");
            }
            catch(Exception e)
            {
                connectionResult.Text="⚠ Kết nối không thành công: "+e.Message;
                connectionResult.Foreground=Brush("#A64D39");
            }
        },true);

        var preferences=MayAssistantPreferences.Read();
        var microphones=GeminiMicrophoneRecorder.AvailableMicrophones();
        var inputMic=new ComboBox{
            Width=300,ItemsSource=microphones,
            DisplayMemberPath=nameof(MicrophoneSource.Label),
            SelectedValuePath=nameof(MicrophoneSource.Number)
        };
        inputMic.SelectedValue=microphones.Any(x=>x.Number==preferences.MicrophoneNumber)
            ?preferences.MicrophoneNumber:-1;
        var sensitivity=new ComboBox{
            Width=200,DisplayMemberPath="Label",SelectedValuePath="Value",
            ItemsSource=new[]{
                new {Label="Cao · mic xa",Value=0.002},
                new {Label="Vừa · khuyên dùng",Value=0.0045},
                new {Label="Thấp · môi trường ồn",Value=0.010}
            },
            SelectedValue=preferences.Sensitivity
        };
        var speakAutomatically=new CheckBox{
            Content="Mây tự đọc câu trả lời qua loa (khi không dùng rảnh tay)",
            IsChecked=preferences.AutoSpeak
        };
        var share=new CheckBox{
            Content="Cho phép gửi tóm tắt dữ liệu HISA liên quan khi hỏi Gemini",
            IsChecked=GeminiWarehouseSharingConsent.Read()
        };
        var micPermission=new CheckBox{
            Content="Cho phép HISA dùng micro khi tôi chủ động bật thu âm/rảnh tay",
            IsChecked=GeminiVoiceConsentStore.Read()
        };
        var preferenceStatus=T("✓ Tùy chọn được lưu trên máy, không chứa API Key hoặc quyền truy cập tự động.",12,false,Muted);
        var savePreferences=B("✓ Lưu tùy chọn giọng nói / micro",()=>
        {
            try
            {
                var next=new MayAssistantPreferences{
                    AutoSpeak=speakAutomatically.IsChecked==true,
                    MicrophoneNumber=inputMic.SelectedValue is int number?number:-1,
                    Sensitivity=sensitivity.SelectedValue is double v?v:0.0045
                };
                next.Save();
                _geminiChatAutoSpeak=next.AutoSpeak;
                if(_geminiHandsFreeActive)
                    StopHandsFreeSession();
                preferenceStatus.Text="✓ Đã lưu tùy chọn. Nếu Mây đang nghe, micro đã dừng để áp dụng thiết bị mới. Bấm Nói với Mây để tiếp tục.";
                preferenceStatus.Foreground=Brush("#14765C");
            }
            catch(Exception e)
            {
                preferenceStatus.Text="Không lưu được tùy chọn: "+e.Message;
                preferenceStatus.Foreground=Brush("#AA4D39");
            }
        },true);
        var privacyStatus=T("Micro và chia sẻ dữ liệu là hai quyền độc lập; thu hồi một quyền không tự bật lại quyền kia.",12,false,Muted);
        void UpdatePrivacy()
        {
            share.IsChecked=GeminiWarehouseSharingConsent.Read();
            micPermission.IsChecked=GeminiVoiceConsentStore.Read();
            privacyStatus.Text="✓ Quyền hiện tại · dữ liệu kho: "+
                (share.IsChecked==true?"ĐÃ BẬT":"ĐÃ TẮT")+
                " · micro: "+(micPermission.IsChecked==true?"ĐÃ BẬT":"ĐÃ TẮT");
        }
        var savePrivacy=B("Lưu quyền AI / micro",()=>
        {
            try
            {
                var newWarehouse=share.IsChecked==true;
                var newMicrophone=micPermission.IsChecked==true;
                var oldWarehouse=GeminiWarehouseSharingConsent.Read();
                var oldMicrophone=GeminiVoiceConsentStore.Read();
                if((newWarehouse&&!oldWarehouse||newMicrophone&&!oldMicrophone) &&
                   !Ask("Xác nhận cấp quyền cho Mây?\n\n"+
                       (newWarehouse&&!oldWarehouse?
                           "• Dữ liệu kho: khi bạn gửi câu hỏi, HISA được gửi bản tóm tắt giới hạn tới Google Gemini. Không gửi file Excel gốc.\n":"")+
                       (newMicrophone&&!oldMicrophone?
                           "• Micro: khi bạn chủ động bấm thu âm/rảnh tay, âm thanh được gửi tới Google Gemini để nhận dạng tiếng Việt.\n":"")+
                       "\nBất kỳ lúc nào bạn cũng có thể tắt lại trong Cấu hình."))
                {
                    UpdatePrivacy();return;
                }
                // Denied microphone access must end any already-running
                // recording BEFORE updating persisted permissions.
                if(!newMicrophone)
                {
                    StopHandsFreeSession();
                    _geminiMicrophone.Stop(false);
                }
                GeminiWarehouseSharingConsent.Save(newWarehouse);
                GeminiVoiceConsentStore.Save(newMicrophone);
                _geminiChatIncludeWarehouse=newWarehouse;
                UpdatePrivacy();
                privacyStatus.Foreground=Brush("#14765C");
            }
            catch(Exception e)
            {
                UpdatePrivacy();
                privacyStatus.Text="Không lưu được quyền: "+e.Message;
                privacyStatus.Foreground=Brush("#AA4D39");
            }
        },true);
        var voiceTest=T("Giọng Mây: nữ dịu dàng, ưu tiên miền Nam; phát âm thực tế phụ thuộc Gemini TTS và loa Windows.",12,false,Muted);
        var testSpeaker=A("🔊 Thử giọng đọc Mây",async()=>
        {
            var key=credentials.ReadKey();
            if(string.IsNullOrWhiteSpace(key))
            {
                voiceTest.Text="Chưa cấu hình Gemini API Key để tạo giọng đọc.";return;
            }
            try
            {
                await _geminiVoice.SpeakGeminiAsync(key,
                    "Dạ, Mây Mây chào anh. Mây đang sẵn sàng hỗ trợ công việc tại kho Hưng Yên nha.",
                    message=>Dispatcher.BeginInvoke(new Action(()=>voiceTest.Text=message)),null);
            }
            catch(Exception e){voiceTest.Text="Không phát được giọng: "+e.Message;}
        });

        var sourcePaths=new[]{
            ("Tổng hợp",_service.Settings.MasterPath),
            ("Thành phẩm xuất",_service.Settings.ProductExportPath),
            ("KT",_service.Settings.KtPath),
            ("Máy kéo",_service.Settings.TractionMachineListPath),
            ("Tủ điện",_service.Settings.ElectricalCabinetListPath),
            ("Đầu cửa car",_service.Settings.CarDoorListPath)
        };
        var sourceStatus=string.Join(" · ",sourcePaths.Select(x=>x.Item1+":"+
            (!string.IsNullOrWhiteSpace(x.Item2)&&System.IO.File.Exists(x.Item2)?"✓":"—")));
        return Card(Stack(instructions,
            Row(save,delete,testConnection),
            connectionResult,
            T("MÔ HÌNH VÀ CÁC CÔNG CỤ",14,true),
            T("Trò chuyện: "+GeminiChatService.Model+
                " · Nhận dạng: Gemini qua Internet · Giọng Mây: "+GeminiVietnameseTts.Model+
                " (giọng nữ, ưu tiên miền Nam). Các mô hình hiện được cố định theo phiên bản HISA để bảo đảm tương thích.",12,false,Muted),
            Row(B("🎙 Mở trò chuyện Mây",()=>Navigate("gemini-chat"),true),
                B("📷 Đọc lịch Zalo",()=>Navigate("ai-schedule")),
                B("▤ Mây đề xuất phiếu",()=>Navigate("gemini-chat")),
                B("AI đọc / đề xuất sửa Excel",()=>Navigate("gemini-excel"))),
            T("KIỂM TRA NGUỒN DỮ LIỆU ĐỐI CHIẾU",14,true),
            T(sourceStatus,12,true,Brush("#217E76")),
            T("Dữ liệu Excel được đọc cục bộ theo nguồn HISA đã cấu hình. Khi cấp quyền gửi Gemini, chỉ bản tóm tắt có giới hạn được truyền đi; việc lưu, sửa hay duyệt phiếu vẫn phải xác nhận riêng.",11,false,Muted),
            T("GIỌNG NÓI · MICRO · RẢNH TAY",14,true),
            Row(Field("Micro đầu vào",inputMic),Field("Độ nhạy nhận dạng",sensitivity)),
            speakAutomatically,
            Row(savePreferences,testSpeaker,
                B("⚙ Quyền micro Windows",()=>
                {
                    try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "ms-settings:privacy-microphone"){UseShellExecute=true});}
                    catch(Exception ex){preferenceStatus.Text="Không mở được Windows Settings: "+ex.Message;}
                })),
            preferenceStatus,voiceTest,
            T("QUYỀN RIÊNG TƯ · BẬT/TẮT ĐỘC LẬP",14,true),
            share,micPermission,Row(savePrivacy,B("⏹ Dừng Mây ngay",()=>
            {
                StopHandsFreeSession();
                privacyStatus.Text="Đã dừng micro và loa. Các quyền đã lưu chưa thay đổi.";
            })),
            privacyStatus,
            T("CHẠY NỀN & AN TOÀN",14,true),
            T("Thu nhỏ HISA để đưa Mây vào khay hệ thống. Chỉ một phiên rảnh tay bạn đã chủ động bật mới tiếp tục nghe; HISA không tự bật micro lúc khởi động. Nhấn ⏹ Dừng Mây để ngừng ngay. Mây không tự xuất hàng, sửa Excel hoặc duyệt phiếu.",12,false,Muted),
            T("BẢO MẬT API KEY",12,true),
            T("Khóa được lưu trong Windows Credential Manager theo tài khoản hiện tại và được giữ khi cập nhật. HISA không ghi API Key vào Excel, JSON cấu hình hay file ZIP sao lưu.",12,false,Muted),
            Row(B("Google AI Studio",()=>
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "https://aistudio.google.com/apikey"){UseShellExecute=true})),
                B("Kiểm tra máy kéo gia công",()=>Navigate("motor-hung-yen")))
        ));
    }
}
