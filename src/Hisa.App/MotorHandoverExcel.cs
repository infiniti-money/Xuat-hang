using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Synchronizes the "Ngày bàn giao" column in the ORIGINAL MÁY KÉO.xlsx
/// exclusively from confirmed delivered slips with individually verified serials.
/// Never marks machines based on an HĐ alone or from a draft / projected shipment.
/// Tracking own writes enables safe reversal after a slip is cancelled.
/// </summary>
public static class MotorHandoverExcel
{
    private const string ColumnTitle="Ngày bàn giao";
    private const string ReceiptFile="motor_handover_excel_sync.json";
    private static readonly JsonSerializerOptions Json=new() {WriteIndented=true};
    public sealed class Receipt
    {
        public string Identity { get; set; }="";
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
               data.Select(x=>x.Identity).Distinct(StringComparer.Ordinal).Count()!=data.Count)
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

    internal static void SaveExcel(XLWorkbook book,string excelPath,string backupDir)
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
            var backup=Path.Combine(backupDir,"MAY-KEO-truoc-cap-nhat-"+
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
        string dataDirectory,string excelBackupDirectory)
    {
        var warnings=new List<string>();
        var path=settings.TractionMachineListPath;
        var receiptPath=Path.Combine(dataDirectory,ReceiptFile);
        var prior=ReadReceipts(receiptPath);
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
        if(wished.Count==0 && prior.Count==0)return new Outcome(0,0,0,warnings);
        using var book=new XLWorkbook(path);
        var sheet=book.Worksheets.First();
        if(Rules.Code(sheet.Cell(2,4).GetString())!="HD")
            throw new InvalidDataException("MÁY KÉO.xlsx không có tiêu đề HĐ tại cột D, dòng 2. Không sửa file.");
        // The user-selected canonical field is N, not an appended arbitrary column.
        // Legacy v1.9.13 workbooks may have an older 'Ngày bàn giao' header in K/L.
        var dateColumn=MotorProductionExcel.ProgressColumn;
        var targetHeading=sheet.Cell(2,dateColumn).GetString().Trim();
        if(targetHeading.Length>0 && Rules.Code(targetHeading)!="NGAYBANGIAO")
            throw new InvalidDataException("Cột N đang thuộc dữ liệu khác. Không ghi đè file.");
        var newlyAdded=targetHeading.Length==0;
        var lastColumn=Math.Min(sheet.LastColumnUsed()?.ColumnNumber()??10,160);
        var legacyColumn=Enumerable.Range(1,lastColumn)
            .FirstOrDefault(c=>c!=dateColumn &&
                Rules.Code(sheet.Cell(2,c).GetString())=="NGAYBANGIAO");
        var rows=EquipmentListReader.Read(path,"Máy kéo");
        var migrated=0;
        if(legacyColumn>0)
        {
            foreach(var row in rows)
            {
                var target=sheet.Cell(row.SourceRow,dateColumn);
                var old=sheet.Cell(row.SourceRow,legacyColumn);
                if(target.IsEmpty()&&!old.IsEmpty())
                {
                    target.Value=old.Value;
                    target.Style.NumberFormat.Format=old.Style.NumberFormat.Format;
                    migrated++;
                }
                else if(!target.IsEmpty()&&!old.IsEmpty() && DateText(target)!=DateText(old))
                    warnings.Add("Cột N khác ngày bàn giao cũ; giữ N và không xóa dữ liệu cũ: "+
                        row.Contract+" / "+row.Serial);
            }
        }
        if(newlyAdded && rows.Any(row=>
            MotorProductionExcel.Status(DateText(sheet.Cell(row.SourceRow,dateColumn)))=="Cần đối chiếu"))
            throw new InvalidDataException("Cột N chứa dữ liệu không nhận diện được. Chưa sửa file.");
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
            if(owned==null && existing.Length>0 && existing!=day &&
                MotorProductionExcel.Status(existing) is not ("Đã làm" or "Chưa làm"))
            {
                unmatched++;
                warnings.Add("Dòng máy kéo đã có ngày bàn giao nhập thủ công khác ngày phiếu. Không ghi đè: "+identity);
                continue;
            }
            if(owned!=null&&existing!=owned.Date&&existing!=day)
            {
                unmatched++;
                warnings.Add("Ngày trong Excel đã bị thay đổi bên ngoài HISA. Không ghi đè: "+identity);
                continue;
            }
            actions.Add((candidates[0].SourceRow,day,identity,owned?.PreviousDate??existing));
            next[identity]=new Receipt{Identity=identity,Date=day,PreviousDate=owned?.PreviousDate??existing};
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
            sheet.Column(dateColumn).Width=18;
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
            else if(MotorProductionExcel.Status(action.NewDay) is "Đã làm" or "Chưa làm")
            {
                // Cancelling a delivery restores the operator's original text.
                cell.Value=action.NewDay;
                written++;
            }
        }
        if(newlyAdded||migrated>0||written>0||cleared>0)
            SaveExcel(book,path,excelBackupDirectory);
        // The receipt file only changes after Excel was safely committed.
        if(prior.Count!=next.Count || prior.Any(x=>!next.TryGetValue(x.Identity,out var y)||
            y.Date!=x.Date||y.PreviousDate!=x.PreviousDate))
            PersistReceipts(receiptPath,next.Values.OrderBy(x=>x.Identity).ToList());
        return new Outcome(written,cleared,unmatched,warnings);
    }
}
