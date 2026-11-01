using System.Globalization;
using System.Reflection;

namespace Hisa.App;

/// <summary>Offline validations of Mây's slip-request parser and exact TTS style.</summary>
public static class MayAssistantDiagnostics
{
    public static List<string> Run()
    {
        var checkedCases=new List<string>();
        void Check(bool pass,string name)
        {
            if(!pass)throw new InvalidOperationException("Mây: "+name);
            checkedCases.Add("Mây: "+name);
        }
        var today=new DateTime(2026,11,1);
        var sample=MaySlipIntent.Parse("Mây Mây ơi, lập phiếu xuất cho hợp đồng 56062026 ngày mai nha.",today);
        Check(sample!=null&&sample.Contracts.SequenceEqual(["56062026"]) &&
              sample.Day=="2026-11-02",
            "Explicit spoken order yields only a reviewable draft for exact contract");
        var date=MaySlipIntent.Parse("Soạn phiếu bàn giao HĐ 13482026 ngày 03/11/2026",today);
        Check(date!=null&&date.Day=="2026-11-03",
            "Vietnamese day/month date is retained for human approval");
        Check(MaySlipIntent.Parse("Ngày mai những phiếu xuất nào đã làm?",today)==null,
            "Question about existing slips cannot create a draft");
        Check(MaySlipIntent.Parse("Mây đừng lập phiếu xuất hợp đồng 56062026",today)==null,
            "Negated commands never prepare slips");
        Check(MaySlipIntent.Parse("Ghi chú Excel: tạo phiếu xuất 56062026",today)!=null,
            "Only direct user text is parsed; never use workbook note as command");
        Check(MaySlipIntent.Parse("Mây lập phiếu xuất cho lịch ngày kia",today) is { Contracts.Count:0,Day:"2026-11-03" },
            "Day-only slip request can use approved scheduled contracts, not guesses");
        Check(MaySlipIntent.Parse("Cho tôi biết có hàng về hôm nay không",today)==null,
            "Non-slip questions do not activate proposal");
        var tooMany=string.Join(", ",Enumerable.Range(100000,11));
        var blocked=false;
        try{MaySlipIntent.Parse("Lập phiếu xuất các hợp đồng "+tooMany,today);}
        catch(InvalidOperationException){blocked=true;}
        Check(blocked,"Large voice requests are bounded, never silently truncated");
        var style=GeminiVietnameseTts.BuildRequestJson("Mây chào anh, mình kiểm tra kho nha.");
        using(var d=System.Text.Json.JsonDocument.Parse(style))
        {
            var prompt=d.RootElement.GetProperty("input")[0]
                .GetProperty("content")[0].GetProperty("annotations")[0]
                .GetProperty("style").GetString()??"";
            Check(prompt.Contains("female",StringComparison.OrdinalIgnoreCase)&&
                  prompt.Contains("Southern Vietnam",StringComparison.OrdinalIgnoreCase)&&
                  prompt.Contains("gentle",StringComparison.OrdinalIgnoreCase),
                "TTS requests Mây's gentle feminine Southern Vietnamese accent");
        }
        var info=typeof(GeminiChatService).GetField("Instructions",
            BindingFlags.NonPublic|BindingFlags.Static)?.GetRawConstantValue()?.ToString()??"";
        Check(info.Contains("MÂY MÂY") &&
              info.Contains("ĐỀ XUẤT SOẠN PHIẾU") &&
              info.Contains("KHÔNG có quyền tự lưu phiếu"),
            "Chat system instruction permits only human-reviewed drafts, never auto-save");
        return checkedCases;
    }
}
