using System.IO;
using NAudio.Wave;

namespace Hisa.App;

/// <summary>
/// Captures one short WAV clip from the default Windows microphone.
/// No microphone is opened until the user explicitly clicks and consents.
/// No audio is written to disk; caller decides whether to upload the WAV.
/// </summary>
public sealed class GeminiMicrophoneRecorder : IDisposable
{
    public const int MaximumSeconds = 25;
    private const int Rate = 16000;
    private const int BytesPerSecond = Rate * 2;
    private readonly object _gate = new();
    private WaveInEvent? _input;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private Action<byte[]>? _completed;
    private Action<string>? _status;
    private int _recorded;
    private bool _submit;
    private bool _handsFree;
    private HandsFreeVoiceActivity? _activity;
    private readonly Queue<byte[]> _preRoll = new();
    private bool _disposed;

    public bool IsRecording { get { lock (_gate) return _input != null; } }

    public void Start(Action<byte[]> completed, Action<string> status) =>
        StartCore(completed,status,false);

    /// <summary>Start VAD mode only after explicit hands-free session consent.</summary>
    public void StartHandsFree(Action<byte[]> completed,Action<string> status) =>
        StartCore(completed,status,true);

    private void StartCore(Action<byte[]> completed, Action<string> status,bool handsFree)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if (_input != null) throw new InvalidOperationException("Micro đang thu âm.");
            if (WaveIn.DeviceCount == 0)
                throw new InvalidOperationException("Windows không tìm thấy micro. Kiểm tra System → Sound → Input.");

            var input = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(Rate,16,1),
                BufferMilliseconds = 100
            };
            var stream = new MemoryStream();
            var writer = new WaveFileWriter(stream,input.WaveFormat);
            input.DataAvailable += OnDataAvailable;
            input.RecordingStopped += OnRecordingStopped;
            _input = input; _buffer = stream; _writer = writer;
            _completed = completed; _status = status;
            _recorded = 0; _submit = false;
            _handsFree = handsFree;
            _activity = handsFree ? new HandsFreeVoiceActivity() : null;
            _preRoll.Clear();
            try { input.StartRecording(); }
            catch
            {
                _input = null; _buffer = null; _writer = null;
                _completed = null; _status = null;
                input.Dispose(); writer.Dispose(); stream.Dispose();
                throw;
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        WaveInEvent? stop = null;
        lock (_gate)
        {
            if (_writer == null || !ReferenceEquals(sender,_input)) return;
            if (_handsFree)
            {
                var detector=_activity!;
                var wasSpeaking=detector.HasSpeech;
                detector.Feed(e.Buffer.AsSpan(0,e.BytesRecorded));
                if(!wasSpeaking)
                {
                    // Keep only 300-400ms in memory to preserve initial consonants,
                    // even while listening idle for an extended time.
                    _preRoll.Enqueue(e.Buffer.AsSpan(0,e.BytesRecorded).ToArray());
                    while(_preRoll.Count>4)_preRoll.Dequeue();
                    if(detector.HasSpeech)
                    {
                        foreach(var block in _preRoll)
                        {
                            var bytes=Math.Min(block.Length,MaximumSeconds*BytesPerSecond-_recorded);
                            if(bytes>0){_writer.Write(block,0,bytes);_recorded+=bytes;}
                        }
                        _preRoll.Clear();
                    }
                }
                else
                {
                    var bytes=Math.Min(e.BytesRecorded,MaximumSeconds*BytesPerSecond-_recorded);
                    if(bytes>0){_writer.Write(e.Buffer,0,bytes);_recorded+=bytes;}
                }
                if(detector.Finished || _recorded>=MaximumSeconds*BytesPerSecond)
                {
                    _submit=detector.HasSpeech && _recorded>0;
                    stop=_input;
                }
            }
            else
            {
                var length=Math.Min(e.BytesRecorded,MaximumSeconds*BytesPerSecond-_recorded);
                if(length>0){_writer.Write(e.Buffer,0,length);_recorded+=length;}
                if(_recorded>=MaximumSeconds*BytesPerSecond)
                {
                    _submit=true;stop=_input;
                }
            }
        }
        if(stop!=null)
        {
            try {stop.StopRecording();}
            catch(InvalidOperationException) { }
        }
    }

    public void Stop(bool sendForTranscription)
    {
        WaveInEvent? input;
        lock (_gate)
        {
            input = _input;
            if (input == null) return;
            _submit = sendForTranscription;
        }
        try { input.StopRecording(); }
        catch (InvalidOperationException) { }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        WaveInEvent? input;
        WaveFileWriter? writer;
        MemoryStream? stream;
        Action<byte[]>? callback;
        Action<string>? status;
        bool send;
        int count;
        lock (_gate)
        {
            if (!ReferenceEquals(sender,_input)) return;
            input = _input; writer = _writer; stream = _buffer;
            callback = _completed; status = _status; send = _submit;
            count = _recorded;
            _input = null; _writer = null; _buffer = null;
            _completed = null; _status = null; _submit = false; _recorded = 0;
            _handsFree = false; _activity = null; _preRoll.Clear();
        }
        try
        {
            writer?.Dispose(); // Finish WAV header before sending.
            if (e.Exception != null) status?.Invoke(
                "Windows không thu được âm thanh. Kiểm tra quyền micro và thiết bị đầu vào: " +
                e.Exception.Message);
            else if (!send) status?.Invoke("Đã hủy thu âm. Không gửi dữ liệu đến Gemini.");
            else if (count < BytesPerSecond/3)
                status?.Invoke("Âm thanh quá ngắn. Hãy ghi âm ít nhất một giây.");
            else if (stream != null) callback?.Invoke(stream.ToArray());
        }
        finally
        {
            input?.Dispose();
            stream?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop(false);
    }
}
