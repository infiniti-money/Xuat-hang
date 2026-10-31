using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace Hisa.App;

/// <summary>
/// Local Windows Speech API. The microphone runs only after an explicit user click.
/// Audio is recognized by the installed Windows language engine, not uploaded to
/// Gemini; only the resulting text is sent if the user presses Send.
/// </summary>
public sealed class GeminiVoice : IDisposable
{
    private SpeechRecognitionEngine? _microphone;
    private SpeechSynthesizer? _speaker;

    public void Listen(Action<string> recognized,Action<string> status)
    {
        StopListening();
        var vietnamese=SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(x=>x.Culture.Name.Equals("vi-VN",StringComparison.OrdinalIgnoreCase));
        if(vietnamese==null)
        {
            status("Windows chưa cài bộ nhận dạng giọng nói tiếng Việt (vi-VN). Hãy cài gói ngôn ngữ/nhận dạng tiếng Việt trong Windows rồi thử lại.");
            return;
        }
        var engine=new SpeechRecognitionEngine(vietnamese);
        try
        {
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            _microphone=engine;
            engine.SpeechRecognized+=(_,e)=>
            {
                if(e.Result.Confidence>=0.35 && !string.IsNullOrWhiteSpace(e.Result.Text))
                    recognized(e.Result.Text);
            };
            engine.RecognizeCompleted+=(_,e)=>
            {
                status(e.Error==null?"Đã dừng nhận giọng nói. Kiểm tra câu hỏi trước khi gửi.":
                    "Mic Windows: "+e.Error.Message);
                StopListening();
            };
            engine.RecognizeAsync(RecognizeMode.Single);
            status("Đang nghe mic hệ thống. Nói một câu rồi kiểm tra nội dung trước khi gửi.");
        }
        catch
        {
            _microphone=null;
            engine.Dispose();
            throw;
        }
    }

    public void StopListening()
    {
        var engine=_microphone;
        _microphone=null;
        if(engine==null)return;
        try { engine.RecognizeAsyncCancel(); }
        catch(InvalidOperationException) { }
        // Avoid disposing inside a native RecognizeCompleted event callback.
        // Engine is disposed on a later explicit action/application cleanup.
    }

    public void Speak(string text,Action<string> status)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        StopSpeaking();
        var synth=new SpeechSynthesizer();
        try
        {
            synth.SetOutputToDefaultAudioDevice();
            var vi=synth.GetInstalledVoices().FirstOrDefault(x=>x.Enabled &&
                x.VoiceInfo.Culture.Name.Equals("vi-VN",StringComparison.OrdinalIgnoreCase));
            if(vi!=null)synth.SelectVoice(vi.VoiceInfo.Name);
            _speaker=synth;
            synth.SpeakCompleted+=(_,e)=>status(e.Error==null?
                "Đã đọc xong bằng loa hệ thống.":"Không thể phát lời nói: "+e.Error.Message);
            synth.SpeakAsync(text.Length>3000?text[..3000]:text);
            status(vi==null?"Đang phát bằng giọng mặc định Windows (chưa có giọng tiếng Việt).":
                "Đang phát tiếng Việt qua loa hệ thống.");
        }
        catch { _speaker=null;synth.Dispose();throw; }
    }

    public void StopSpeaking()
    {
        if(_speaker==null)return;
        try {_speaker.SpeakAsyncCancelAll();}catch(InvalidOperationException){}
        _speaker.Dispose();
        _speaker=null;
    }

    public void Dispose()
    {
        StopListening();
        StopSpeaking();
    }
}
