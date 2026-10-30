using System.Text.Json;

namespace Hisa.App;

public static class AiScheduleDiagnostics
{
    public static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("AI diagnostics failed: " + description);
            checks.Add(description);
        }

        const string fromGpt = """
        {
          "arrivalDate":"2026-10-31", "dispatchDate":"2026-10-31",
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
        const string fromGemini = """
        {
          "arrivalDate":"2026-10-31", "dispatchDate":"2026-10-31",
          "arrivals":[
            {"project":"Cổ Liễng - Xuân La, Hà Nội","company":"KD Việt Nam","contract":"56062026","plannedDate":"2026-10-31","container":"VSIU 2461333","containerExportDate":"2026-10-26","note":"Khách cần gấp","urgent":true,"hasExportSchedule":null},
            {"project":"Nguyễn Thị Hoàng Yến - Đông Ngạc","company":"TB Thành Tâm","contract":"13482026","plannedDate":"2026-10-31","container":"SELU 4642099","containerExportDate":"2026-10-22","note":"Khách cần gấp","urgent":true,"hasExportSchedule":null}
          ],
          "dispatches":[
            {"project":"Cổ Liễng - Xuân La, Hà Nội","company":"KD Việt Nam","contract":"56062026","plannedDate":"2026-10-31","container":"","containerExportDate":"","note":"Đã có lx","urgent":false,"hasExportSchedule":true},
            {"project":"Nguyễn Thị Hoàng Yến","company":"TB Thành Tâm","contract":"13482026","plannedDate":"2026-10-31","container":"","containerExportDate":"","note":"","urgent":false,"hasExportSchedule":null}
          ]
        }
        """;
        var first = AiScheduleReader.Parse(fromGpt);
        var second = AiScheduleReader.Parse(fromGemini);
        Check(first.Arrivals.Count == 2 && first.Dispatches.Count == 2,
            "AI parses incoming and outgoing tables separately");
        Check(first.Arrivals.All(x => x.Urgent), "AI preserves urgent customers and container data");
        Check(first.Arrivals[0].Container == "VSIU 2461333" &&
              first.Arrivals[1].Container == "SELU 4642099",
            "AI preserves original container associations");
        Check(first.Dispatches[0].HasExportSchedule == true &&
              first.Dispatches[0].ScheduleLabel == "Đã có lịch xuất",
            "Đã có LX means export schedule (never driver or shipped)");
        Check(first.Dispatches[1].HasExportSchedule == null &&
              first.Dispatches[1].ScheduleLabel == "Chưa xác nhận lịch xuất",
            "Blank LX note does not imply no schedule");
        var merged = AiScheduleReader.Combine(first, second);
        Check(merged.Arrivals.Count == 2 && merged.Dispatches.Count == 2,
            "GPT/Gemini agreement keeps single row per contract");
        Check(merged.AnalyzedBy == "GPT + Gemini",
            "Dual-provider provenance is preserved");
        Check(merged.Arrivals[0].ContainerExportDate == "2026-10-26" &&
              merged.ArrivalDate == "2026-10-31",
            "Container export date is distinct from warehouse arrival date");

        second.Dispatches[1].Company = "Đơn vị khác";
        var disagreement = AiScheduleReader.Combine(AiScheduleReader.Parse(fromGpt), second);
        Check(disagreement.Dispatches[1].NeedsReview &&
              disagreement.Dispatches[1].ReviewNote.Contains("đơn vị"),
            "Differences between GPT and Gemini need manual review");

        var snapshot = new WorkbookSnapshot
        {
            Packages = [new Package { Contract = "56062026", Project = "Cổ Liễng" }]
        };
        var warnings = AiScheduleReader.ValidateWithWarehouse(disagreement, snapshot);
        Check(warnings.Any(x => x.Contains("13482026") && x.Contains("chưa tìm thấy")),
            "Missing master workbook contracts show reconciliation warnings");
        var persisted = JsonSerializer.Deserialize<AiScheduleDocument>(
            JsonSerializer.Serialize(merged, WarehouseService.Json), WarehouseService.Json)!;
        Check(persisted.Dispatches.Count == 2 &&
              persisted.Dispatches[0].HasExportSchedule == true,
            "Reviewed schedule JSON round-trip keeps LX semantics");
        // Provider failures must be actionable and must never expose raw provider responses or credentials.
        Check(AiScheduleReader.DiagnoseApiFailure("GPT", 401,
            "{\"error\":{\"code\":\"invalid_api_key\",\"message\":\"Unauthorized\"}}")
            .Contains("API Key không hợp lệ"), "GPT 401 provides valid API key guidance");
        Check(AiScheduleReader.DiagnoseApiFailure("GPT", 429,
            "{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"Billing quota exceeded\"}}")
            .Contains("hạn mức/tín dụng"), "GPT insufficient quota error is distinguished from server outages");
        Check(AiScheduleReader.DiagnoseApiFailure("Gemini", 404,
            "{\"error\":{\"code\":\"NOT_FOUND\",\"message\":\"Model not found\"}}")
            .Contains("mô hình"), "Gemini missing model guidance");
        var secret = "AIzaDontEverShowThisSecret";
        Check(!AiScheduleReader.DiagnoseApiFailure("Gemini", 403,
            "{\"error\":{\"message\":\"" + secret + " is blocked\"}}").Contains(secret),
            "Provider errors do not leak returned API key text");
        Check(AiScheduleReader.DiagnoseApiFailure("Gemini", 503, "<html>Error</html>")
            .Contains("tạm thời"), "Non-JSON proxy and transient errors have safe guidance");
        return checks;
    }
}
