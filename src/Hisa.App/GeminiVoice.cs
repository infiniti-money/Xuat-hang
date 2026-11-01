using System.IO;
using System.Speech.Synthesis;
using NAudio.Wave;

namespace Hisa.App;

/// <summary>Plays verified Vietnamese speech. Never falls back to an English voice.</summary>
public sealed class GeminiVoice : IDisposable
{
    private SpeechSynthesizer? _speaker;
    private WaveOutEvent? _player;
    private MemoryStream? _audioStream;
    private WaveFileReader? _reader;
    private volatile bool _isSpeaking;
    private long _epoch;
    public bool IsSpeaking=>_isSpeaking;

    // Existing offline TTS is allowed only if a genuine vi-VN voice exists.
    public void Speak(string text,Action<string> status,Action? onFinished=null)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        StopSpeaking();
        var synth=new SpeechSynthesizer();
        try
        {
            var voice=synth.GetInstalledVoices().FirstOrDefault(x=>x.Enabled &&
                x.VoiceInfo.Culture.Name.Equals("vi-VN",StringComparison.OrdinalIgnoreCase));
            if(voice==null)throw new InvalidOperationException(
                "Windows không có giọng đọc tiếng Việt. Hãy dùng giọng Gemini TTS trong HISA.");
            synth.SetOutputToDefaultAudioDevice();
            synth.SelectVoice(voice.VoiceInfo.Name);
            _speaker=synth;
            _isSpeaking=true;
            synth.SpeakCompleted+=(_,e)=>
            {
                _isSpeaking=false;
                status(e.Error==null?"Đã đọc xong bằng tiếng Việt.":"Loa Windows: "+e.Error.Message);
                if(!e.Cancelled && e.Error==null)onFinished?.Invoke();
            };
            synth.SpeakAsync(text.Length>3000?text[..3000]:text);
            status("Đang phát tiếng Việt bằng giọng Windows vi-VN.");
        }
        catch
        {
            _speaker=null;_isSpeaking=false;synth.Dispose();throw;
        }
    }

    public async Task SpeakGeminiAsync(string key,string text,
        Action<string> status,Action? onFinished,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        StopSpeaking();
        var session=Interlocked.Read(ref _epoch);
        _isSpeaking=true;
        try
        {
            status("Gemini đang tạo giọng đọc tiếng Việt…");
            var wav=await GeminiVietnameseTts.SynthesizeAsync(key,text,token);
            token.ThrowIfCancellationRequested();
            if(session!=Interlocked.Read(ref _epoch))return;
            var stream=new MemoryStream(wav,false);
            var reader=new WaveFileReader(stream);
            var player=new WaveOutEvent();
            try
            {
                player.Init(reader);
                if(session!=Interlocked.Read(ref _epoch) || token.IsCancellationRequested)
                {
                    player.Dispose();reader.Dispose();stream.Dispose();return;
                }
                _audioStream=stream;_reader=reader;_player=player;
                player.PlaybackStopped+=(_,e)=>
                {
                    if(session!=Interlocked.Read(ref _epoch))return;
                    _isSpeaking=false;
                    status(e.Exception==null?"Đã phát xong câu trả lời bằng tiếng Việt.":
                        "Không phát được loa tiếng Việt: "+e.Exception.Message);
                    if(e.Exception==null && !token.IsCancellationRequested)onFinished?.Invoke();
                };
                player.Play();
                status("● Gemini đang phát giọng tiếng Việt · micro đã tạm dừng.");
            }
            catch {player.Dispose();reader.Dispose();stream.Dispose();throw;}
        }
        catch
        {
            if(session==Interlocked.Read(ref _epoch))_isSpeaking=false;
            throw;
        }
    }

    public void StopSpeaking()
    {
        Interlocked.Increment(ref _epoch);
        _isSpeaking=false;
        var player=_player;
        _player=null;
        try{player?.Stop();}catch(InvalidOperationException){}
        player?.Dispose();
        _reader?.Dispose();_reader=null;
        _audioStream?.Dispose();_audioStream=null;
        if(_speaker is {} synth)
        {
            _speaker=null;
            try{synth.SpeakAsyncCancelAll();}catch(InvalidOperationException){}
            synth.Dispose();
        }
    }
    public void Dispose()=>StopSpeaking();
}
