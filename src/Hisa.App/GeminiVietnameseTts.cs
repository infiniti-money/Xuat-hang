using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace Hisa.App;

/// <summary>
/// Vietnamese TTS on the user's existing Gemini API credential. Unlike Windows
/// SpeechSynthesizer, this does not fall back to an English-language voice.
/// Synthesized WAV is held in RAM only.
/// </summary>
public static class GeminiVietnameseTts
{
    public const string Model="gemini-3.8-flash-lite-tts";
    private static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(90)};
    public const int MaxCharacters=2200;
    public const int MaxWavSize=14*1024*1024;

    public static string BuildRequestJson(string vietnamese)
    {
        if(string.IsNullOrWhiteSpace(vietnamese))
            throw new InvalidOperationException("Không có câu trả lời để đọc.");
        if(vietnamese.Length>MaxCharacters)
            vietnamese=vietnamese[..MaxCharacters];
        var request=new
        {
            model=Model,
            input=new[]
            {
                new
                {
                    type="user_input",
                    content=new[]
                    {
                        new
                        {
                            type="text",
                            text=vietnamese,
                            annotations=new[]
                            {
                                new {type="speech_metadata",
                                    style="Speak VERBATIM in Vietnamese (vi-VN), as a gentle, warm young adult female Vietnamese speaker called Mây Mây. Use a natural Southern Vietnam (miền Nam, Sài Gòn) accent, with soft, calm and friendly intonation, a moderate unhurried pace and clear pronunciation. Do not change the words or speak English."}
                            }
                        }
                    }
                }
            },
            response_format=new {type="audio",mime_type="audio/wav"},
            generation_config=new {speech_config=new[]{new{voice="Leda"}}}
        };
        return JsonSerializer.Serialize(request);
    }

    public static byte[] DecodeAudio(string json)
    {
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement;
        string? base64=null;
        if(root.TryGetProperty("output_audio",out var output) &&
           output.ValueKind==JsonValueKind.Object &&
           output.TryGetProperty("data",out var d) && d.ValueKind==JsonValueKind.String)
            base64=d.GetString();
        if(base64==null && root.TryGetProperty("steps",out var steps) &&
            steps.ValueKind==JsonValueKind.Array)
        {
            foreach(var step in steps.EnumerateArray())
            {
                if(!step.TryGetProperty("content",out var content) ||
                    content.ValueKind!=JsonValueKind.Array)continue;
                foreach(var part in content.EnumerateArray())
                {
                    if(part.TryGetProperty("type",out var t) && t.GetString()=="audio" &&
                       part.TryGetProperty("data",out var v) && v.ValueKind==JsonValueKind.String)
                        base64=v.GetString();
                }
            }
        }
        if(string.IsNullOrEmpty(base64))
            throw new FormatException("Gemini chưa trả âm thanh tiếng Việt. Kiểm tra hạn mức TTS và API Key.");
        byte[] bytes;
        try{bytes=Convert.FromBase64String(base64);}
        catch(FormatException){throw new FormatException("Gemini trả dữ liệu âm thanh không hợp lệ.");}
        if(bytes.Length<44 || bytes.Length>MaxWavSize)
            throw new InvalidDataException("Âm thanh tiếng Việt vượt giới hạn hoặc bị thiếu dữ liệu.");
        using var stream=new MemoryStream(bytes,false);
        try
        {
            using var reader=new WaveFileReader(stream);
            if(reader.Length==0 || reader.WaveFormat.Channels<1)
                throw new InvalidDataException("Gemini trả WAV rỗng.");
        }
        catch(Exception e) when(e is InvalidDataException or FormatException or
            EndOfStreamException or NotSupportedException)
        {
            throw new InvalidDataException("Gemini không trả được định dạng WAV tiếng Việt hợp lệ.");
        }
        return bytes;
    }

    public static async Task<byte[]> SynthesizeAsync(string key,string vietnamese,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Chưa cấu hình Gemini API Key.");
        using var request=new HttpRequestMessage(HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/interactions");
        request.Headers.TryAddWithoutValidation("x-goog-api-key",key);
        request.Content=new StringContent(BuildRequestJson(vietnamese),Encoding.UTF8,"application/json");
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        var body=await response.Content.ReadAsStringAsync(token);
        if(!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AiScheduleReader.DiagnoseApiFailure(
                "Gemini TTS tiếng Việt",(int)response.StatusCode,body));
        return DecodeAudio(body);
    }
}
