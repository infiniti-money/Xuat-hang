namespace Hisa.App;

public sealed class MotorSerialEntry
{
    public string LineId { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Material { get; set; } = "";
    public int UnitIndex { get; set; }
    public string SerialNumber { get; set; } = "";
    public MotorSerialEntry Copy()=>(MotorSerialEntry)MemberwiseClone();
}

public static class MotorSerials
{
    private static string Text(string? value)=>value?.Trim()??"";
    private static IEnumerable<MotorSerialEntry> Entries(Slip slip)=>(slip.MotorSerials??[]).OfType<MotorSerialEntry>();
    public static bool IsMotor(MaterialLine line)=>line.Include&&Rules.Classify(line.Material)=="Máy kéo";
    private static string Key(MotorSerialEntry value)=>value.LineId+"|"+value.UnitIndex;
    private static bool SameMotor(MotorSerialEntry entry,MaterialLine line)=>Rules.Identity(entry.Contract,entry.Project,entry.Company)==Rules.Identity(line.Contract,line.Project,line.Company)&&Rules.Code(entry.Material)==Rules.Code(line.Material);
    public static List<MotorSerialEntry> BuildRows(Slip slip)
    {
        var motors=slip.Lines.Where(IsMotor).ToList();var result=new List<MotorSerialEntry>();
        var previous=Entries(slip).GroupBy(Key).ToDictionary(g=>g.Key,g=>g.ToList());
        if(motors.GroupBy(x=>x.Id).Any(g=>string.IsNullOrWhiteSpace(g.Key)||g.Count()>1))throw new ArgumentException("Các dòng máy kéo trùng mã dòng. Hãy tải lại vật tư hoặc thêm mỗi dòng bằng nút Thêm vật tư.");
        foreach(var line in motors)
        {
            if(line.Quantity is null||line.Quantity<0||line.Quantity!=decimal.Truncate(line.Quantity.Value))throw new ArgumentException($"HĐ {line.Contract}: số lượng máy kéo phải là số nguyên không âm để nhập serial từng máy.");
            if(line.Quantity>10000||result.Count+line.Quantity>10000)throw new ArgumentException("Một phiếu hỗ trợ tối đa 10.000 máy kéo. Hãy chia thành các phiếu nhỏ hơn.");
            for(var index=1;index<=line.Quantity;index++)
            {
                var old=(previous.GetValueOrDefault(line.Id+"|"+index)??[]).Where(x=>SameMotor(x,line)).ToList();
                result.Add(new MotorSerialEntry{LineId=line.Id,Contract=line.Contract,Project=line.Project,Company=line.Company,Material=line.Material,UnitIndex=index,SerialNumber=old.Count==1?Text(old[0].SerialNumber):""});
            }
        }
        return result;
    }
    public static void Validate(Slip slip,IEnumerable<Slip>? history=null,bool requireAll=true)
    {
        var expected=BuildRows(slip);var actual=Entries(slip).ToList();
        var groups=actual.GroupBy(Key).ToDictionary(g=>g.Key,g=>g.ToList());
        if(actual.Count!=expected.Count||groups.Any(g=>g.Value.Count!=1)||expected.Any(row=>!groups.TryGetValue(Key(row),out var matching)||!matching.Any(x=>x.Contract==row.Contract&&x.Project==row.Project&&x.Company==row.Company&&Rules.Code(x.Material)==Rules.Code(row.Material))))
            throw new ArgumentException("Danh sách serial chưa khớp số máy kéo đang chọn. Mở bảng Serial máy kéo để kiểm tra lại.");
        if(requireAll&&actual.Any(x=>string.IsNullOrWhiteSpace(x.SerialNumber)))
        {
            var first=actual.First(x=>string.IsNullOrWhiteSpace(x.SerialNumber));
            throw new ArgumentException($"HĐ {first.Contract} · máy số {first.UnitIndex}: chưa nhập serial. Nhập đủ serial trước khi xác nhận Đã xuất.");
        }
        var filled=actual.Where(x=>!string.IsNullOrWhiteSpace(x.SerialNumber)).ToList();
        if(filled.Any(x=>Text(x.SerialNumber).Length>200||x.SerialNumber.Contains('\n')||x.SerialNumber.Contains('\r')))throw new ArgumentException("Mỗi máy dùng một serial trên một dòng, tối đa 200 ký tự.");
        var duplicate=filled.GroupBy(x=>Text(x.SerialNumber),StringComparer.OrdinalIgnoreCase).FirstOrDefault(g=>g.Count()>1);
        if(duplicate!=null)throw new ArgumentException("Serial bị trùng trong phiếu: "+duplicate.Key);
        var wanted=filled.Select(x=>Text(x.SerialNumber)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var other in (history??[]).Where(x=>x.Id!=slip.Id&&x.Status=="delivered"))
        {
            var active=other.Lines.Where(IsMotor).ToList();
            var conflict=Entries(other).FirstOrDefault(x=>x.UnitIndex>=1&&active.Any(line=>line.Id==x.LineId&&SameMotor(x,line)&&line.Quantity>=x.UnitIndex)&&wanted.Contains(Text(x.SerialNumber)));
            if(conflict!=null)throw new ArgumentException($"Serial {conflict.SerialNumber} đã xuất theo phiếu {other.SlipCode}, HĐ {conflict.Contract}, ngày {Rules.Day(other.HandoverDate)}.");
        }
        foreach(var entry in actual)entry.SerialNumber=Text(entry.SerialNumber);
    }
    public static string SerialText(Slip slip,MaterialLine line)=>string.Join("; ",Entries(slip).Where(x=>x.LineId==line.Id&&SameMotor(x,line)&&x.UnitIndex>=1&&x.UnitIndex<=line.Quantity&&Text(x.SerialNumber).Length>0).OrderBy(x=>x.UnitIndex).Select(x=>Text(x.SerialNumber)));
    public static string NoteWithSerials(Slip slip,MaterialLine line)
    {
        var serials=SerialText(slip,line);return string.Join(" · ",new[]{line.Note,serials.Length>0?"Serial: "+serials:""}.Where(x=>!string.IsNullOrWhiteSpace(x)));
    }
    // Audit output must not run strict export validation on every unrelated waiting
    // slip: a draft whose quantity/model is being edited must not block all ledgers.
    public static List<MotorSerialEntry> ActiveRows(Slip slip)
    {
        var entries=Entries(slip).ToList();if(entries.Count==0)return [];
        var active=slip.Lines.Where(IsMotor).Where(x=>!string.IsNullOrWhiteSpace(x.Id)&&x.Quantity is >0 && x.Quantity==decimal.Truncate(x.Quantity.Value)).GroupBy(x=>x.Id).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.First());
        return entries.Where(x=>!string.IsNullOrWhiteSpace(x.LineId)&&active.TryGetValue(x.LineId,out var line)&&SameMotor(x,line)&&x.UnitIndex>=1&&x.UnitIndex<=line.Quantity&&Text(x.SerialNumber).Length>0)
            .GroupBy(Key).Where(g=>g.Count()==1).Select(g=>{var value=g.First().Copy();value.SerialNumber=Text(value.SerialNumber);return value;}).ToList();
    }
}
