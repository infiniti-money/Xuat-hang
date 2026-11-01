namespace Hisa.App;

/// <summary>
/// Reads physical serials from the independently configured MÁY KÉO.xlsx
/// production list and puts them into IN-MEMORY slip motor rows so every
/// standard print template includes them. No Excel modification, no
/// shipment confirmation and no overwrite of manually entered serials.
/// </summary>
public static class MotorSerialAutoFill
{
    public sealed record Result(int Assigned,int Missing,IReadOnlyList<string> Warnings);

    public static Result Fill(Slip slip,IEnumerable<EquipmentListReader.Row> productionRows,
        IEnumerable<Slip>? slipHistory=null)
    {
        var expected=MotorSerials.BuildRows(slip);
        var warnings=new List<string>();
        if(expected.Count==0){slip.MotorSerials=[];return new Result(0,0,warnings);}

        var all=productionRows.Where(x=>x.Category=="Máy kéo")
            .Where(x=>!string.IsNullOrWhiteSpace(x.Serial)).ToList();
        // A duplicate physical serial in the entire source cannot be
        // trusted even when contract and project happen to match.
        var duplicated=all.GroupBy(x=>Rules.Code(x.Serial),StringComparer.Ordinal)
            .Where(g=>g.Key.Length==0||g.Count()!=1)
            .Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        var alreadyDelivered=(slipHistory??[])
            .Where(x=>x.Id!=slip.Id&&x.Status=="delivered")
            .SelectMany(MotorSerials.ActiveRows)
            .Select(x=>Rules.Code(x.SerialNumber))
            .Where(x=>x.Length>0).ToHashSet(StringComparer.Ordinal);
        int assigned=0;
        var occupied=expected.Where(x=>!string.IsNullOrWhiteSpace(x.SerialNumber))
            .Select(x=>Rules.Code(x.SerialNumber)).ToHashSet(StringComparer.Ordinal);
        var lineGroups=expected.GroupBy(x=>x.LineId).ToList();
        foreach(var units in lineGroups)
        {
            var first=units.First();
            var scope=Rules.Identity(first.Contract,first.Project,first.Company);
            if(string.IsNullOrWhiteSpace(first.Contract)||
               string.IsNullOrWhiteSpace(first.Project)||
               string.IsNullOrWhiteSpace(first.Company))
            {
                warnings.Add("Máy kéo HĐ "+first.Contract+
                    ": thiếu công trình/đơn vị để lấy serial chính xác.");
                continue;
            }
            var sameContract=all.Where(x=>
                Rules.Identity(x.Contract,x.Project,x.Company)==scope).ToList();
            var exact=sameContract.Where(x=>
                Rules.Code(x.Material)==Rules.Code(first.Material)).ToList();
            var sameIdentityLines=lineGroups.Count(g=>
                Rules.Identity(g.First().Contract,g.First().Project,g.First().Company)==scope);
            // A weaker fallback is safe ONLY for one motor line and exactly
            // the same number of physical units. Similar contract numbers
            // from other projects or companies are NEVER mixed.
            var sources=exact.Count==units.Count()?exact:
                exact.Count==0 && sameIdentityLines==1 &&
                sameContract.Count==units.Count()?sameContract:[];
            if(sources.Count!=units.Count()||sources.Count==0)
            {
                warnings.Add("HĐ "+first.Contract+" · "+first.Project+
                    ": thiếu hoặc không khớp đúng số máy kéo/serial trong MÁY KÉO.xlsx.");
                continue;
            }
            var sourceCodes=sources.Select(x=>Rules.Code(x.Serial)).ToList();
            if(sourceCodes.Distinct(StringComparer.Ordinal).Count()!=sourceCodes.Count||
               sourceCodes.Any(x=>x.Length==0||duplicated.Contains(x)||
                   alreadyDelivered.Contains(x)))
            {
                warnings.Add("HĐ "+first.Contract+
                    ": serial trùng, không rõ hoặc đã bàn giao trong phiếu khác.");
                continue;
            }
            var unitRows=units.OrderBy(x=>x.UnitIndex).ToList();
            var manual=unitRows.Where(x=>!string.IsNullOrWhiteSpace(x.SerialNumber))
                .Select(x=>Rules.Code(x.SerialNumber)).ToList();
            if(manual.Distinct(StringComparer.Ordinal).Count()!=manual.Count||
               manual.Any(x=>!sourceCodes.Contains(x)))
            {
                warnings.Add("HĐ "+first.Contract+
                    ": serial đã nhập tay khác nguồn. Giữ nguyên để kiểm tra.");
                continue;
            }
            var remaining=sources.Where(x=>
                !manual.Contains(Rules.Code(x.Serial)) &&
                !occupied.Contains(Rules.Code(x.Serial)))
                .OrderBy(x=>x.SourceRow).ThenBy(x=>x.Serial,StringComparer.Ordinal)
                .ToList();
            var empty=unitRows.Where(x=>string.IsNullOrWhiteSpace(x.SerialNumber)).ToList();
            if(remaining.Count!=empty.Count)
            {
                warnings.Add("HĐ "+first.Contract+
                    ": không thể gán serial duy nhất cho từng máy trong phiếu.");
                continue;
            }
            for(var i=0;i<empty.Count;i++)
            {
                empty[i].SerialNumber=remaining[i].Serial.Trim();
                occupied.Add(Rules.Code(remaining[i].Serial));
                assigned++;
            }
        }
        // Never automatically mark a slip Delivered. Existing values are
        // preserved for operator review; new values print from the same
        // MotorSerials.NoteWithSerials pathway as manually entered rows.
        slip.MotorSerials=expected;
        return new Result(assigned,expected.Count(x=>string.IsNullOrWhiteSpace(x.SerialNumber)),warnings);
    }
}
