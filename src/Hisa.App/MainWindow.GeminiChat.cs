using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hisa.App;

public partial class MainWindow
{
    private readonly List<GeminiChatMessage> _geminiChatHistory = [];
    private bool _geminiChatIncludeWarehouse=GeminiWarehouseSharingConsent.Read();
    private bool _geminiChatSending;
    private readonly GeminiVoice _geminiVoice=new();
    private readonly GeminiMicrophoneRecorder _geminiMicrophone=new();
    private bool _geminiMicrophoneConsent;
    private bool _geminiChatTranscribing;
    private bool _geminiChatAutoSpeak;
    private bool _geminiHandsFreeActive;
    private int _geminiHandsFreeSession;
    private CancellationTokenSource? _geminiHandsFreeCancellation;
    private Button? _geminiHandsFreeToggle;

    /// <summary>Close the microphone immediately on explicit stop, navigation or exit.</summary>
    private void StopHandsFreeSession()
    {
        _geminiHandsFreeActive=false;
        if(_geminiHandsFreeToggle!=null)
            _geminiHandsFreeToggle.Content="🎙 Nói với Mây";
        TopVoiceButton.Content="🎙 Nói với Mây";
        _geminiHandsFreeSession++;
        var cts=_geminiHandsFreeCancellation;
        _geminiHandsFreeCancellation=null;
        try{cts?.Cancel();}
        catch(ObjectDisposedException){}
        cts?.Dispose();
        _geminiMicrophone.Stop(false);
        _geminiVoice.StopSpeaking();
    }

    private bool HandsFreeCurrent(int session) =>
        _geminiHandsFreeActive && session==_geminiHandsFreeSession && _page=="gemini-chat";

    private void ShowGeminiChat()
    {
        _page="gemini-chat";
        var messages=new StackPanel { Margin=new Thickness(6,8,8,8) };
        var viewer=new ScrollViewer
        {
            Content=messages, VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            Background=Brush("#F7FAFD")
        };

        void Bubble(string role,string content)
        {
            var assistant=role=="model";
            var outer=new Border
            {
                HorizontalAlignment=assistant?HorizontalAlignment.Left:HorizontalAlignment.Right,
                MaxWidth=760, Padding=new Thickness(16,12,16,12),
                Margin=new Thickness(10,7,10,7),CornerRadius=new CornerRadius(13),
                Background=Brush(assistant?"#FFFFFF":"#E3F5F2"),
                BorderBrush=Brush(assistant?"#DAE6EE":"#BFE8E1"),
                BorderThickness=new Thickness(1),
                Child=Stack(
                    T(assistant?"✦ MÂY MÂY · TRỢ LÝ HISA":"ANH",11,true,Brush(assistant?"#1A8C92":"#307866")),
                    new TextBlock {
                        Text=content,TextWrapping=TextWrapping.Wrap,
                        Foreground=Brush("#19394D"),FontSize=13.5,
                        LineHeight=22,Margin=new Thickness(0,7,0,0)
                    })
            };
            messages.Children.Add(outer);
        }
        void ScrollToEnd() => viewer.ScrollToEnd();

        if(_geminiChatHistory.Count==0)
        {
            Bubble("model","Dạ, Mây chào anh! Cứ gọi Mây hoặc Mây Mây nha. "+
                "Mây có thể trò chuyện bằng tiếng Việt, xem lịch cont và lịch xuất trong HISA, "+
                "và giúp lên phiếu xuất nháp để anh kiểm tra, duyệt. Mây không tự lưu phiếu hay ghi Excel đâu ạ.");
        }
        else foreach(var item in _geminiChatHistory) Bubble(item.Role,item.Text);

        var question=new TextBox
        {
            AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=56,
            MaxHeight=125,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalContentAlignment=VerticalAlignment.Top,
            FontSize=13.5,Padding=new Thickness(13),
            ToolTip="Nhập câu hỏi. Enter để gửi; Shift+Enter để xuống dòng."
        };

        var contextToggle=new CheckBox
        {
            Content="Cho phép Gemini đọc dữ liệu HISA liên quan (kiện, lịch cont về, lịch xuất đã duyệt, phiếu và trạng thái chuẩn bị)",
            IsChecked=_geminiChatIncludeWarehouse,
            Foreground=Brush("#244C60"),FontWeight=FontWeights.SemiBold
        };
        var privacyNote=T(
            _geminiChatIncludeWarehouse
            ? "ĐÃ BẬT: Gemini có thể nhận dữ liệu HISA chỉ đọc được chọn lọc, gồm lịch hàng về và lịch xuất. Không gửi file Excel gốc, đường dẫn hay API Key."
            : "CHƯA CHIA SẺ: Gemini không thể biết tình hình kho HISA. Đánh dấu ô trên để cho phép đọc tóm tắt, không gửi file Excel gốc.",
            11,false,Muted);

        var send=B("➤  Gửi câu hỏi",()=>{ },true);
        var activity=T("Sẵn sàng. Mỗi câu hỏi cần Internet và hạn mức Gemini API.",11,false,Muted);
        var voicePermission=new CheckBox
        {
            Content=new TextBlock
            {
                Text="Tôi đồng ý: khi bật micro/rảnh tay, HISA được thu âm và gửi "+
                     "các lượt nói tới Google Gemini qua Internet để nhận dạng tiếng Việt. "+
                     "Rảnh tay tự gửi câu hỏi; không lưu file âm thanh. Có thể thu hồi quyền bất kỳ lúc nào.",
                TextWrapping=TextWrapping.Wrap,MaxWidth=780
            },
            IsChecked=GeminiVoiceConsentStore.Read(),
            HorizontalContentAlignment=HorizontalAlignment.Stretch
        };
        voicePermission.Checked+=(_,_)=>{try{GeminiVoiceConsentStore.Save(true);}
            catch(Exception ex){voicePermission.IsChecked=false;activity.Text="Không lưu được sự đồng ý: "+ex.Message;}};
        voicePermission.Unchecked+=(_,_)=>
        {
            StopHandsFreeSession();
            _geminiMicrophone.Stop(false);
            try{GeminiVoiceConsentStore.Save(false);}
            catch(Exception ex){activity.Text="Không lưu được trạng thái quyền micro: "+ex.Message;}
            activity.Text="Đã tắt quyền micro/Gemini. Micro đã dừng.";
        };
        var micSources=GeminiMicrophoneRecorder.AvailableMicrophones();
        var micSelector=new ComboBox
        {
            Width=290,ItemsSource=micSources,
            DisplayMemberPath=nameof(MicrophoneSource.Label),
            SelectedValuePath=nameof(MicrophoneSource.Number),SelectedValue=-1,
            ToolTip="Mặc định dùng micro của Windows. Nếu tín hiệu không tăng khi nói, chọn thiết bị khác."
        };
        var sensitivitySelector=new ComboBox
        {
            Width=160,DisplayMemberPath="Label",SelectedValuePath="Value",
            ItemsSource=new[]
            {
                new {Label="Cao (mic nhỏ)",Value=0.002},
                new {Label="Vừa (mặc định)",Value=0.0045},
                new {Label="Thấp (nơi ồn)",Value=0.010}
            },
            SelectedValue=0.0045
        };
        int SelectedMicrophone()=>micSelector.SelectedValue is int n?n:-1;
        double SelectedSensitivity()=>sensitivitySelector.SelectedValue is double v?v:0.0045;
        var autoVoice=new CheckBox{Content="Tự đọc câu trả lời bằng loa hệ thống",
            IsChecked=_geminiChatAutoSpeak};
        autoVoice.Checked+=(_,_)=>_geminiChatAutoSpeak=true;
        autoVoice.Unchecked+=(_,_)=>_geminiChatAutoSpeak=false;
        void StartHandsFreeListening(int session)
        {
            if(!HandsFreeCurrent(session))return;
            if(_geminiMicrophone.IsRecording)return;
            try
            {
                _geminiMicrophone.StartHandsFree(
                    wav=>Dispatcher.BeginInvoke(new Action(()=>
                    {
                        if(!HandsFreeCurrent(session)){Array.Clear(wav);return;}
                        _=ProcessHandsFreeAudioAsync(wav,session);
                    })),
                    message=>Dispatcher.BeginInvoke(new Action(()=>
                    {
                        if(HandsFreeCurrent(session))activity.Text=message;
                    })),
                    SelectedMicrophone(),SelectedSensitivity());
                activity.Text="● RẢNH TAY ĐANG NGHE · Nói tiếng Việt. HISA tự dừng sau khi bạn im lặng; bấm Tắt rảnh tay để ngừng.";
            }
            catch(Exception ex)
            {
                StopHandsFreeSession();
                activity.Text="Không thể bật rảnh tay: "+ex.Message+
                    ". Kiểm tra Windows Settings → Privacy & security → Microphone.";
            }
        }

        async Task ResumeAfterSpeakerAsync(int session)
        {
            if(!HandsFreeCurrent(session))return;
            try
            {
                // Give the output device time to become silent; never record
                // the tail of the assistant's own response.
                await Task.Delay(750,_geminiHandsFreeCancellation?.Token??CancellationToken.None);
            }
            catch(OperationCanceledException){return;}
            if(HandsFreeCurrent(session))StartHandsFreeListening(session);
        }

        async Task ProcessHandsFreeAudioAsync(byte[] wav,int session)
        {
            try
            {
                if(!HandsFreeCurrent(session))return;
                string? key;
                try{key=new GeminiCredentialStore().ReadKey();}
                catch(Exception){key=null;}
                if(string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Chưa có Gemini API Key trong Cấu hình.");
                activity.Text="● Đã dừng micro · Gemini đang nhận dạng tiếng Việt…";
                var transcript=await GeminiAudioTranscriptionService.TranscribeAsync(
                    key,wav,_geminiHandsFreeCancellation?.Token??CancellationToken.None);
                if(!HandsFreeCurrent(session))return;
                if(string.IsNullOrWhiteSpace(transcript))
                    throw new FormatException("Không nhận dạng được câu nói.");
                activity.Text="Đã nghe: "+transcript+" · Đang hỏi Gemini…";
                await SendAsync(transcript,true,session);
            }
            catch(OperationCanceledException)
            {
                // Explicit stop must not restart the microphone or upload more audio.
            }
            catch(Exception ex)
            {
                if(HandsFreeCurrent(session))
                {
                    StopHandsFreeSession();
                    activity.Text="Đã dừng rảnh tay do lỗi nhận dạng: "+ex.Message;
                }
            }
            finally{Array.Clear(wav);}
        }

        Button? handsFree=null;
        handsFree=B("🎙 Nói với Mây",()=>
        {
            if(_geminiHandsFreeActive)
            {
                StopHandsFreeSession();
                handsFree!.Content="🎙 Nói với Mây";
                activity.Text="○ ĐÃ DỪNG HISA · micro và loa đã tắt. Bấm Nói với Mây để tiếp tục.";
                return;
            }
            if(_geminiChatSending||_geminiChatTranscribing||_geminiMicrophone.IsRecording)
            {
                activity.Text="Hãy hoàn thành lượt micro/Gemini hiện tại trước khi bật rảnh tay.";
                return;
            }
            string? key;
            try{key=new GeminiCredentialStore().ReadKey();}
            catch(Exception){key=null;}
            if(string.IsNullOrWhiteSpace(key))
            {
                activity.Text="Cần cấu hình Gemini API Key trước khi bật rảnh tay.";
                return;
            }
            if(voicePermission.IsChecked!=true || !GeminiVoiceConsentStore.Read())
            {
                // One clear consent covers both microphone and a bounded HISA
                // read-only projection. Persist independent permissions so
                // either can later be revoked in Advanced options.
                if(MessageBox.Show(this,
                    "BẬT TRỢ LÝ HISA RẢNH TAY?\n\n"+
                    "• Khi bạn bấm Nói với Mây, micro sẽ mở trong phiên hiện tại. "+
                    "Mỗi đoạn nói được tự động gửi đến Google Gemini qua Internet "+
                    "để nhận dạng và hỏi đáp tiếng Việt; Gemini phát trả lời qua loa.\n"+
                    "• Đồng thời cho phép gửi tóm tắt dữ liệu HISA liên quan "+
                    "(lịch cont về, lịch xuất đã duyệt, kiện, phiếu, trạng thái kho) "+
                    "cho Gemini khi bạn đặt câu hỏi. Không gửi nguyên file Excel, API Key "+
                    "hay dữ liệu ngoài phạm vi câu hỏi.\n"+
                    "• Lựa chọn được ghi nhớ để lần sau chỉ cần bấm một nút. "+
                    "Bạn có thể tắt và thu hồi từng quyền trong Tùy chọn. "+
                    "Micro KHÔNG tự bật khi mở HISA và AI KHÔNG tự sửa Excel.\n\n"+
                    "Bạn đồng ý bật micro và chia sẻ tóm tắt HISA như trên chứ?",
                    "HISA · Quyền micro và dữ liệu (một lần)",
                    MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)
                {
                    activity.Text="Chưa bật HISA. Micro không được mở và dữ liệu kho không được gửi.";
                    return;
                }
                try
                {
                    GeminiVoiceConsentStore.Save(true);
                    voicePermission.IsChecked=true;
                    if(!GeminiWarehouseSharingConsent.Read())
                    {
                        GeminiWarehouseSharingConsent.Save(true);
                        contextToggle.IsChecked=true;
                    }
                }
                catch(Exception ex)
                {
                    activity.Text="Không lưu được quyền đã đồng ý: "+ex.Message;
                    return;
                }
                if(!GeminiVoiceConsentStore.Read() || voicePermission.IsChecked!=true)
                {
                    activity.Text="Quyền micro chưa được cấp. HISA không thu âm.";
                    return;
                }
            }
            _geminiHandsFreeCancellation=new CancellationTokenSource();
            _geminiHandsFreeActive=true;
            int session=++_geminiHandsFreeSession;
            handsFree!.Content="⏹ Dừng Mây";
            TopVoiceButton.Content="⏹ Dừng Mây";
            StartHandsFreeListening(session);
        });
        _geminiHandsFreeToggle=handsFree;

        void OnRecorded(byte[] wav)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                // Recording has stopped before this cloud-upload decision.
                if(_page!="gemini-chat"){Array.Clear(wav);return;}
                if(!GeminiVoiceConsentStore.Read())
                {
                    Array.Clear(wav);
                    activity.Text="Bạn đã thu hồi quyền gửi âm thanh; không gửi Gemini.";
                    return;
                }
                string? key;
                try { key=new GeminiCredentialStore().ReadKey(); }
                catch(Exception) { key=null; }
                if(string.IsNullOrWhiteSpace(key))
                {
                    Array.Clear(wav);
                    activity.Text="Chưa có Gemini API Key. Mở Cấu hình & sao lưu → Gemini AI.";
                    return;
                }
                _geminiChatTranscribing=true;
                activity.Text="Đang chuyển giọng nói tiếng Việt thành chữ bằng Gemini…";
                try
                {
                    var transcript=await GeminiAudioTranscriptionService.TranscribeAsync(key,wav);
                    if(_page=="gemini-chat")
                    {
                        if(question.Text.Length>0)question.Text+=" ";
                        question.Text+=transcript;
                        question.CaretIndex=question.Text.Length;
                        activity.Text="Đã nhận dạng tiếng Việt. Kiểm tra số hợp đồng và nội dung trước khi bấm Gửi.";
                    }
                }
                catch(OperationCanceledException)
                { activity.Text="Đã hủy nhận dạng âm thanh."; }
                catch(HttpRequestException)
                { activity.Text="Không kết nối được Gemini. Kiểm tra Internet rồi thử lại."; }
                catch(Exception ex) when(ex is InvalidOperationException or FormatException or
                    System.Text.Json.JsonException)
                { activity.Text="Nhận dạng giọng nói: "+ex.Message; }
                finally
                {
                    Array.Clear(wav);
                    _geminiChatTranscribing=false;
                }
            }));
        }

        var listen=B("🎙  Bắt đầu thu micro",()=>
        {
            if(_geminiHandsFreeActive)
            {
                activity.Text="Đang bật rảnh tay. Bấm Tắt rảnh tay trước khi dùng chế độ thu thủ công.";
                return;
            }
            if(_geminiChatTranscribing)
            {
                activity.Text="Gemini đang nhận dạng bản ghi trước. Hãy kiểm tra kết quả.";
                return;
            }
            if(_geminiMicrophone.IsRecording)
            {
                activity.Text="Micro đang thu. Bấm 'Dừng và nhận dạng' hoặc 'Hủy micro'.";
                return;
            }
            if(voicePermission.IsChecked!=true || !GeminiVoiceConsentStore.Read())
            {
                activity.Text="Hãy đánh dấu đồng ý thu và gửi âm thanh tới Gemini trước khi bật micro.";
                return;
            }
            try
            {
                _geminiMicrophone.Start(OnRecorded,
                    message=>Dispatcher.BeginInvoke(new Action(()=>activity.Text=message)),
                    SelectedMicrophone());
                activity.Text="● ĐANG THU micro (tối đa 25 giây). Nói tiếng Việt, rồi bấm 'Dừng và nhận dạng'.";
            }
            catch(Exception ex)
            {
                activity.Text="Không mở được micro: "+ex.Message+
                    " · Kiểm tra Windows Settings → Privacy & security → Microphone "+
                    "và System → Sound → Input.";
            }
        });
        var testMic=B("🔎 Thử micro (không gửi)",()=>
        {
            if(_geminiHandsFreeActive || _geminiMicrophone.IsRecording)
            {
                activity.Text="Tắt rảnh tay hoặc dừng thu hiện tại trước khi thử micro.";
                return;
            }
            if(voicePermission.IsChecked!=true || !GeminiVoiceConsentStore.Read())
            {
                activity.Text="Hãy bật quyền micro ở phía trên để thử tín hiệu (không gửi Gemini).";
                return;
            }
            try
            {
                _geminiMicrophone.Start(wav=>Array.Clear(wav),
                    message=>Dispatcher.BeginInvoke(new Action(()=>activity.Text=message)),
                    SelectedMicrophone());
                activity.Text="Đang thử micro, hãy nói. Nếu tín hiệu không tăng, chọn thiết bị khác. Bấm Hủy micro để dừng. Không gửi Gemini.";
            }
            catch(Exception ex)
            {
                activity.Text="Không thể thử micro: "+ex.Message;
            }
        });
        var transcribe=B("✓  Dừng và nhận dạng",()=>
        {
            if(!_geminiMicrophone.IsRecording)
            {
                activity.Text="Chưa thu âm. Bấm 'Bắt đầu thu micro' trước.";
                return;
            }
            activity.Text="Đã dừng micro · đang nhận dạng tiếng Việt bằng Gemini…";
            _geminiMicrophone.Stop(true);
        });
        var speak=B("🔊  Đọc câu trả lời",async ()=>
        {
            var latest=_geminiChatHistory.LastOrDefault(x=>x.Role=="model")?.Text;
            if(string.IsNullOrWhiteSpace(latest))
            {activity.Text="Chưa có câu trả lời Gemini để phát.";return;}
            string? key;
            try{key=new GeminiCredentialStore().ReadKey();}
            catch(Exception){key=null;}
            if(string.IsNullOrWhiteSpace(key))
            {activity.Text="Chưa cấu hình Gemini API Key để tạo giọng nói tiếng Việt.";return;}
            try
            {
                await _geminiVoice.SpeakGeminiAsync(key,latest,
                    message=>Dispatcher.BeginInvoke(new Action(()=>activity.Text=message)),null);
            }
            catch(Exception ex){activity.Text="Không phát được giọng tiếng Việt: "+ex.Message;}
        });
        var stopVoice=B("◼ Hủy micro / dừng loa",()=>
        {
            if(_geminiHandsFreeActive)
            {
                StopHandsFreeSession();
                activity.Text="Đã tắt rảnh tay, hủy tác vụ và dừng micro/loa.";
                return;
            }
            _geminiMicrophone.Stop(false);
            _geminiVoice.StopSpeaking();
            activity.Text="Đã hủy micro. Âm thanh chưa được gửi đến Gemini.";
        });
        var privacySettings=B("⚙ Quyền micro Windows",()=>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "ms-settings:privacy-microphone"){UseShellExecute=true});
            }
            catch(Exception ex)
            {
                activity.Text="Không mở được Cài đặt Windows: "+ex.Message+
                    ". Nhấn Win+R rồi nhập ms-settings:privacy-microphone.";
            }
        });

        void ClearChat()
        {
            if(_geminiHandsFreeActive)
            {
                StopHandsFreeSession();
                handsFree!.Content="🎙 Nói với Mây";
            }
            _geminiChatHistory.Clear();
            messages.Children.Clear();
            Bubble("model","Dạ, Mây đây ạ. Mây sẵn sàng xem lịch và soạn phiếu nháp để anh duyệt nha.");
            activity.Text="Đã xóa lịch sử trò chuyện trong phiên này.";
        }

        contextToggle.Checked+=(_,_) =>
        {
            try
            {
                GeminiWarehouseSharingConsent.Save(true);
                _geminiChatIncludeWarehouse=true;
                ClearChat(); // discard turns from an incompatible privacy mode
                privacyNote.Text="✓ ĐÃ CHO PHÉP: HISA chia sẻ dữ liệu vận hành liên quan (chỉ đọc) khi hỏi Gemini, kể cả câu hỏi bằng mic/rảnh tay. Ô này được nhớ cho lần mở sau; bỏ chọn để thu hồi.";
            }
            catch(Exception ex)
            {
                _geminiChatIncludeWarehouse=false;
                contextToggle.IsChecked=false;
                privacyNote.Text="Không lưu được quyền chia sẻ: "+ex.Message;
            }
        };
        contextToggle.Unchecked+=(_,_) =>
        {
            _geminiChatIncludeWarehouse=false;
            ClearChat();
            try{GeminiWarehouseSharingConsent.Save(false);}
            catch(Exception ex){privacyNote.Text="Không lưu được trạng thái tắt chia sẻ: "+ex.Message;return;}
            privacyNote.Text="ĐÃ TẮT: Gemini chỉ nhận câu hỏi, không được đọc dữ liệu HISA. Bật lại khi cần hỏi lịch cont/lịch xuất.";
        };

        async Task SendAsync(string? voiceText=null,bool fromHandsFree=false,int session=0)
        {
            bool handedToSpeaker=false;
            if(_geminiChatSending)return;
            if(_geminiHandsFreeActive && !fromHandsFree)
            {
                activity.Text="Đang bật rảnh tay. Bấm Tắt rảnh tay trước khi gửi văn bản thủ công.";
                return;
            }
            if(fromHandsFree && !HandsFreeCurrent(session))return;
            var userText=(voiceText??question.Text).Trim();
            if(userText.Length==0)
            {
                if(fromHandsFree)StopHandsFreeSession();
                activity.Text="Không nhận được câu hỏi. Đã tắt rảnh tay.";
                return;
            }
            if(userText.Length>GeminiChatService.MaxQuestionLength)
            {
                if(fromHandsFree)StopHandsFreeSession();
                activity.Text="Câu hỏi quá 3.000 ký tự. Đã tắt rảnh tay.";return;
            }
            string? key;
            try { key=new GeminiCredentialStore().ReadKey(); }
            catch (Exception)
            {
                activity.Text="Không đọc được Gemini API Key. Kiểm tra Cấu hình & sao lưu.";return;
            }
            if(string.IsNullOrWhiteSpace(key))
            {
                activity.Text="Chưa lưu Gemini API Key. Mở Cấu hình & sao lưu → Gemini AI.";return;
            }
            _geminiChatSending=true;
            send.IsEnabled=false;question.IsEnabled=false;contextToggle.IsEnabled=false;
            activity.Text="Đang hỏi Gemini qua Internet…";
            try
            {
                string? context=null;
                if(_geminiChatIncludeWarehouse && GeminiWarehouseSharingConsent.Read())
                {
                    var warehouse=_service.Snapshot;
                    var readiness=new DispatchReadinessStore(_service.DataDirectory).Load();
                    var approved=AiScheduleWorkspace.RecentApproved(_service.DataDirectory);
                    context=GeminiChatService.BuildReadOnlyContext(
                        userText,warehouse,_service.Slips,readiness,DateTime.Today,approved);
                    activity.Text="Đã cấp dữ liệu HISA chỉ đọc: "+
                        approved.Count+" lịch đã duyệt, "+
                        (warehouse.Packages?.Count??0)+" kiện trong bộ nhớ · Đang hỏi Gemini…";
                }
                else if(_geminiChatIncludeWarehouse)
                {
                    _geminiChatIncludeWarehouse=false;
                    contextToggle.IsChecked=false;
                    activity.Text="Quyền chia sẻ HISA đã thu hồi. Gemini chỉ nhận câu hỏi.";
                }
                var reply=await GeminiChatService.AskAsync(
                    key,_geminiChatHistory,userText,context,
                    fromHandsFree ? (_geminiHandsFreeCancellation?.Token??CancellationToken.None)
                        : CancellationToken.None);
                if(fromHandsFree && !HandsFreeCurrent(session))return;
                _geminiChatHistory.Add(new GeminiChatMessage { Role="user",Text=userText });
                _geminiChatHistory.Add(new GeminiChatMessage { Role="model",Text=reply });
                // Keep only recent local turns, no conversation or key saved on disk.
                if(_geminiChatHistory.Count>24)
                    _geminiChatHistory.RemoveRange(0,_geminiChatHistory.Count-24);
                Bubble("user",userText);
                Bubble("model",reply);
                // Extract intents only from the user's own utterance, never
                // from Gemini output or untrusted workbook/schedule fields.
                var slipProposal=MaySlipIntent.Parse(userText,DateTime.Today);
                if(slipProposal!=null)
                {
                    var chosen=ResolveMaySlipProposal(slipProposal);
                    if(chosen!=null)
                    {
                        var proposalPanel=new Border
                        {
                            Margin=new Thickness(14,4,14,12),
                            Padding=new Thickness(12),
                            CornerRadius=new CornerRadius(9),
                            Background=Brush("#EAF6F2"),
                            Child=Stack(
                                T("MÂY ĐỀ XUẤT PHIẾU XUẤT NHÁP",13,true,Brush("#197A70")),
                                T(chosen.Summary,12,false,Muted),
                                T("Chưa lưu phiếu · chưa ghi sổ xuất · anh cần kiểm tra và duyệt ở màn hình tiếp theo.",11,false,Muted),
                                B("▤ Xem phiếu Mây đề xuất",()=>OpenMaySlipReview(chosen),true)
                            )
                        };
                        messages.Children.Add(proposalPanel);
                        activity.Text="Mây đã chuẩn bị đề xuất phiếu. Anh bấm Xem phiếu Mây đề xuất để kiểm tra trước khi lưu.";
                    }
                    else
                        messages.Children.Add(T("Mây cần số hợp đồng hợp lệ hoặc lịch xuất đã duyệt đúng ngày để lên phiếu; Mây chưa ghi gì vào sổ kho.",12,false,Muted));
                }
                if(fromHandsFree && HandsFreeCurrent(session))
                {
                    // Micro has already stopped: speech output cannot be
                    // captured and mistaken for a new user question.
                    try
                    {
                        await _geminiVoice.SpeakGeminiAsync(key,reply,
                            message=>Dispatcher.BeginInvoke(new Action(()=>
                            {
                                if(HandsFreeCurrent(session))activity.Text=message;
                            })),()=>Dispatcher.BeginInvoke(new Action(()=>
                                _=ResumeAfterSpeakerAsync(session))),
                            _geminiHandsFreeCancellation?.Token??CancellationToken.None);
                        handedToSpeaker=true;
                        activity.Text="● RẢNH TAY · Gemini đang trả lời bằng loa; micro tạm tắt.";
                    }
                    catch(Exception ex)
                    {
                        StopHandsFreeSession();
                        activity.Text="Dừng rảnh tay: không phát được giọng tiếng Việt: "+ex.Message;
                    }
                }
                else if(_geminiChatAutoSpeak)
                {
                    try
                    {
                        await _geminiVoice.SpeakGeminiAsync(key,reply,
                            message=>Dispatcher.BeginInvoke(new Action(()=>activity.Text=message)),null);
                    }
                    catch(Exception ex){activity.Text="Giọng đọc tiếng Việt chưa hoạt động: "+ex.Message;}
                }
                if(!fromHandsFree)question.Clear();
                ScrollToEnd();
                if(!fromHandsFree)
                    activity.Text="Gemini đã trả lời. Trạng thái kho chỉ có giá trị sau khi anh kiểm tra nguồn.";
            }
            catch(OperationCanceledException)
            {
                if(!fromHandsFree)activity.Text="Gemini phản hồi quá chậm. Kiểm tra Internet rồi thử lại.";
            }
            catch(HttpRequestException)
            {
                activity.Text="Không kết nối được Gemini. Kiểm tra Internet, DNS hoặc tường lửa.";
            }
            catch(Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.Json.JsonException)
            {
                // GeminiChatService only exposes sanitized provider errors, not raw response or key.
                activity.Text=ex.Message;
            }
            finally
            {
                if(fromHandsFree && HandsFreeCurrent(session) && !handedToSpeaker)
                {
                    // Do not silently loop after an API error, which could
                    // repeatedly upload room audio without a useful response.
                    StopHandsFreeSession();
                    activity.Text="Rảnh tay đã dừng (lỗi mạng, API hoặc loa). Bấm Bật để thử lại.";
                }
                _geminiChatSending=false;
                send.IsEnabled=true;question.IsEnabled=true;contextToggle.IsEnabled=true;
                question.Focus();
            }
        }
        send.Click+=async (_,_)=>await SendAsync();
        question.PreviewKeyDown+=async (_,e)=>
        {
            if(e.Key==Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift)==0)
            {
                e.Handled=true;await SendAsync();
            }
        };

        var examples=Row(
            B("Hợp đồng nào cần chuẩn bị?",()=>question.Text="Hôm nay và ngày mai có hợp đồng nào dự kiến xuất, cần chuẩn bị vật tư?"),
            B("Tìm theo số hợp đồng",()=>{question.Text="Tra cứu hợp đồng ";question.Focus();question.CaretIndex=question.Text.Length;}),
            B("Báo cáo công việc hôm nay",()=>{_geminiChatIncludeWarehouse=true;
                contextToggle.IsChecked=true;question.Text="Dựa trên dữ liệu kho đã chia sẻ, tóm tắt công việc hôm nay, kiện cont chờ nhận, hợp đồng chờ xuất, máy kéo cần kiểm tra và việc cần ưu tiên. Nêu rõ những gì chưa có dữ liệu."; }),
            B("Quy tắc cont đi",()=>question.Text="Nếu cùng hợp đồng nhưng khác ngày cont đi và T1/T2 thì quản lý kiện thế nào?")
        );
        var advanced=new Expander
        {
            Header="⚙ Tùy chọn nâng cao · quyền riêng tư, micro, giọng đọc",
            IsExpanded=false,
            Margin=new Thickness(0,9,0,2),
            Content=Stack(
                T("Có thể bỏ chọn một trong hai quyền bất kỳ lúc nào. "+
                  "Tắt chia sẻ kho thì Gemini chỉ biết nội dung bạn hỏi; "+
                  "tắt quyền micro sẽ ngừng thu ngay.",11,false,Muted),
                contextToggle,privacyNote,voicePermission,autoVoice,
                Row(Field("Thiết bị micro",micSelector),Field("Độ nhạy rảnh tay",sensitivitySelector)),
                Row(testMic,privacySettings),
                Row(listen,transcribe,speak,stopVoice),
                B("Xóa cuộc trò chuyện",ClearChat),
                B("⚙ Gemini API Key",()=>Navigate("settings"))
            )
        };
        var head=Card(Stack(
            Row(T("✦  MÂY MÂY · TRỢ LÝ HISA",19,true),
                B("⚙ Cấu hình Gemini",()=>Navigate("settings"))),
            T("Mây là trợ lý nữ, trò chuyện nhẹ nhàng bằng tiếng Việt và ưu tiên giọng miền Nam. "+
              "Bấm Nói với Mây để hỏi lịch cont về, lịch xuất, hoặc nhờ Mây soạn phiếu xuất nháp. "+
              "Phiếu chỉ được lưu sau khi anh xem và duyệt; Mây không tự ghi Excel.",12,false,Muted),
            Row(handsFree),
            advanced
        ));
        var bottom=Card(Stack(
            T("CÂU HỎI GỢI Ý",11,true,Brush("#1D8E91")),
            examples,
            question,
            Row(send,B("AI đọc / đề xuất sửa Excel",()=>Navigate("gemini-excel")),
                B("Lịch Zalo",()=>Navigate("ai-schedule")),
                B("Kiểm tra xuất hàng",()=>Navigate("readiness"))),
            activity),new Thickness(16,12,16,12));
        var layout=new Grid();
        layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        layout.Children.Add(head);
        Grid.SetRow(viewer,1);layout.Children.Add(viewer);
        Grid.SetRow(bottom,2);layout.Children.Add(bottom);
        SetPage("Mây Mây · Trợ lý HISA","Một nút trò chuyện · giọng nữ miền Nam · đề xuất phiếu chờ anh duyệt",layout);
        ScrollToEnd();
    }
}
