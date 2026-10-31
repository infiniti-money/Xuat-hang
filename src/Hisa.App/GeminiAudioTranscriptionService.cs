using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// An explicitly approved microphone clip is sent directly to the existing
/// Gemini API. No file is stored on disk or attached to warehouse history.
/// Vietnamese recognition does not depend on an installed Windows vi-VN SAPI.
/// </summary>
public static class GeminiAudioTranscriptionService
{
    private static readonly HttpClient Http = new() { Timeout=TimeSpan.FromSeconds(90) };
    public const int MaxWavBytes = 2_000_000;
    public const string MimeType = "audio/wav";

    public static string BuildRequestJson(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if(wav.Length < 6000 || wav.Length > MaxWavBytes)
            throw new InvalidOperationException(
                "Bản ghi âm quá ngắn hoặc quá dài. Chỉ thu tối đa 25 giây.");
        if(wav.Length<12 || Encoding.ASCII.GetString(wav,0,4)!="RIFF" ||
            Encoding.ASCII.GetString(wav,8,4)!="WAVE")
            throw new InvalidOperationException("Định dạng ghi âm không hợp lệ (cần WAV).");
        var parts=new object[]
        {
            new { text="Chép chính xác câu nói tiếng Việt trong âm thanh thành văn bản. " +
                "Giữ nguyên số hợp đồng, tên dự án và chữ viết tắt (HISA, cont, T1, T2, Hưng Yên). " +
                "Không tự thêm dữ liệu hoặc trả lời câu hỏi. Chỉ xuất nội dung lời nói." },
            new { inline_data=new { mime_type=MimeType, data=Convert.ToBase64String(wav) } }
        };
        var payload=new
        {
            contents=new[] { new { role="user",parts } },
            generationConfig=new { maxOutputTokens=1024,temperature=0 }
        };
        return JsonSerializer.Serialize(payload);
    }

    public static async Task<string> TranscribeAsync(
        string apiKey,byte[] wav,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Chưa có Gemini API Key. Vào Cấu hình → Gemini để nhập khóa.");
        var json=BuildRequestJson(wav);
        using var request=new HttpRequestMessage(HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/models/"+
            GeminiChatService.Model+":generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key",apiKey);
        request.Content=new StringContent(json,Encoding.UTF8,"application/json");
        using var response=await Http.SendAsync(
            request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        var body=await response.Content.ReadAsStringAsync(cancellationToken);
        if(!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AiScheduleReader.DiagnoseApiFailure(
                "Gemini nhận dạng tiếng nói",(int)response.StatusCode,body));
        var transcript=GeminiChatService.ParseReply(body).Trim();
        if(transcript.Length>GeminiChatService.MaxQuestionLength)
            throw new InvalidOperationException(
                "Nội dung nhận dạng vượt giới hạn 3.000 ký tự. Hãy nói câu ngắn hơn.");
        return transcript;
    }
}
