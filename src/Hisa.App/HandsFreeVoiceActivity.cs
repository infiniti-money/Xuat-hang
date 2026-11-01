namespace Hisa.App;

/// <summary>
/// Conservative, offline voice activity detector for 16 kHz 16-bit mono PCM.
/// Allows lower-gain microphones; does not make API requests or store audio.
/// </summary>
public sealed class HandsFreeVoiceActivity
{
    public const double DefaultThreshold=0.0045; // ~-47 dBFS, was ~-35 dBFS.
    public const int MinVoiceBlocks=2;
    public const int EndSilenceBlocks=12;
    private const int SampleRate=16000;
    private const int ReferenceBlockSamples=1600; // 100 ms
    private readonly double _threshold;
    private int _voiceSamples;
    private int _silenceSamples;
    public double CurrentRms {get;private set;}
    public double Threshold => _threshold;
    public bool HasSpeech {get;private set;}
    public bool Finished {get;private set;}

    /// <param name="threshold">0.002 = very sensitive; 0.0045 = normal; 0.01 = low sensitivity.</param>
    public HandsFreeVoiceActivity(double threshold=DefaultThreshold)
    {
        if(threshold < 0.001 || threshold > 0.1)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        _threshold=threshold;
    }

    public static double MeasureRms(ReadOnlySpan<byte> pcm)
    {
        if(pcm.Length<2)return 0;
        double energy=0;
        var count=pcm.Length/2;
        for(int n=0;n<count;n++)
        {
            var sample=(short)(pcm[n*2]|(pcm[n*2+1]<<8));
            var amplitude=sample/32768.0;
            energy+=amplitude*amplitude;
        }
        return Math.Sqrt(energy/count);
    }

    public void Feed(ReadOnlySpan<byte> pcm)
    {
        if(Finished || pcm.Length<2)return;
        CurrentRms=MeasureRms(pcm);
        var count=pcm.Length/2;
        var voice=CurrentRms>=_threshold;
        if(!HasSpeech)
        {
            _voiceSamples=voice?_voiceSamples+count:0;
            if(_voiceSamples>=MinVoiceBlocks*ReferenceBlockSamples)HasSpeech=true;
            return;
        }
        _silenceSamples=voice?0:_silenceSamples+count;
        if(_silenceSamples>=EndSilenceBlocks*ReferenceBlockSamples)Finished=true;
    }
}
