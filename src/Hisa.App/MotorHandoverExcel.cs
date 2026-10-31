using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Synchronizes fixed column L in the ORIGINAL MÁY KÉO.xlsx:
/// explicit "Đã làm"/"Chưa làm" before delivery, real dates only from confirmed slips.
/// Never marks machines based on an HĐ alone or from a draft / projected shipment.
/// Tracking own writes enables safe reversal after a slip is cancelled.
/// </summary>
public static class MotorHandoverExcel
{
    private const string ColumnTitle="Ngày xuất kho";
    private const int StatusColumn=12; // Cột L: trạng thái gia công hoặc ngày xuất kho (hai chiều)
    private const string ReceiptFile="motor_handover_excel_sync.json";
    private static readonly JsonSerializerOptions Json=new() {WriteIndented=true};
    public sealed class Receipt
    {
        public string Identity { get; set; }="";
        // Legacy v1.9.17 receipts have no Column property and therefore belong to N.
        // They must never grant permission to overwrite a different value in L.
        public string Column { get; set; }="N";
        public string Date { get; set; }="";
        public string PreviousDate { get; set; }="";
    }
    public sealed record Outcome(int Written,int Cleared,int Unmatched,IReadOnlyList<string> Warnings);

    // A physical serial plus exact contract/project/company is stronger than a
    // free-text model description; the machine may be named differently in
    // Tổng hợp.xlsx and MÁY KÉO.xlsx. A duplicate matching key is never written.
    private static string Key(string contract,string project,string company,string serial)=>
        Rules.Identity(contract,project,company)+"|"+Rules.Code(serial);

    private static string Key(EquipmentListReader.Row row)=>
        Key(row.Contract,row.Project,row.Company,row.Serial);

    private static string Key(MotorSerialEntry serial)=>
        Key(serial.Contract,serial.Project,serial.Company,serial.SerialNumber);

    public static string IdentityFor(MotorHungYenStore.Item item)=>
        Key(item.Contract,item.Project,item.Company,item.Serial);

    public static Dictionary<string,string> ConfirmedDates(IEnumerable<Slip> slips)
    {
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        var ambiguous=new HashSet<string>(StringComparer.Ordinal);
        foreach(var slip in slips.Where(x=>x.Status=="delivered"))
        foreach(var serial in MotorSerials.ActiveRows(slip))
        {
            if(string.IsNullOrWhiteSpace(serial.SerialNumber) ||
               string.IsNullOrWhiteSpace(serial.Project) ||
               string.IsNullOrWhiteSpace(serial.Company) ||
               !DateTime.TryParseExact(slip.HandoverDate,"yyyy-MM-dd",
                   CultureInfo.InvariantCulture,DateTimeStyles.None,out _))continue;
            var id=Key(serial);
            if(values.TryGetValue(id,out var earlier)&&earlier!=slip.HandoverDate)
                ambiguous.Add(id);
            else values[id]=slip.HandoverDate;
        }
        foreach(var key in ambiguous)values.Remove(key);
        return values;
    }

    private static bool IsWorkText(string value)=>value is "Đã làm" or "Chưa làm";

    private static string DateText(IXLCell cell)
    {
        if(cell.IsEmpty())return "";
        if(cell.TryGetValue<DateTime>(out var date))
            return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(cell.TryGetValue<double>(out var serial) && serial>10000 && serial<80000)
            return DateTime.FromOADate(serial).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        var value=cell.GetString().Trim();
        if(DateTime.TryParse(value,CultureInfo.GetCultureInfo("vi-VN"),
            DateTimeStyles.None,out date))return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        return value;
    }

    private static List<Receipt> ReadReceipts(string path)
    {
        if(!File.Exists(path))return [];
        try
        {
            var data=JsonSerializer.Deserialize<List<Receipt>>(File.ReadAllText(path),Json) ??
                throw new InvalidDataException("Lịch sử đồng bộ ngày bàn giao rỗng.");
            if(data.Any(x=>string.IsNullOrEmpty(x.Identity)) ||
               data.Select(x=>x.Column+"|"+x.Identity).Distinct(StringComparer.Ordinal).Count()!=data.Count)
                throw new InvalidDataException("Lịch sử đồng bộ có khóa trùng hoặc thiếu.");
            return data;
        }
        catch(JsonException ex)
        {
            throw new InvalidDataException(
                "Không đọc được lịch sử đồng bộ ngày bàn giao. Giữ nguyên Excel, không ghi đè.",ex);
        }
    }

    private static void PersistReceipts(string path,IReadOnlyList<Receipt> rows)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,JsonSerializer.Serialize(rows,Json),new UTF8Encoding(false));
            if(File.Exists(path))File.Copy(path,path+".bak",true);
            File.Move(temp,path,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }

    private static void SaveExcel(XLWorkbook book,string excelPath,string backupDir)
    {
        var folder=Path.GetDirectoryName(Path.GetFullPath(excelPath))!;
        var temp=Path.Combine(folder,"."+Path.GetFileNameWithoutExtension(excelPath)+"."+
            Guid.NewGuid().ToString("N")+".tmp.xlsx");
        try
        {
            // Respect an open/locked production workbook and always retain its
            // complete pre-write bytes before updating the actual original.
            using(var locked=new FileStream(excelPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
            Directory.CreateDirectory(backupDir);
            var backup=Path.Combine(backupDir,"MAY-KEO-truoc-ngay-ban-giao-"+
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+
                Guid.NewGuid().ToString("N")[..8]+".xlsx");
            File.Copy(excelPath,backup,false);
            book.SaveAs(temp,new SaveOptions{
                EvaluateFormulasBeforeSaving=false,GenerateCalculationChain=false
            });
            File.Move(temp,excelPath,true);
        }
        catch(IOException e)
        {
            throw new IOException("Không thể cập nhật ngày bàn giao trong MÁY KÉO.xlsx. "+
                "Hãy lưu và đóng file Excel trước khi thử lại; phiếu đã xuất vẫn được giữ. "+e.Message,e);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }

    public static Outcome Sync(AppSettings settings,IEnumerable<Slip> slips,
        string dataDirectory,string excelBackupDirectory,string? explicitWorkIdentity=null)
    {
        var warnings=new List<string>();
        var path=settings.TractionMachineListPath;
        var receiptPath=Path.Combine(dataDirectory,ReceiptFile);
        var allReceipts=ReadReceipts(receiptPath);
        var legacy=allReceipts.Where(x=>!string.Equals(x.Column,"L",StringComparison.OrdinalIgnoreCase)).ToList();
        var prior=allReceipts.Where(x=>string.Equals(x.Column,"L",StringComparison.OrdinalIgnoreCase)).ToList();
        var incomplete=slips.Where(x=>x.Status=="delivered")
            .SelectMany(x=>x.Lines.Where(MotorSerials.IsMotor))
            .Any(x=>x.Quantity is >0);
        if(string.IsNullOrWhiteSpace(path))
        {
            if(incomplete)warnings.Add("Chưa cấu hình MÁY KÉO.xlsx; không thể đồng bộ ngày bàn giao theo seri.");
            return new Outcome(0,0,0,warnings);
        }
        if(!File.Exists(path))
        {
            if(prior.Count==0&&!incomplete)return new Outcome(0,0,0,warnings);
            throw new IOException("Không tìm thấy MÁY KÉO.xlsx ở đường dẫn đã lưu. "+
                "Phiếu đã xác nhận xuất được giữ nguyên; cần kiểm tra Kho HISA rồi đồng bộ lại.");
        }
        var wished=new Dictionary<string,string>(StringComparer.Ordinal);
        var conflict=new HashSet<string>(StringComparer.Ordinal);
        foreach(var slip in slips.Where(x=>x.Status=="delivered"))
        foreach(var serial in MotorSerials.ActiveRows(slip))
        {
            if(string.IsNullOrWhiteSpace(serial.SerialNumber) ||
               string.IsNullOrWhiteSpace(serial.Project) ||
               string.IsNullOrWhiteSpace(serial.Company))continue;
            if(!DateTime.TryParseExact(slip.HandoverDate,"yyyy-MM-dd",
                CultureInfo.InvariantCulture,DateTimeStyles.None,out _))continue;
            var id=Key(serial);
            if(wished.TryGetValue(id,out var old) && old!=slip.HandoverDate)
                conflict.Add(id);
            else wished[id]=slip.HandoverDate;
        }
        using var book=new XLWorkbook(path);
        var sheet=book.Worksheets.First();
        if(Rules.Code(sheet.Cell(2,4).GetString())!="HD")
            throw new InvalidDataException("MÁY KÉO.xlsx không có tiêu đề HĐ tại cột D, dòng 2. Không sửa file.");
        // The user's source template reserves column L for either a work-state
        // word or the real confirmed delivery date. Never append a new column,
        // and never overwrite an unrelated original column L.
        var dateColumn=StatusColumn;
        var header=sheet.Cell(2,dateColumn).GetString().Trim();
        if(header.Length>0 && Rules.Code(header) is not ("NGAYBANGIAO" or
            "NGAYXUATKHO" or "TRANGTHAI" or "TINHTRANG"))
            throw new InvalidDataException("Cột L đang có tiêu đề '"+header+
                "'. HISA không ghi đè; hãy kiểm tra mẫu MÁY KÉO.xlsx.");
        var newlyAdded=header.Length==0;
        if(newlyAdded && sheet.Column(dateColumn).CellsUsed()
            .Any(c=>c.Address.RowNumber>=3 && !IsWorkText(DateText(c)) &&
                !DateTime.TryParseExact(DateText(c),"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _)))
            throw new InvalidDataException("Cột L không có tiêu đề nhưng chứa dữ liệu ngoài Chưa làm / Đã làm / ngày xuất kho. HISA không ghi đè.");
        var rows=EquipmentListReader.Read(path,"Máy kéo");
        var workItems=new MotorHungYenStore(dataDirectory).List(rows);
        foreach(var item in workItems.Where(x=>x.PresentInSource &&
            !string.IsNullOrWhiteSpace(x.Serial) &&
            !string.IsNullOrWhiteSpace(x.Project) &&
            !string.IsNullOrWhiteSpace(x.Company)))
        {
            var choice=item.WorkChoice;
            if(choice.Length==0 && item.State==MotorHungYenStore.Completed)
                choice="Đã làm"; // Existing v1.9.13 work confirmations
            if(!IsWorkText(choice))continue;
            var id=IdentityFor(item);
            if(!wished.ContainsKey(id))wished[id]=choice;
        }
        if(wished.Count==0 && prior.Count==0)
            return new Outcome(0,0,0,warnings);
        var indexed=rows.Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
            .GroupBy(Key).ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.Ordinal);
        var next=new Dictionary<string,Receipt>(StringComparer.Ordinal);
        var priorByKey=prior.ToDictionary(x=>x.Identity,StringComparer.Ordinal);
        int written=0,cleared=0,unmatched=0;
        // Plan every mutation before touching any workbook cell. Only unique
        // HĐ + project + company + model + serial identities can be marked.
        var actions=new List<(int Row,string? NewDay,string Identity,string PreviousDay)>();
        foreach(var (identity,day) in wished)
        {
            if(conflict.Contains(identity))
            {
                warnings.Add("Một máy kéo có nhiều ngày xuất khác nhau. Chưa ghi Excel: "+identity);
                unmatched++;continue;
            }
            if(!indexed.TryGetValue(identity,out var candidates)||candidates.Count!=1)
            {
                unmatched++;
                warnings.Add("Không có đúng một máy kéo khớp HĐ / công trình / đơn vị / seri. Chưa ghi: "+identity);
                continue;
            }
            var cell=sheet.Cell(candidates[0].SourceRow,dateColumn);
            var existing=DateText(cell);
            priorByKey.TryGetValue(identity,out var owned);
            // Excel is also an input: a human's change in L must not be
            // silently reverted by background reconciliation with stale JSON.
            // Only an explicit click in HISA may replace one work word with another.
            bool explicitWork=explicitWorkIdentity==identity && IsWorkText(day) &&
                IsWorkText(existing);
            if(owned==null && existing.Length>0 && existing!=day &&
               !(IsWorkText(existing) && !IsWorkText(day)) && !explicitWork)
            {
                unmatched++;
                warnings.Add("Cột L có dữ liệu nhập tay khác trạng thái dự kiến; không ghi đè: "+identity);
                continue;
            }
            if(owned!=null&&existing!=owned.Date&&existing!=day && !explicitWork)
            {
                unmatched++;
                warnings.Add("Cột L đã bị sửa bên ngoài HISA. Không ghi đè: "+identity);
                continue;
            }
            // An already-matching value typed by the operator stays operator-owned.
            // Claim ownership only when HISA actually changes the cell.
            if(owned==null && existing==day)continue;
            actions.Add((candidates[0].SourceRow,day,identity,owned?.PreviousDate??existing));
            next[identity]=new Receipt{Identity=identity,Column="L",Date=day,
                PreviousDate=explicitWork && owned!=null && existing!=owned.Date
                    ? existing : owned?.PreviousDate??existing};
        }
        foreach(var old in prior)
        {
            if(next.ContainsKey(old.Identity))continue;
            if(!indexed.TryGetValue(old.Identity,out var matched)||matched.Count!=1)
            {
                // Preserve receipts for rows that move or disappear so a later
                // refresh can safely find the old application-owned value.
                next[old.Identity]=old;
                warnings.Add("Không tìm thấy đúng một dòng để hoàn tác ngày bàn giao cũ: "+old.Identity);
                continue;
            }
            var cell=sheet.Cell(matched[0].SourceRow,dateColumn);
            if(DateText(cell)!=old.Date)
            {
                // External edit: never clear or replace a human-entered date.
                warnings.Add("Ngày bàn giao đã được sửa thủ công; giữ nguyên: "+old.Identity);
                continue;
            }
            actions.Add((matched[0].SourceRow,old.PreviousDate,old.Identity,""));
        }
        if(newlyAdded)
        {
            sheet.Cell(2,dateColumn).Value=ColumnTitle;
            sheet.Cell(2,dateColumn).Style.Font.Bold=true;
            sheet.Column(dateColumn).Width=20;
        }
        foreach(var action in actions)
        {
            var cell=sheet.Cell(action.Row,dateColumn);
            if(DateText(cell)==(action.NewDay??""))continue;
            if(string.IsNullOrEmpty(action.NewDay))
            {
                cell.Clear(XLClearOptions.Contents);
                cleared++;
            }
            else if(DateTime.TryParseExact(action.NewDay,"yyyy-MM-dd",
                CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))
            {
                cell.Value=day;
                cell.Style.NumberFormat.Format="dd/MM/yyyy";
                written++;
            }
            else if(IsWorkText(action.NewDay??""))
            {
                cell.Value=action.NewDay!;
                written++;
            }
        }
        if(newlyAdded||written>0||cleared>0)
            SaveExcel(book,path,excelBackupDirectory);
        // The receipt file only changes after Excel was safely committed.
        if(prior.Count!=next.Count || prior.Any(x=>!next.TryGetValue(x.Identity,out var y)||
            y.Date!=x.Date||y.PreviousDate!=x.PreviousDate))
            PersistReceipts(receiptPath,legacy.Concat(next.Values)
                .OrderBy(x=>x.Column).ThenBy(x=>x.Identity).ToList());
        return new Outcome(written,cleared,unmatched,warnings);
    }
}
