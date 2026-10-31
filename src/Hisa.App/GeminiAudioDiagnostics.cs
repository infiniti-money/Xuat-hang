using System.Text;
using System.Text.Json;

namespace Hisa.App;

/// <summary>Offline-only tests; never access microphone hardware or send audio to Google.</summary>
public static class GeminiAudioDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool condition,string name)
        {
            if(!condition)throw new InvalidOperationException("FAIL Gemini mic: "+name);
            checks.Add("Gemini mic: "+name);
        }

        var wav=new byte[64044]; // Test fixture only, not a microphone recording.
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav,0);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav,8);
        var json=GeminiAudioTranscriptionService.BuildRequestJson(wav);
        using(var body=JsonDocument.Parse(json))
        {
            var root=body.RootElement;
            var parts=root.GetProperty("contents")[0].GetProperty("parts");
            Check(parts.GetArrayLength()==2 &&
                parts[0].GetProperty("text").GetString()!.Contains("tiếng Việt"),
                "Audio transcript request includes Vietnamese domain-specific instructions");
            var audio=parts[1].GetProperty("inline_data");
            Check(audio.GetProperty("mime_type").GetString()=="audio/wav" &&
                Convert.FromBase64String(audio.GetProperty("data").GetString()!)
                    .SequenceEqual(wav),
                "Only explicit WAV bytes are serialized for the Gemini call");
            Check(!json.Contains("x-goog-api-key") && !json.Contains("API KEY"),
                "No API key is embedded in the audio request payload");
        }

        bool invalid=false;
        try {GeminiAudioTranscriptionService.BuildRequestJson(new byte[64044]);}
        catch(InvalidOperationException){invalid=true;}
        Check(invalid,"Non-WAV bytes are rejected before any request");

        invalid=false;
        try {GeminiAudioTranscriptionService.BuildRequestJson(new byte[GeminiAudioTranscriptionService.MaxWavBytes+1]);}
        catch(InvalidOperationException){invalid=true;}
        Check(invalid,"Oversized audio is rejected before any request");

        invalid=false;
        try{GeminiAudioTranscriptionService.TranscribeAsync("",wav).GetAwaiter().GetResult();}
        catch(InvalidOperationException){invalid=true;}
        Check(invalid,"Missing Gemini credential is rejected offline");

        return checks;
    }
}
