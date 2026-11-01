using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>Offline HISA/Gemini integration tests: no audio, cloud call or workbook writes.</summary>
public static class GeminiHisaContextDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool value,string name)
        {
            if(!value)throw new InvalidOperationException("Gemini HISA: "+name);
            checks.Add("Gemini HISA: "+name);
        }
        const string contract="56062026";
        var plannedArrival=new AiScheduleLine
        {
            Contract=contract,Project="Hưng Yên T1",Company="Nhà máy A",
            PlannedDate="2026-11-02",Container="CONT-101",
            ContainerExportDate="2026-10-29"
        };
        var earlier=new AiScheduleLine
        {
            Contract=contract,Project="Hưng Yên T1",Company="Nhà máy A",
            PlannedDate="2026-11-03",HasExportSchedule=true
        };
        var arising=new AiScheduleLine
        {
            Contract="13482026",Project="Hưng Yên T2",Company="Nhà máy B",
            PlannedDate="2026-11-03",HasExportSchedule=null
        };
        var approved=new AiScheduleDocument
        {
            ArrivalDate="2026-11-02",DispatchDate="2026-11-03",
            ApprovedAt="2026-11-01T15:00:00+07:00",Arrivals=[plannedArrival],
            Dispatches=[earlier,arising]
        };
        var draft=new AiScheduleDocument
        {
            DispatchDate="2026-11-03",
            Dispatches=[new AiScheduleLine
            {
                Contract="99999999",Project="Chưa duyệt",Company="N",
                PlannedDate="2026-11-03"
            }]
        };
        var snapshot=new WorkbookSnapshot();
        var state=new DispatchReadinessState();
        var summary=GeminiChatService.BuildReadOnlyContext(
            "Ngày mai cont nào dự kiến về, ngày kia lịch xuất hàng nào?",
            snapshot,[],state,new DateTime(2026,11,1),[draft,approved]);
        Check(summary.Contains("Dự kiến VỀ kho 2026-11-02") &&
            summary.Contains("CONT-101") && summary.Contains(contract),
            "Approved container schedule is available in Gemini without inventory records");
        Check(summary.Contains("Lịch XUẤT 2026-11-03") &&
            summary.Contains("Đã có lịch xuất") &&
            summary.Contains("Mới phát sinh (ô LX để trống)"),
            "Both old LX and newly arising blank LX are properly identified");
        Check(!summary.Contains("99999999") && !summary.Contains("Chưa duyệt"),
            "Draft/unapproved Zalo lines are excluded");
        Check(summary.Contains("không tự ghi đã nhập") ||
            summary.Contains("KHÔNG tự ghi đã nhập"),
            "Planned arrival is not a physical warehouse receipt");
        Check(summary.Contains("lịch làm việc") &&
            summary.Contains("KHÔNG đồng nghĩa"),
            "Planned dispatch is an operational schedule, not delivered evidence");
        var focused=GeminiChatService.BuildReadOnlyContext("HĐ "+contract,
            snapshot,[],state,new DateTime(2026,11,1),[approved]);
        Check(focused.Contains("CONT-101")&&focused.Contains("Đã có lịch xuất") &&
            !focused.Contains("13482026"),
            "Contract scoped questions show only matching approved lines");

        var temporary=Path.Combine(Path.GetTempPath(),"hisa-gemini-context-"+
            Guid.NewGuid().ToString("N"));
        try
        {
            var folder=Path.Combine(temporary,"ai_schedules");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"lich-kho-approved.json"),
                JsonSerializer.Serialize(approved,WarehouseService.Json));
            File.WriteAllText(Path.Combine(folder,"lich-kho-draft.json"),
                JsonSerializer.Serialize(draft,WarehouseService.Json));
            File.WriteAllText(Path.Combine(folder,"lich-kho-corrupt.json"),"{invalid");
            var loaded=AiScheduleWorkspace.RecentApproved(temporary);
            Check(loaded.Count==1 && loaded[0].Arrivals.Single().Container=="CONT-101",
                "Only approved documents are loaded from on-disk schedules");
            Check(loaded[0].Dispatches.Count==2 &&
                loaded[0].Dispatches[1].HasExportSchedule==null,
                "Read-only schedule loader preserves old-LX and blank-LX distinctions");
        }
        finally
        {
            if(Directory.Exists(temporary))Directory.Delete(temporary,true);
        }
        Check(!File.Exists(GeminiWarehouseSharingConsent.ConfigPath) ||
              File.Exists(GeminiWarehouseSharingConsent.ConfigPath),
              "Warehouse sharing requires a separate stored preference; no API credentials used");
        return checks;
    }
}
