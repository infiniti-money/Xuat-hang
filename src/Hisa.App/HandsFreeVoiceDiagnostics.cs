namespace Hisa.App;

/// <summary>Deterministic offline tests. Never open microphone, speakers or Gemini.</summary>
public static class HandsFreeVoiceDiagnostics
{
    private static byte[] Frame(short amplitude)
    {
        var bytes=new byte[3200]; // 100ms of 16kHz mono 16-bit PCM.
        for(int i=0;i<bytes.Length;i+=2)
        {
            bytes[i]=(byte)(amplitude&255);
            bytes[i+1]=(byte)((amplitude>>8)&255);
        }
        return bytes;
    }

    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool condition,string label)
        {
            if(!condition)throw new InvalidOperationException("Hands-free: "+label);
            checks.Add("Hands-free: "+label);
        }
        var empty=Frame(0);
        var quiet=Frame(120);
        var loud=Frame(2400);
        var tracker=new HandsFreeVoiceActivity();
        for(int i=0;i<100;i++)tracker.Feed(empty);
        Check(!tracker.HasSpeech && !tracker.Finished,
            "Idle silence never starts an upload or closes microphone");
        tracker.Feed(loud);
        Check(!tracker.HasSpeech,"One isolated spike cannot trigger voice upload");
        tracker.Feed(empty);
        Check(!tracker.HasSpeech,"A noise spike resets voice onset");
        tracker.Feed(quiet);
        tracker.Feed(quiet);
        Check(!tracker.HasSpeech,"Low background noise is ignored");
        tracker.Feed(loud);
        tracker.Feed(loud);
        Check(tracker.HasSpeech && !tracker.Finished,
            "Two voiced frames activate speech");
        for(int i=0;i<HandsFreeVoiceActivity.EndSilenceBlocks-1;i++)tracker.Feed(empty);
        Check(!tracker.Finished,
            "Short pauses do not cut a sentence");
        tracker.Feed(loud);
        Check(!tracker.Finished,"New speech resets silence counter");
        for(int i=0;i<HandsFreeVoiceActivity.EndSilenceBlocks;i++)tracker.Feed(empty);
        Check(tracker.Finished,"Sustained silence ends a spoken turn");
        tracker.Feed(loud);
        Check(tracker.Finished,"Completed turn stays complete without implicit restart");
        return checks;
    }
}
