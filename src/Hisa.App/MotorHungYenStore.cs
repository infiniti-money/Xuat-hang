using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// Local, explicitly confirmed work stages for each machine in MÁY KÉO.xlsx.
/// This does not modify the production Excel, canonical inventory or handover
/// slips. A source cell such as "DXH" is NOT evidence of a work stage.
/// </summary>
public sealed class MotorHungYenStore
{
    public const string Pending="pending";
    public const string Sent="sent";
    public const string Completed="completed";
    public const string HandedOver="handed_over";
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNameCaseInsensitive=true,
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        WriteIndented=true
    };
    private readonly string _file;
    private readonly object _gate=new();
    private Dictionary<string,Entry>? _items;

    public MotorHungYenStore(string dataDirectory)
    {
        if(string.IsNullOrWhiteSpace(dataDirectory))throw new ArgumentException("Thiếu thư mục dữ liệu.");
        Directory.CreateDirectory(dataDirectory);
        _file=Path.Combine(dataDirectory,"motor_hung_yen_tracking.json");
    }

    public sealed class History
    {
        public string State { get; set; }="";
        public string EffectiveDay { get; set; }="";
        public string RecordedAt { get; set; }="";
        public string By { get; set; }="";
        public string Note { get; set; }="";
        public string Label => Name(State);
    }

    public sealed class Entry
    {
        public string Key { get; set; }="";
        public string Contract { get; set; }="";
        public string Project { get; set; }="";
        public string Company { get; set; }="";
        public string Material { get; set; }="";
        public string Serial { get; set; }="";
        public string Lot { get; set; }="";
        public string State { get; set; }=Pending;
        public string WorkChoice { get; set; }="";
        public string SentDay { get; set; }="";
        public string CompletedDay { get; set; }="";
        public string HandoverDay { get; set; }="";
        public string LastNote { get; set; }="";
        public List<History> Events { get; set; }=[];
        public string StatusLabel => Name(State);
    }

    public sealed class Item
    {
        public string Key { get; set; }="";
        public string Contract { get; set; }="";
        public string Project { get; set; }="";
        public string Company { get; set; }="";
        public string Material { get; set; }="";
        public decimal? Quantity { get; set; }
        public string Lot { get; set; }="";
        public string Serial { get; set; }="";
        public string SourceNote { get; set; }="";
        public string SourcePosition { get; set; }="";
        public string SourceWorkValue { get; set; }="";
        public string State { get; set; }=Pending;
        public string StatusLabel => Name(State);
        public string WorkChoice { get; set; }="";
        public string WorkLabel => SourceWorkValue is "Đã làm" or "Chưa làm"
            ? SourceWorkValue : WorkChoice.Length>0?WorkChoice:
            State is Completed or HandedOver?"Đã làm":"Chưa làm";
        public string SentDay { get; set; }="";
        public string CompletedDay { get; set; }="";
        public string HandoverDay { get; set; }="";
        public string LastNote { get; set; }="";
        public bool PresentInSource { get; set; }
        public string SourceStatus => PresentInSource?"Có trong Excel":"Không còn trong Excel";
    }

    public static string Name(string state)=>state switch
    {
        Pending=>"Chưa ghi nhận gửi",
        Sent=>"Đã gửi gia công tại Hưng Yên",
        Completed=>"Đã làm xong",
        HandedOver=>"Đã bàn giao",
        _=>"Cần đối chiếu"
    };

    public static string MachineKey(EquipmentListReader.Row row)
    {
        if(row.Category!="Máy kéo"||Rules.Contract(row.Contract).Length==0)
            throw new ArgumentException("Chỉ dùng danh sách máy kéo có số hợp đồng.");
        // Include both physical serial and source row: even identical/no-serial
        // machines in the same contract must not share a status.
        var raw=string.Join("|",[
            Rules.Contract(row.Contract),Rules.ProjectCode(row.Project),Rules.Code(row.Company),
            Rules.Code(row.Material),Rules.Code(row.Serial),Rules.Code(row.Lot),
            Rules.Code(row.Sheet),row.SourceRow.ToString(CultureInfo.InvariantCulture)
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static string Identity(string contract,string project,string company,
        string material,string serial,string lot)
    {
        if(string.IsNullOrWhiteSpace(serial))return "";
        return string.Join("|",Rules.Contract(contract),Rules.ProjectCode(project),
            Rules.Code(company),Rules.Code(material),Rules.Code(serial),Rules.Code(lot));
    }

    private static string Identity(EquipmentListReader.Row row)=>
        Identity(row.Contract,row.Project,row.Company,row.Material,row.Serial,row.Lot);

    private static string Identity(Entry record)=>
        Identity(record.Contract,record.Project,record.Company,record.Material,
            record.Serial,record.Lot);

    private Dictionary<string,Entry> Load()
    {
        if(_items!=null)return _items;
        if(!File.Exists(_file))return _items=new(StringComparer.Ordinal);
        try
        {
            var entries=JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_file),Json)
                ?? throw new InvalidDataException("File trạng thái máy kéo không chứa danh sách hợp lệ.");
            if(entries.Any(x=>x.Key.Length==0 || x.Events==null) ||
                entries.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=entries.Count)
                throw new InvalidDataException("File trạng thái máy kéo có khóa trùng hoặc lịch sử sai.");
            return _items=entries.ToDictionary(x=>x.Key,StringComparer.Ordinal);
        }
        catch(JsonException ex)
        {
            throw new InvalidDataException("File theo dõi máy kéo bị lỗi JSON. Không ghi đè dữ liệu cũ.",ex);
        }
    }

    public List<Item> List(IEnumerable<EquipmentListReader.Row> machineRows)
    {
        lock(_gate)
        {
            var entries=Load();
            var result=new List<Item>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            var rows=machineRows.Where(x=>x.Category=="Máy kéo").ToList();
            var uniqueSerials=rows.Where(x=>Identity(x).Length>0).GroupBy(Identity)
                .Where(x=>x.Count()==1).Select(x=>x.Key)
                .ToHashSet(StringComparer.Ordinal);
            var historical=entries.Values.Where(x=>Identity(x).Length>0)
                .GroupBy(Identity).Where(x=>x.Count()==1)
                .ToDictionary(x=>x.Key,x=>x.Single(),StringComparer.Ordinal);
            foreach(var row in rows)
            {
                var key=MachineKey(row);
                entries.TryGetValue(key,out var state);
                // The exact row-based key stays backward compatible. If Excel
                // inserts/reorders rows, resolve a UNIQUE physical serial to its
                // original persistent key without rewriting any history.
                if(state==null)
                {
                    var serialIdentity=Identity(row);
                    if(uniqueSerials.Contains(serialIdentity) &&
                       historical.TryGetValue(serialIdentity,out var recovered))
                    {
                        key=recovered.Key;
                        state=recovered;
                    }
                }
                if(!seen.Add(key))
                    throw new InvalidDataException("Trùng nguồn hoặc số seri máy kéo: "+row.Contract+" / "+row.Serial);
                result.Add(new Item
                {
                    Key=key,Contract=row.Contract,Project=row.Project,Company=row.Company,
                    Material=row.Material,Quantity=row.Quantity,Lot=row.Lot,Serial=row.Serial,
                    SourceNote=row.Note,SourcePosition=row.Sheet+" · dòng "+row.SourceRow,
                    SourceWorkValue=row.MotorColumnN,
                    State=state?.State??Pending,WorkChoice=state?.WorkChoice??"",
                    SentDay=state?.SentDay??"",CompletedDay=state?.CompletedDay??"",
                    HandoverDay=state?.HandoverDay??"",LastNote=state?.LastNote??"",
                    PresentInSource=true
                });
            }
            // Do not erase handover evidence if the Excel list was later edited.
            foreach(var item in entries.Values.Where(x=>!seen.Contains(x.Key)))
                result.Add(new Item
                {
                    Key=item.Key,Contract=item.Contract,Project=item.Project,
                    Company=item.Company,Material=item.Material,Serial=item.Serial,Lot=item.Lot,
                    State=item.State,WorkChoice=item.WorkChoice,
                    SentDay=item.SentDay,CompletedDay=item.CompletedDay,
                    HandoverDay=item.HandoverDay,LastNote=item.LastNote,PresentInSource=false
                });
            return result;
        }
    }

    public List<History> HistoryFor(string key)
    {
        lock(_gate)
        {
            var entries=Load();
            if(!entries.TryGetValue(key,out var entry))return [];
            return entry.Events.Select(x=>new History
            {
                State=x.State,EffectiveDay=x.EffectiveDay,RecordedAt=x.RecordedAt,
                By=x.By,Note=x.Note
            }).ToList();
        }
    }


    /// <summary>
    /// The dropdown owns only "Chưa làm" / "Đã làm". A handover is never
    /// confirmed here; only a delivered slip can generate a handover date.
    /// WorkChoice is explicit so untouched source rows are not mass-written.
    /// </summary>
    public void SetWorkStatus(Item source,bool done,string effectiveDay,string note)
    {
        if(!source.PresentInSource || string.IsNullOrWhiteSpace(source.Serial))
            throw new InvalidOperationException("Chỉ đổi tình trạng máy kéo có seri trong Excel nguồn.");
        if(!DateTime.TryParseExact(effectiveDay,"yyyy-MM-dd",CultureInfo.InvariantCulture,
            DateTimeStyles.None,out var day) || day.Date>DateTime.Today)
            throw new ArgumentException("Ngày xác nhận không hợp lệ hoặc ở tương lai.");
        lock(_gate)
        {
            var records=Load();
            records.TryGetValue(source.Key,out var previous);
            if(previous?.State==HandedOver)
                throw new InvalidOperationException("Máy đã có lịch sử bàn giao. Chỉ chỉnh ngày bằng phiếu xuất; kiểm tra chứng từ trước khi thay trạng thái.");
            if(previous?.Events.Count>0 &&
               string.CompareOrdinal(effectiveDay,previous.Events[^1].EffectiveDay)<0)
                throw new InvalidOperationException("Ngày xác nhận không thể sớm hơn lịch sử cũ.");
            var nextRecords=records.ToDictionary(x=>x.Key,x=>JsonSerializer.Deserialize<Entry>(
                JsonSerializer.Serialize(x.Value,Json),Json)!,StringComparer.Ordinal);
            if(!nextRecords.TryGetValue(source.Key,out var entry))
            {
                entry=new Entry
                {
                    Key=source.Key,Contract=source.Contract,Project=source.Project,
                    Company=source.Company,Material=source.Material,
                    Lot=source.Lot,Serial=source.Serial
                };
                nextRecords.Add(entry.Key,entry);
            }
            var choice=done?"Đã làm":"Chưa làm";
            if(entry.WorkChoice==choice)return;
            entry.WorkChoice=choice;
            entry.State=done?Completed:(entry.SentDay.Length>0?Sent:Pending);
            entry.CompletedDay=done?effectiveDay:"";
            entry.LastNote=note.Trim();
            entry.Events.Add(new History
            {
                State=entry.State,EffectiveDay=effectiveDay,
                RecordedAt=DateTimeOffset.Now.ToString("O"),
                By=Environment.UserName,Note="Tình trạng gia công: "+choice+
                    (entry.LastNote.Length>0?" · "+entry.LastNote:"")
            });
            var temp=_file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,JsonSerializer.Serialize(nextRecords.Values.ToList(),Json),Encoding.UTF8);
                if(File.Exists(_file))File.Copy(_file,_file+".bak",true);
                File.Move(temp,_file,true);
                _items=nextRecords;
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }

    /// <summary>Only an explicit operator action can advance a physical workflow.</summary>
    public void Advance(Item source,string next,string effectiveDay,string note)
    {
        if(!source.PresentInSource)throw new InvalidOperationException(
            "Dòng không còn trong Excel nguồn. Không đổi trạng thái khi chưa đối chiếu.");
        if(!DateTime.TryParseExact(effectiveDay,"yyyy-MM-dd",CultureInfo.InvariantCulture,
            DateTimeStyles.None,out var day))throw new ArgumentException(
                "Ngày thực tế phải theo yyyy-MM-dd.");
        if(day.Date>DateTime.Today)throw new InvalidOperationException(
            "Ngày đã làm hoặc bàn giao không thể ở tương lai.");
        lock(_gate)
        {
            var records=Load();
            records.TryGetValue(source.Key,out var previous);
            var current=previous?.State??Pending;
            if(current==Pending&&next!=Sent||
               current==Sent&&next!=Completed||
               current==Completed&&next!=HandedOver||
               current==HandedOver)
                throw new InvalidOperationException(
                     "Cần ghi đúng thứ tự: Đã gửi gia công tại Hưng Yên → Đã làm xong → Đã bàn giao. Trạng thái hiện tại: "+Name(current));
            if(previous?.Events.Count>0 &&
               string.CompareOrdinal(effectiveDay,previous.Events[^1].EffectiveDay)<0)
                throw new InvalidOperationException("Ngày xác nhận không thể sớm hơn giai đoạn trước.");
            // Build an independent candidate before writing. Never mutate
            // committed in-memory state until JSON has been safely published.
            var nextRecords=records.ToDictionary(x=>x.Key,x=>JsonSerializer.Deserialize<Entry>(
                JsonSerializer.Serialize(x.Value,Json),Json)!,StringComparer.Ordinal);
            if(!nextRecords.TryGetValue(source.Key,out var entry))
            {
                entry=new Entry
                {
                    Key=source.Key,Contract=source.Contract,Project=source.Project,
                    Company=source.Company,Material=source.Material,
                    Lot=source.Lot,Serial=source.Serial
                };
                nextRecords.Add(entry.Key,entry);
            }
            entry.State=next;
            if(next==Sent)entry.SentDay=effectiveDay;
            if(next==Completed)entry.CompletedDay=effectiveDay;
            if(next==HandedOver)entry.HandoverDay=effectiveDay;
            entry.LastNote=note.Trim();
            entry.Events.Add(new History
            {
                State=next,EffectiveDay=effectiveDay,
                RecordedAt=DateTimeOffset.Now.ToString("O"),
                By=Environment.UserName,Note=entry.LastNote
            });
            var temp=_file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,JsonSerializer.Serialize(nextRecords.Values.ToList(),Json),Encoding.UTF8);
                if(File.Exists(_file))File.Copy(_file,_file+".bak",true);
                File.Move(temp,_file,true);
                _items=nextRecords;
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
