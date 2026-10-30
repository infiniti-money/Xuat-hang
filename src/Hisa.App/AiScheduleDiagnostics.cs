using System.Text.Json;

namespace Hisa.App;

/// <summary>All AI tests use fake local JSON; no external API call or user's workbooks.</summary>
public static class AiScheduleDiagnostics
{
    public static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool okay, string label)
        {
            if (!okay) throw new InvalidOperationException("AI test failed: " + label);
            checks.Add(label);
        }
        const string geminiOutput = """
        {
          "arrivalDate":"2026-10-31","dispatchDate":"2026-10-31",
          "arrivals":[
            {"project":"Cổ Liễng - Xuân La, Hà Nội","company":"KD Việt Nam","contract":"56062026","plannedDate":"2026-10-31","container":"VSIU 2461333","containerExportDate":"2026-10-26","note":"Khách cần gấp","urgent":true,"hasExportSchedule":null},
            {"project":"Nguyễn Thị Hoàng Yến - Đông Ngạc","company":"TB Thành Tâm","contract":"13482026","plannedDate":"2026-10-31","container":"SELU 4642099","containerExportDate":"2026-10-22","note":"Khách cần gấp","urgent":true,"hasExportSchedule":null}
          ],
          "dispatches":[
            {"project":"Cổ Liễng - Xuân La, Hà Nội","company":"KD Việt Nam","contract":"56062026","plannedDate":"2026-10-31","container":"","containerExportDate":"","note":"Đã có lx","urgent":false,"hasExportSchedule":null},
            {"project":"Nguyễn Thị Hoàng Yến","company":"TB Thành Tâm","contract":"13482026","plannedDate":"2026-10-31","container":"","containerExportDate":"","note":"","urgent":false,"hasExportSchedule":null}
          ]
        }
        """;

        var document = AiScheduleReader.Parse(geminiOutput);
        Check(document.Arrivals.Count == 2 && document.Dispatches.Count == 2,
            "Gemini parses arrivals and dispatches into separate tables");
        Check(document.Arrivals.All(x => x.Urgent),
            "Gemini preserves urgent contracts");
        Check(document.Arrivals[0].Container == "VSIU 2461333" &&
              document.Arrivals[1].Container == "SELU 4642099",
            "Each arrival row keeps the correct container");
        Check(document.ArrivalDate == "2026-10-31" &&
              document.Arrivals[0].ContainerExportDate == "2026-10-26",
            "Container shipping date is distinct from Hưng Yên arrival date");
        Check(document.Dispatches[0].HasExportSchedule == true &&
              document.Dispatches[0].ScheduleLabel == "Đã có lịch xuất",
            "Đã có LX means export schedule, never driver or shipped");
        Check(document.Dispatches[1].HasExportSchedule == null &&
              document.Dispatches[1].ScheduleLabel == "Chưa xác nhận lịch xuất",
            "Empty LX note is unknown, not false");
        document.AnalyzedBy = "Gemini";
        Check(document.AnalyzedBy == "Gemini", "Analysis source is Gemini only");
        var snapshot = new WorkbookSnapshot
        {
            Packages = [new Package { Contract = "56062026", Project = "Cổ Liễng" }]
        };
        var warnings = AiScheduleReader.ValidateWithWarehouse(document, snapshot);
        Check(warnings.Any(x => x.Contains("13482026") && x.Contains("chưa tìm thấy")),
            "Missing master workbook contract triggers warehouse warning");
        document.Dispatches[1].Company = "TB Thành Tâm";
        document.Dispatches[1].NeedsReview = true;
        var restored = JsonSerializer.Deserialize<AiScheduleDocument>(
            JsonSerializer.Serialize(document, WarehouseService.Json), WarehouseService.Json)!;
        Check(restored.Dispatches.Count == 2 &&
              restored.Dispatches[0].HasExportSchedule == true &&
              restored.Dispatches[1].NeedsReview,
            "Approved Gemini schedule round-trip retains changes");
        Check(AiScheduleReader.DiagnoseApiFailure("Gemini", 401,
            "{\"error\":{\"code\":\"UNAUTHENTICATED\",\"message\":\"Invalid key\"}}")
            .Contains("API Key không hợp lệ"), "Invalid key guidance");
        Check(AiScheduleReader.DiagnoseApiFailure("Gemini", 429,
            "{\"error\":{\"code\":\"RESOURCE_EXHAUSTED\",\"message\":\"quota exhausted\"}}")
            .Contains("hạn mức/tín dụng"), "Gemini quota error guidance");
        Check(AiScheduleReader.DiagnoseApiFailure("Gemini", 404,
            "{\"error\":{\"code\":\"NOT_FOUND\",\"message\":\"model unavailable\"}}")
            .Contains("mô hình"), "Gemini model access guidance");
        var secret = "AIzaNeverShowApiKeyValue";
        Check(!AiScheduleReader.DiagnoseApiFailure("Gemini", 403,
            "{\"error\":{\"message\":\"" + secret + " blocked\"}}").Contains(secret),
            "API key text from provider response never surfaced");
        Check(AiScheduleReader.DiagnoseApiFailure("Gemini", 503, "<html>error</html>")
            .Contains("tạm thời"), "Non-JSON server errors handled");
        return checks;
    }
}
