namespace Hisa.App;

// Pure data checks run as part of the existing Windows regression suite.
// No API credentials, Internet requests, Excel edits or production files are needed.
public static class AiScheduleDiagnostics
{
    public static List<string> Run()
    {
        var checks = new List<string>();
        static void Assert(bool ok, string name, List<string> list)
        {
            if (!ok) throw new InvalidOperationException("AI schedule regression: " + name);
            list.Add("AI: " + name);
        }
        const string fixture = """
        {
          "date":"2026-10-31",
          "incoming":[
            {"container":"VSIU 2461333","sourceDate":"2026-10-26","rows":[
              {"contract":"56062026","project":"Cổ Liễng - Xuân La","company":"KD Việt Nam","note":"Khách cần gấp","urgent":true},
              {"contract":"21112026","project":"Lê Đình Cương","company":"Minh Long","note":"Tủ điện, domino, ARD","urgent":false}
            ]},
            {"container":"SELU 4642099","sourceDate":"2026-10-22","rows":[
              {"contract":"13482026","project":"Nguyễn Thị Hoàng Yến","company":"TB Thành Tâm","note":"Khách cần gấp","urgent":true}
            ]}
          ],
          "outgoing":[
            {"contract":"56062026","project":"Cổ Liễng - Xuân La","company":"KD Việt Nam","note":"Đã có lx","hasSchedule":false},
            {"contract":"13482026","project":"Nguyễn Thị Hoàng Yến","company":"TB Thành Tâm","note":"","hasSchedule":false},
            {"contract":"001234","project":"Công trình khác","company":"Công ty","note":"Đã có lx","hasSchedule":true}
          ]
        }
        """;
        var doc = AiScheduleReader.Parse(fixture);
        Assert(doc.Date == "2026-10-31" && doc.Incoming.Count == 2 &&
               doc.Incoming.Sum(x => x.Rows.Count) == 3, "date and two independent incoming container groups", checks);
        Assert(doc.Incoming[0].SourceDate == "2026-10-26" &&
               doc.Date != doc.Incoming[0].SourceDate, "container shipping date is not warehouse plan date", checks);
        Assert(doc.Outgoing[0].HasSchedule &&
               doc.Outgoing[0].ScheduleLabel == "Đã có lịch xuất", "LX means existing schedule, not driver", checks);
        Assert(!doc.Outgoing[1].HasSchedule &&
               doc.Outgoing[1].ScheduleLabel == "Chưa rõ trạng thái lịch xuất",
               "blank LX never means no driver or cancellation", checks);
        Assert(doc.Outgoing.Take(2).All(x => x.Urgent) &&
               !doc.Outgoing[2].Urgent, "urgent incoming contracts matched to outgoing", checks);
        Assert(Rules.Contract(doc.Outgoing[2].Contract) == "1234",
               "contract normalization retains a matching canonical key", checks);
        try
        {
            AiScheduleReader.Parse("""{"date":"26/10/2026","incoming":[],"outgoing":[]}""");
            throw new InvalidOperationException("AI schedule regression: invalid date accepted");
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("AI chưa đọc đúng ngày"))
        {
            checks.Add("AI: reject non-ISO plan date");
        }
        return checks;
    }
}
