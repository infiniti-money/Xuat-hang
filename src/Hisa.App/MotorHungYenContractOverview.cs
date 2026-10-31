namespace Hisa.App;

/// <summary>
/// One row per genuine contract + project + company; never conflates T1/T2.
/// The outsourcing list contains ONLY operator-confirmed sent/completed/handover
/// records, not machine rows merely present in the production workbook.
/// </summary>
public static class MotorHungYenContractOverview
{
    public sealed class Contract
    {
        public string Key { get; set; } = "";
        public string ContractNumber { get; set; } = "";
        public string Project { get; set; } = "";
        public string Company { get; set; } = "";
        public int MachineCount { get; set; }
        public int SentCount { get; set; }
        public int CompletedCount { get; set; }
        public int HandedOverCount { get; set; }
        public int PendingCount { get; set; }
        public decimal? Quantity { get; set; }
        public string Status { get; set; } = "";
        public bool HasConfirmedWork => SentCount+CompletedCount+HandedOverCount>0;
    }

    public static string Identity(MotorHungYenStore.Item row)=>
        Rules.Identity(row.Contract,row.Project,row.Company);

    public static List<Contract> Summarize(IEnumerable<MotorHungYenStore.Item> machines,
        bool confirmedOnly=true)
    {
        var all=machines
            .Where(x=>Rules.Contract(x.Contract).Length>0)
            .GroupBy(Identity,StringComparer.Ordinal)
            .Select(group=>
            {
                var rows=group.ToList();
                var sent=rows.Count(x=>x.State==MotorHungYenStore.Sent);
                var completed=rows.Count(x=>x.State==MotorHungYenStore.Completed);
                var handed=rows.Count(x=>x.State==MotorHungYenStore.HandedOver);
                var pending=rows.Count(x=>x.State==MotorHungYenStore.Pending);
                var count=rows.Count;
                return new Contract
                {
                    Key=group.Key,
                    ContractNumber=rows[0].Contract,Project=rows[0].Project,
                    Company=rows[0].Company,
                    MachineCount=count,SentCount=sent,CompletedCount=completed,
                    HandedOverCount=handed,PendingCount=pending,
                    Quantity=rows.All(x=>x.Quantity.HasValue)?
                        rows.Sum(x=>x.Quantity!.Value):null,
                    Status=handed==count?"Đã bàn giao toàn bộ":
                        completed==count?"Đã làm xong · chờ bàn giao":
                        sent+completed+handed==0?"Chưa ghi nhận gửi":
                        pending>0?$"Đã gửi {sent+completed+handed}/{count} máy · còn {pending} chưa ghi nhận":
                        handed>0?$"Đã bàn giao {handed}/{count} máy":
                        completed>0?$"Đã làm {completed}/{count} máy":
                        "Đang gia công"
                };
            })
            .Where(x=>!confirmedOnly||x.HasConfirmedWork)
            .OrderBy(x=>x.ContractNumber,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Project,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Company,StringComparer.OrdinalIgnoreCase).ToList();
        return all;
    }
}
