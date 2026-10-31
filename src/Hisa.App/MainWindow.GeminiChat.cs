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
    private bool _geminiChatIncludeWarehouse;
    private bool _geminiChatSending;
    private readonly GeminiVoice _geminiVoice=new();
    private bool _geminiChatAutoSpeak;

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
                    T(assistant?"✦ GEMINI · TRỢ LÝ HISA":"ANH",11,true,Brush(assistant?"#1A8C92":"#307866")),
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
            Bubble("model","Xin chào! Tôi có thể trò chuyện bằng tiếng Việt, giải thích quy trình xuất hàng, " +
                "tra cứu số hợp đồng và phân tích vật tư trong HISA khi anh bật chia sẻ dữ liệu kho. " +
                "Tôi chỉ tư vấn, không tự nhập kho hay xác nhận xuất hàng.");
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
            Content="Cho phép Gemini đọc tóm tắt dữ liệu kho liên quan",
            IsChecked=_geminiChatIncludeWarehouse,
            Foreground=Brush("#244C60"),FontWeight=FontWeights.SemiBold
        };
        var privacyNote=T(
            _geminiChatIncludeWarehouse
            ? "ĐÃ BẬT: chỉ thông tin liên quan, giới hạn dung lượng, được gửi đến Google khi anh đặt câu hỏi. Không gửi file Excel hoặc API Key."
            : "MẶC ĐỊNH TẮT: chỉ câu hỏi và lịch sử trò chuyện được gửi đến Gemini. Không tự gửi dữ liệu kho.",
            11,false,Muted);

        var send=B("➤  Gửi câu hỏi",()=>{ },true);
        var activity=T("Sẵn sàng. Mỗi câu hỏi cần Internet và hạn mức Gemini API.",11,false,Muted);
        var autoVoice=new CheckBox{Content="Tự đọc câu trả lời bằng loa hệ thống",
            IsChecked=_geminiChatAutoSpeak};
        autoVoice.Checked+=(_,_)=>_geminiChatAutoSpeak=true;
        autoVoice.Unchecked+=(_,_)=>_geminiChatAutoSpeak=false;
        var listen=B("🎙  Mic nhập câu hỏi",()=>
        {
            try
            {
                _geminiVoice.Listen(word=>Dispatcher.BeginInvoke(()=>
                {
                    if(question.Text.Length>0)question.Text+=" ";
                    question.Text+=word;question.CaretIndex=question.Text.Length;
                }),message=>Dispatcher.BeginInvoke(()=>activity.Text=message));
            }
            catch(Exception ex)when(ex is InvalidOperationException or
                System.Runtime.InteropServices.COMException)
            {activity.Text="Không thể truy cập mic/nhận dạng Windows: "+ex.Message;}
        });
        var speak=B("🔊  Đọc câu trả lời",()=>
        {
            var latest=_geminiChatHistory.LastOrDefault(x=>x.Role=="model")?.Text;
            if(string.IsNullOrWhiteSpace(latest))
            {activity.Text="Chưa có câu trả lời Gemini để phát.";return;}
            try{_geminiVoice.Speak(latest,message=>Dispatcher.BeginInvoke(()=>
                    activity.Text=message));}
            catch(Exception ex) {activity.Text="Loa hệ thống: "+ex.Message;}
        });
        var stopVoice=B("◼ Dừng mic/loa",()=>
        {
            _geminiVoice.StopListening();_geminiVoice.StopSpeaking();
            activity.Text="Đã dừng mic/loa.";
        });

        void ClearChat()
        {
            _geminiChatHistory.Clear();
            messages.Children.Clear();
            Bubble("model","Bắt đầu cuộc trò chuyện mới. Tôi chỉ tư vấn và không thay đổi dữ liệu kho.");
            activity.Text="Đã xóa lịch sử trò chuyện trong phiên này.";
        }

        contextToggle.Checked+=(_,_) =>
        {
            _geminiChatIncludeWarehouse=true;
            ClearChat(); // prevent context from leaking into the wrong consent mode
            privacyNote.Text="ĐÃ BẬT: tóm tắt dữ liệu kho liên quan sẽ được gửi đến Gemini khi đặt câu hỏi. Không gửi file Excel hoặc API Key.";
        };
        contextToggle.Unchecked+=(_,_) =>
        {
            _geminiChatIncludeWarehouse=false;
            ClearChat(); // old model turns could contain private data
            privacyNote.Text="ĐÃ TẮT: chỉ câu hỏi và lịch sử trò chuyện mới được gửi đến Gemini. Không tự gửi dữ liệu kho.";
        };

        async Task SendAsync()
        {
            if(_geminiChatSending)return;
            var userText=question.Text.Trim();
            if(userText.Length==0){activity.Text="Nhập câu hỏi trước khi gửi.";return;}
            if(userText.Length>GeminiChatService.MaxQuestionLength)
            {
                activity.Text="Câu hỏi không được quá 3.000 ký tự.";return;
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
                if(_geminiChatIncludeWarehouse)
                {
                    var warehouse=_service.Snapshot;
                    var readiness=new DispatchReadinessStore(_service.DataDirectory).Load();
                    context=GeminiChatService.BuildReadOnlyContext(
                        userText,warehouse,_service.Slips,readiness,DateTime.Today);
                }
                var reply=await GeminiChatService.AskAsync(
                    key,_geminiChatHistory,userText,context);
                _geminiChatHistory.Add(new GeminiChatMessage { Role="user",Text=userText });
                _geminiChatHistory.Add(new GeminiChatMessage { Role="model",Text=reply });
                // Keep only recent local turns, no conversation or key saved on disk.
                if(_geminiChatHistory.Count>24)
                    _geminiChatHistory.RemoveRange(0,_geminiChatHistory.Count-24);
                Bubble("user",userText);
                Bubble("model",reply);
                if(_geminiChatAutoSpeak)
                {
                    try{_geminiVoice.Speak(reply,message=>Dispatcher.BeginInvoke(()=>
                            activity.Text=message));}
                    catch(Exception ex){activity.Text="Loa Windows: "+ex.Message;}
                }
                question.Clear();
                ScrollToEnd();
                activity.Text="Gemini đã trả lời. Trạng thái kho chỉ có giá trị sau khi anh kiểm tra nguồn.";
            }
            catch(OperationCanceledException)
            {
                activity.Text="Gemini phản hồi quá chậm. Kiểm tra Internet rồi thử lại.";
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
        var head=Card(Stack(
            Row(T("✦  GEMINI CHAT  /  TRỢ LÝ HISA",19,true),
                B("⚙  Gemini API Key",()=>Navigate("settings")),
                B("Xóa hội thoại",ClearChat)),
            T("Gemini hiểu lịch xuất, nguồn kiện và công việc trong ngày khi bạn chủ động bật chia sẻ dữ liệu. Mic chỉ thu một câu khi bấm, lời nói được nhận dạng cục bộ trên Windows; loa đọc câu trả lời bằng giọng hệ thống. AI đề xuất sửa Excel riêng, phải xem trước và xác nhận.",12,false,Muted),
            contextToggle,privacyNote,autoVoice,
            Row(listen,speak,stopVoice),
            T("Chỉ bật chia sẻ dữ liệu khi muốn tra cứu hợp đồng, vật tư hoặc kế hoạch thực tế từ HISA.",11,false,Muted)
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
        SetPage("Trợ lý Gemini AI","Mic & loa Windows · hiểu kế hoạch hằng ngày · đề xuất sửa Excel có phê duyệt",layout);
        ScrollToEnd();
    }
}
