using System.IO;
using System.Text.Json;
using NAudio.Wave;

namespace Hisa.App;

/// <summary>Offline checks: no API key, microphone, Internet or speaker hardware.</summary>
public static class GeminiVietnameseTtsDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool good,string label)
        {
            if(!good)throw new InvalidOperationException("Gemini Vietnamese TTS: "+label);
            checks.Add("Gemini Vietnamese TTS: "+label);
        }
        const string spoken="Xin chào, tôi đang kiểm tra hợp đồng 56062026.";
        var request=GeminiVietnameseTts.BuildRequestJson(spoken);
        using(var parsed=JsonDocument.Parse(request))
        {
            var root=parsed.RootElement;
            Check(root.GetProperty("model").GetString()==GeminiVietnameseTts.Model,
                "Uses dedicated Gemini TTS model not chat model");
            Check(root.GetProperty("input")[0].GetProperty("content")[0]
                    .GetProperty("text").GetString()==spoken,
                "Voice receives original Vietnamese text verbatim");
            Check(root.GetProperty("response_format").GetProperty("mime_type")
                .GetString()=="audio/wav","Requests playable WAV rather than raw unlabelled PCM");
            Check(root.GetProperty("generation_config").GetProperty("speech_config")[0]
                    .GetProperty("voice").GetString()=="Leda",
                "Uses consistent youthful prebuilt voice");
        }
        byte[] wave;
        using(var stream=new MemoryStream())
        {
            using(var writer=new WaveFileWriter(stream,new WaveFormat(24000,16,1)))
                writer.Write(new byte[48000],0,48000);
            wave=stream.ToArray();
        }
        var response=JsonSerializer.Serialize(new
        {
            output_audio=new {data=Convert.ToBase64String(wave)}
        });
        Check(GeminiVietnameseTts.DecodeAudio(response).SequenceEqual(wave),
            "Decode real WAV audio from Gemini output_audio");
        var nested=JsonSerializer.Serialize(new
        {
            steps=new[] {new{content=new[]{new{type="audio",
                data=Convert.ToBase64String(wave)}}}}
        });
        Check(GeminiVietnameseTts.DecodeAudio(nested).SequenceEqual(wave),
            "Decode audio from interaction model-output steps");
        bool bad=false;
        try{GeminiVietnameseTts.DecodeAudio("""{"output_audio":{"data":"aGVsbG8="}}""");}
        catch(InvalidDataException){bad=true;}
        Check(bad,"Unplayable or truncated audio is rejected");
        bad=false;
        try{GeminiVietnameseTts.DecodeAudio("""{"steps":[]}""");}
        catch(FormatException){bad=true;}
        Check(bad,"Missing audio has explicit TTS provider error");
        Check(!GeminiVoiceConsentStore.Read() || File.Exists(GeminiVoiceConsentStore.ConfigPath),
            "Microphone consent defaults to off unless explicitly stored");
        return checks;
    }
}
