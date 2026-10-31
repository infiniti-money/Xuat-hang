using System.Speech.Synthesis;

namespace Hisa.App;

/// <summary>
/// Windows speaker output only. Vietnamese speech-to-text now uses explicit
/// WAV microphone capture and Gemini, not a local Windows vi-VN SAPI engine.
/// </summary>
public sealed class GeminiVoice : IDisposable
{
    private SpeechSynthesizer? _speaker;
    private volatile bool _isSpeaking;
    public bool IsSpeaking => _isSpeaking;

    public void Speak(string text,Action<string> status,Action? onFinished=null)
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
            synth.SpeakCompleted+=(_,e)=>
            {
                status(e.Error==null?"Đã đọc xong bằng loa hệ thống.":
                    "Không thể phát lời nói: "+e.Error.Message);
                _isSpeaking=false;
                if(!e.Cancelled && e.Error==null)onFinished?.Invoke();
            };
            _isSpeaking=true;
            synth.SpeakAsync(text.Length>3000?text[..3000]:text);
            status(vi==null?"Đang phát bằng giọng mặc định Windows (chưa có giọng tiếng Việt).":
                "Đang phát tiếng Việt qua loa hệ thống.");
        }
        catch { _isSpeaking=false;_speaker=null;synth.Dispose();throw; }
    }

    public void StopSpeaking()
    {
        _isSpeaking=false;
        if(_speaker==null)return;
        try {_speaker.SpeakAsyncCancelAll();}catch(InvalidOperationException){}
        _speaker.Dispose();
        _speaker=null;
    }

    public void Dispose() => StopSpeaking();
}
