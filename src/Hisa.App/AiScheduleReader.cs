using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hisa.App;

// Import is deliberately read-only: extraction never changes inventory or shipment ledgers.
// The operator must inspect the two sections and explicitly open the existing plan editor.
public sealed class AiScheduleResult
{
    public string Date { get; set; } = "";
    public List<AiIncomingContainer> Incoming { get; set; } = [];
    public List<AiOutgoingRow> Outgoing { get; set; } = [];
    [JsonIgnore] public List<string> Warnings { get; set; } = [];
    [JsonIgnore] public string Providers { get; set; } = "";
}

public sealed class AiIncomingContainer
{
    public string Container { get; set; } = "";
    // This date comes from the source container header, NOT the warehouse receiving date.
    public string SourceDate { get; set; } = "";
    public List<AiIncomingRow> Rows { get; set; } = [];
}

public sealed class AiIncomingRow
{
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Urgent { get; set; }
}

public sealed class AiOutgoingRow
{
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Note { get; set; } = "";
    // "Đã có lx" is a schedule marker, NEVER a driver assignment.
    public bool HasSchedule { get; set; }
    [JsonIgnore] public bool Selected { get; set; }
    [JsonIgnore] public bool Urgent { get; set; }
    [JsonIgnore] public string ScheduleLabel => HasSchedule ? "Đã có lịch xuất" : "Chưa rõ trạng thái lịch xuất";
    [JsonIgnore] public string UrgentLabel => Urgent ? "ƯU TIÊN" : "";
}

public static class AiScheduleReader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal const string Instruction = """
        Đọc ảnh bảng Excel kế hoạch kho HISA Hưng Yên, trả về DUY NHẤT một JSON object:
        {"date":"yyyy-MM-dd","incoming":[{"container":"số container","sourceDate":"yyyy-MM-dd hoặc chuỗi rỗng","rows":[{"contract":"","project":"","company":"","note":"","urgent":false}]}],
        "outgoing":[{"contract":"","project":"","company":"","note":"","hasSchedule":false}]}.
        Có HAI phần: (1) "Hàng dự kiến nhập kho HY" chứa các container và nhiều công trình; (2) "Hàng dự kiến xuất kho HY" chứa lịch xuất dự kiến.
        date là ngày kế hoạch ở dải tiêu đề MÀU VÀNG; không lấy "Ngày Xuất Hàng" nằm trong từng container. sourceDate là ngày xuất tại nguồn trong container; KHÔNG tự coi là ngày hàng thực nhận tại kho.
        Giữ chính xác mã hợp đồng dạng chuỗi, số 0 đầu, tên công trình, công ty và ghi chú. Mỗi dòng công trình là một row; bỏ dòng tiêu đề và "VT trả nợ", "VT gửi kho HY" nếu không có hợp đồng.
        "Khách cần gấp" -> urgent=true ở phần incoming. "Đã có lx" nghĩa là đã có LỊCH XUẤT, KHÔNG phải lái xe -> hasSchedule=true. Nếu ghi chú không nêu lịch xuất thì hasSchedule=false (chỉ có nghĩa chưa rõ, không có nghĩa bị hủy).
        Không tự bịa dữ liệu, ngày, số lượng hay lái xe; ô không đọc được để trống. Không thêm mã không nhìn thấy trong ảnh. Trả JSON hợp lệ, không markdown.
        """;

    public static AiScheduleResult Parse(string json)
    {
        var parsed = JsonSerializer.Deserialize<AiScheduleResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("AI không trả về bảng hợp lệ.");
        if (!DateTime.TryParseExact(parsed.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
              System.Globalization.DateTimeStyles.None, out _))
            throw new InvalidOperationException("AI chưa đọc đúng ngày kế hoạch (yyyy-MM-dd). Cần kiểm tra lại ảnh.");
        parsed.Incoming ??= [];
        parsed.Outgoing ??= [];
        if (parsed.Incoming.Count > 100 || parsed.Incoming.Sum(x => x.Rows?.Count ?? 0) > 500 || parsed.Outgoing.Count > 500)
            throw new InvalidOperationException("Số dòng đọc được vượt mức cho phép; hãy chia ảnh nhỏ hơn.");
        foreach (var cont in parsed.Incoming)
        {
            cont.Container = (cont.Container ?? "").Trim();
            cont.SourceDate = (cont.SourceDate ?? "").Trim();
            cont.Rows ??= [];
            foreach (var row in cont.Rows)
            {
                row.Contract = (row.Contract ?? "").Trim();
                row.Project = (row.Project ?? "").Trim();
                row.Company = (row.Company ?? "").Trim();
                row.Note = (row.Note ?? "").Trim();
                if (row.Note.Contains("khách cần gấp", StringComparison.OrdinalIgnoreCase)) row.Urgent = true;
            }
        }
        foreach (var row in parsed.Outgoing)
        {
            row.Contract = (row.Contract ?? "").Trim();
            row.Project = (row.Project ?? "").Trim();
            row.Company = (row.Company ?? "").Trim();
            row.Note = (row.Note ?? "").Trim();
            if (row.Note.Contains("đã có lx", StringComparison.OrdinalIgnoreCase)) row.HasSchedule = true;
            row.Urgent = parsed.Incoming.SelectMany(x => x.Rows)
                .Any(x => x.Urgent && Rules.Contract(x.Contract).Length > 0
                     && Rules.Contract(x.Contract) == Rules.Contract(row.Contract));
        }
        if (parsed.Outgoing.Count == 0 && parsed.Incoming.Sum(x => x.Rows.Count) == 0)
            throw new InvalidOperationException("Không tìm thấy hai bảng. Hãy chọn ảnh rõ hơn.");
        return parsed;
    }

    public static async Task<AiScheduleResult> AnalyzeAsync(string imagePath, string openAiKey, string geminiKey,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Không tìm thấy ảnh đã chọn.");
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        var mime = extension switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => "" };
        if (mime.Length == 0) throw new InvalidOperationException("Chỉ hỗ trợ ảnh JPG, PNG, WEBP.");
        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        if (bytes.Length == 0 || bytes.Length > 7 * 1024 * 1024)
            throw new InvalidOperationException("Ảnh phải có dung lượng lớn hơn 0 và không vượt 7 MB.");
        if (string.IsNullOrWhiteSpace(openAiKey) && string.IsNullOrWhiteSpace(geminiKey))
            throw new InvalidOperationException("Cần ít nhất một API key OpenAI hoặc Gemini để đọc ảnh qua Internet.");

        var base64 = Convert.ToBase64String(bytes);
        AiScheduleResult? primary = null;
        AiScheduleResult? secondary = null;
        var warnings = new List<string>();
        var providers = new List<string>();
        if (!string.IsNullOrWhiteSpace(openAiKey))
        {
            try { primary = Parse(await CallOpenAiAsync(base64, mime, openAiKey.Trim(), cancellationToken)); providers.Add("GPT"); }
            catch (Exception e) when (e is not OperationCanceledException)
            { warnings.Add("GPT chưa đọc được ảnh: " + SafeError(e)); }
        }
        if (!string.IsNullOrWhiteSpace(geminiKey))
        {
            try
            {
                var parsed = Parse(await CallGeminiAsync(base64, mime, geminiKey.Trim(), cancellationToken));
                if (primary == null) primary = parsed; else secondary = parsed;
                providers.Add("Gemini");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            { warnings.Add("Gemini chưa đọc được ảnh: " + SafeError(e)); }
        }
        if (primary == null)
            throw new InvalidOperationException("Không có AI nào đọc được ảnh. " + string.Join(" ", warnings));
        if (secondary != null)
        {
            if (primary.Date != secondary.Date) warnings.Add("Hai AI đọc khác ngày kế hoạch; phải đối chiếu ảnh gốc.");
            var first = primary.Outgoing.Select(x => Rules.Contract(x.Contract)).Order(StringComparer.Ordinal).ToArray();
            var second = secondary.Outgoing.Select(x => Rules.Contract(x.Contract)).Order(StringComparer.Ordinal).ToArray();
            if (!first.SequenceEqual(second))
                warnings.Add("GPT và Gemini đọc khác danh sách hợp đồng XUẤT; cần kiểm tra từng dòng trước khi chuyển kế hoạch.");
            var firstIn = primary.Incoming.SelectMany(x => x.Rows).Select(x => Rules.Contract(x.Contract)).Order(StringComparer.Ordinal).ToArray();
            var secondIn = secondary.Incoming.SelectMany(x => x.Rows).Select(x => Rules.Contract(x.Contract)).Order(StringComparer.Ordinal).ToArray();
            if (!firstIn.SequenceEqual(secondIn))
                warnings.Add("GPT và Gemini đọc khác danh sách hợp đồng NHẬP; hãy kiểm tra từng container.");
        }
        primary.Warnings.AddRange(warnings);
        primary.Providers = string.Join(" + ", providers);
        return primary;
    }

    private static string SafeError(Exception e) => e is HttpRequestException ? "lỗi kết nối/API." :
        e is TaskCanceledException ? "quá thời gian." :
        e is JsonException ? "dữ liệu JSON không hợp lệ." : "không thể đọc kết quả; kiểm tra tài khoản, model và ảnh.";

    private static async Task<string> CallOpenAiAsync(string image, string mime, string key, CancellationToken ct)
    {
        var body = new {
            model = "gpt-4.1-mini",
            store = false,
            response_format = new { type = "json_object" },
            messages = new object[] {
                new { role = "user", content = new object[] {
                    new { type = "text", text = Instruction },
                    new { type = "image_url", image_url = new { url = $"data:{mime};base64,{image}", detail = "high" } }
                } }
            }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI HTTP {(int)resp.StatusCode}");
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new JsonException("GPT không có nội dung.");
    }

    private static async Task<string> CallGeminiAsync(string image, string mime, string key, CancellationToken ct)
    {
        var body = new {
            contents = new[] { new { role = "user", parts = new object[] {
                new { text = Instruction },
                new { inline_data = new { mime_type = mime, data = image } }
            } } },
            generationConfig = new { responseMimeType = "application/json", maxOutputTokens = 6000 }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
        req.Headers.Add("x-goog-api-key", key);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini HTTP {(int)resp.StatusCode}");
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString() ?? throw new JsonException("Gemini không có nội dung.");
    }
}
