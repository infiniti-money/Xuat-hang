namespace Hisa.App;

/// <summary>
/// Small deterministic voice activity detector for 16-bit mono PCM, in ~100ms blocks.
/// Does not decide speech content or upload audio. Conservative noise floor and
/// 1.2s silence guard reduce accidental speech segmentation.
/// </summary>
public sealed class HandsFreeVoiceActivity
{
    public const double VoiceRmsThreshold=0.018;
    public const int MinVoiceBlocks=2;
    public const int EndSilenceBlocks=12;
    private int _voiceBlocks;
    private int _silenceBlocks;
    public bool HasSpeech {get;private set;}
    public bool Finished {get;private set;}

    public void Feed(ReadOnlySpan<byte> pcm)
    {
        if(Finished || pcm.Length<2)return;
        double energy=0;
        int count=pcm.Length/2;
        for(int n=0;n<count;n++)
        {
            var sample=(short)(pcm[n*2]|(pcm[n*2+1]<<8));
            var amplitude=sample/32768.0;
            energy+=amplitude*amplitude;
        }
        bool voice=Math.Sqrt(energy/count)>=VoiceRmsThreshold;
        if(!HasSpeech)
        {
            _voiceBlocks=voice?_voiceBlocks+1:0;
            if(_voiceBlocks>=MinVoiceBlocks)HasSpeech=true;
            return;
        }
        _silenceBlocks=voice?0:_silenceBlocks+1;
        if(_silenceBlocks>=EndSilenceBlocks)Finished=true;
    }
}
