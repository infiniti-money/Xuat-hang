using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Synchronizes confirmed delivery dates only into column K of MÁY KÉO.xlsx.
/// Matches on full identity first, or unique HĐ + physical serial when
/// project/company spelling differs between slip and workbook. Never assigns
/// a date using the contract number alone or from a draft shipment.
/// Tracking own writes enables safe reversal after a slip is cancelled.
/// </summary>
public static class MotorHandoverExcel
{
    private const string ColumnTitle="Ngày bàn giao";
    private const int StatusColumn=11; // Cột K: NGÀY BÀN GIAO thực tế, không ghi trạng thái chữ.
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
    public sealed record Outcome(int Written,int Cleared,int Unmatched,IReadOnlyList<string> Warnings,
        int SerialWritten=0);

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

    public static Outcome Sync(AppSettings settings,IEnumerable<Slip> slips,
        string dataDirectory,string excelBackupDirectory,string? explicitWorkIdentity=null,
        string? contractScope=null)
    {
        var warnings=new List<string>();
        var scope=Rules.Contract(contractScope);
        var limited=!string.IsNullOrWhiteSpace(contractScope);
        var allSlips=slips.ToList();
        var path=settings.TractionMachineListPath;
        var receiptPath=Path.Combine(dataDirectory,ReceiptFile);
        var allReceipts=ReadReceipts(receiptPath);
        // Old versions wrote "Đã làm/Chưa làm" into K. Preserve their
        // receipts as history, but DO NOT use them to restore work text when
        // a delivered slip is cancelled. A blank K means not handed over.
        var legacy=allReceipts.Where(x=>
            !string.Equals(x.Column,"K",StringComparison.OrdinalIgnoreCase) ||
            IsWorkText(x.Date) ||
            (limited && !x.Identity.StartsWith(scope+"|",StringComparison.Ordinal))).ToList();
        var prior=allReceipts.Where(x=>
            string.Equals(x.Column,"K",StringComparison.OrdinalIgnoreCase) &&
            !IsWorkText(x.Date) &&
            (!limited || x.Identity.StartsWith(scope+"|",StringComparison.Ordinal))).ToList();
        var incomplete=allSlips.Where(x=>x.Status=="delivered")
            .SelectMany(x=>x.Lines.Where(MotorSerials.IsMotor))
            .Any(x=>x.Quantity is >0 &&
                (!limited || Rules.Contract(x.Contract)==scope));
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
        var requests=new Dictionary<string,(string Contract,string Serial)>(
            StringComparer.Ordinal);
        var conflict=new HashSet<string>(StringComparer.Ordinal);
        var missingSerialSlots=0;
        foreach(var slip in allSlips.Where(x=>x.Status=="delivered"))
        {
            var motors=slip.Lines.Where(x=>MotorSerials.IsMotor(x) &&
                (!limited || Rules.Contract(x.Contract)==scope)).ToList();
            var wanted=motors.Sum(x=>x.Quantity is >0 ?
                checked((int)decimal.Truncate(x.Quantity.Value)) : 0);
            if(wanted==0)continue;
            var contracts=string.Join(", ",motors.Select(x=>x.Contract)
                .DistinctBy(Rules.Contract));
            if(!DateTime.TryParseExact(slip.HandoverDate,"yyyy-MM-dd",
                CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            {
                missingSerialSlots+=wanted;
                warnings.Add("Phiếu "+slip.SlipCode+" / HĐ "+contracts+
                    " đã xuất nhưng không có ngày xuất hợp lệ. "+
                    "Kiểm tra ngày trên phiếu trước khi đồng bộ cột K.");
                continue;
            }
            var filled=MotorSerials.ActiveRows(slip)
                .Count(x=>!limited || Rules.Contract(x.Contract)==scope);
            if(wanted<=filled)continue;
            missingSerialSlots+=wanted-filled;
            warnings.Add("Phiếu "+slip.SlipCode+" / HĐ "+contracts+
                " đã xuất nhưng thiếu "+(wanted-filled)+
                " serial máy kéo. Không tự đoán ngày bàn giao theo số HĐ; "+
                "kiểm tra serial trên phiếu và cột I MÁY KÉO.xlsx.");
        }
        foreach(var slip in allSlips.Where(x=>x.Status=="delivered"))
        foreach(var serial in MotorSerials.ActiveRows(slip))
        {
            if(limited && Rules.Contract(serial.Contract)!=scope)continue;
            if(string.IsNullOrWhiteSpace(serial.SerialNumber) ||
               string.IsNullOrWhiteSpace(serial.Project) ||
               string.IsNullOrWhiteSpace(serial.Company))continue;
            if(!DateTime.TryParseExact(slip.HandoverDate,"yyyy-MM-dd",
                CultureInfo.InvariantCulture,DateTimeStyles.None,out _))continue;
            var id=Key(serial);
            requests[id]=(serial.Contract,serial.SerialNumber);
            if(wished.TryGetValue(id,out var old) && old!=slip.HandoverDate)
                conflict.Add(id);
            else wished[id]=slip.HandoverDate;
        }
        // Capture the exact workbook generation BEFORE reading/planning.
        // The patcher checks the same SHA-256 while holding an exclusive lock.
        // A changed source must never receive I/K edits from a stale plan.
        string sourceHash;
        using(var stream=File.OpenRead(path))
            sourceHash=Convert.ToHexString(SHA256.HashData(stream));
        using var book=new XLWorkbook(path);
        var sheet=book.Worksheets.First();
        if(Rules.Code(sheet.Cell(2,4).GetString())!="HD")
            throw new InvalidDataException("MÁY KÉO.xlsx không có tiêu đề HĐ tại cột D, dòng 2. Không sửa file.");
        // The user's source template reserves column K for either a work-state
        // word or the real confirmed delivery date. Never append a new column,
        // and never overwrite an unrelated original column K.
        var dateColumn=StatusColumn;
        var header=sheet.Cell(2,dateColumn).GetString().Trim();
        if(header.Length>0 && Rules.Code(header) is not ("NGAYBANGIAO" or
            "NGAYXUATKHO" or "TRANGTHAI" or "TINHTRANG" or
            "TRANGTHAIMAYKEO" or "TRANGTHAIGIACONG" or "TINHTRANGMAYKEO" or
            "TINHTRANGLAM"))
            throw new InvalidDataException("Cột K đang có tiêu đề '"+header+
                "'. HISA không ghi đè; hãy kiểm tra mẫu MÁY KÉO.xlsx.");
        var newlyAdded=header.Length==0;
        if(newlyAdded && sheet.Column(dateColumn).CellsUsed()
            .Any(c=>c.Address.RowNumber>=3 && !IsWorkText(DateText(c)) &&
                !DateTime.TryParseExact(DateText(c),"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _)))
            throw new InvalidDataException("Cột K không có tiêu đề nhưng chứa dữ liệu ngoài Chưa làm / Đã làm / ngày xuất kho. HISA không ghi đè.");
        // Read status only from Excel. Work progress is kept in the HISA
        // tracking JSON, never written to production column K.
        var rows=EquipmentListReader.Read(path,"Máy kéo");
        if(wished.Count==0 && prior.Count==0)
            return new Outcome(0,0,missingSerialSlots,warnings);

        // For a CONFIRMED delivered slip, plan new column-I serials using the
        // same one-to-one contract/project rules as the manual serial dialog.
        // No draft, guessed project, second choice, or occupied I cell can
        // authorize an automatic serial write. Serial and date edits are
        // committed together as ONE validated OOXML patch and ONE backup.
        var serialCandidates=new List<MotorSerialExcelSync.Assignment>();
        foreach(var slip in allSlips.Where(x=>x.Status=="delivered" &&
            x.Lines.Any(line=>MotorSerials.IsMotor(line) &&
                (!limited || Rules.Contract(line.Contract)==scope))))
        {
            try
            {
                var preview=MotorSerialExcelSync.Preview(settings,slip);
                foreach(var warning in preview.Warnings)
                    warnings.Add("Phiếu "+slip.SlipCode+": "+warning);
                serialCandidates.AddRange(preview.Proposed.Where(x=>
                    !limited || Rules.Contract(x.Contract)==scope));
            }
            catch(Exception e) when(e is IOException or InvalidDataException or
                InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            {
                warnings.Add("Phiếu "+slip.SlipCode+
                    ": chưa thể ghép serial mới vào cột I: "+e.Message);
            }
        }
        var staged=new Dictionary<int,MotorSerialExcelSync.Assignment>();
        var sourceByRow=rows.ToDictionary(x=>x.SourceRow);
        foreach(var group in serialCandidates.GroupBy(x=>x.SourceRow))
        {
            if(group.Count()!=1)
            {
                warnings.Add("Dòng máy kéo "+group.Key+
                    " được nhiều phiếu chọn để ghi serial. Giữ nguyên cột I/K.");
                continue;
            }
            var assignment=group.Single();
            if(!sourceByRow.TryGetValue(assignment.SourceRow,out var target) ||
               Rules.Contract(target.Contract)!=Rules.Contract(assignment.Contract) ||
               Rules.Code(target.Project)!=Rules.Code(assignment.Project) ||
               Rules.Code(target.Company)!=Rules.Code(assignment.Company) ||
               !string.IsNullOrWhiteSpace(target.Serial))
            {
                warnings.Add("Dòng máy kéo "+assignment.SourceRow+
                    " không còn khớp đúng HĐ/công trình hoặc serial I đã có. "+
                    "Không tự ghi.");
                continue;
            }
            staged.Add(assignment.SourceRow,assignment);
            // Temporary planning only; the original Excel remains unchanged
            // until the final atomic I+K edit passes validation.
            target.Serial=assignment.Serial;
        }
        var populated=rows.Where(x=>!string.IsNullOrWhiteSpace(x.Serial)).ToList();
        var indexed=populated.GroupBy(Key)
            .ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.Ordinal);
        // The serial is the physical identifier. A project name may contain
        // a Zalo OCR address, while Excel stores a shorter name. Allow only a
        // UNIQUE physical serial belonging to the exact same HĐ; if that
        // serial is duplicated anywhere in Excel, fail closed.
        var byContractAndSerial=populated.GroupBy(x=>
                Rules.Contract(x.Contract)+"|"+Rules.Code(x.Serial))
            .ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.Ordinal);
        var bySerial=populated.GroupBy(x=>Rules.Code(x.Serial))
            .ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.Ordinal);
        var next=new Dictionary<string,Receipt>(StringComparer.Ordinal);
        var priorByKey=prior.ToDictionary(x=>x.Identity,StringComparer.Ordinal);
        int written=0,cleared=0,unmatched=missingSerialSlots;
        var validDeliveryRows=new HashSet<int>();
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
            indexed.TryGetValue(identity,out var candidates);
            var matched=candidates?.Count==1?candidates[0]:null;
            var (contract,serial)=requests[identity];
            var normalizedContract=Rules.Contract(contract);
            var normalizedSerial=Rules.Code(serial);
            var fallbackKey=normalizedContract+"|"+normalizedSerial;
            if(!bySerial.TryGetValue(normalizedSerial,out var allSameSerial) ||
                allSameSerial.Count!=1)
            {
                unmatched++;
                warnings.Add("HĐ "+contract+" · serial "+serial+
                    ": serial vắng mặt hoặc trùng trên nhiều dòng máy kéo. "+
                    "Không ghi cột K; kiểm tra cột I.");
                continue;
            }
            if(matched==null)
            {
                if(byContractAndSerial.TryGetValue(fallbackKey,out var exactPhysical) &&
                    exactPhysical.Count==1)
                {
                    matched=exactPhysical[0];
                    warnings.Add("HĐ "+contract+" · serial "+serial+
                        ": tên công trình/đơn vị trên phiếu khác Excel; "+
                        "đã đối chiếu bằng số HĐ và serial vật lý duy nhất.");
                }
                else
                {
                    unmatched++;
                    warnings.Add("HĐ "+contract+" · serial "+serial+
                        ": chưa tìm được đúng một máy kéo trong cột D/I. "+
                        "Chưa ghi ngày bàn giao cột K.");
                    continue;
                }
            }
            var cell=sheet.Cell(matched.SourceRow,dateColumn);
            var existing=DateText(cell);
            priorByKey.TryGetValue(identity,out var owned);
            // Only delivered slips create a date. Existing human-entered
            // dates are authoritative and are never replaced. Legacy "Đã làm"
            // may be upgraded to an actual date after confirmed shipment.
            if(owned==null && existing.Length>0 && existing!=day &&
               !IsWorkText(existing))
            {
                unmatched++;
                warnings.Add("Cột K có dữ liệu nhập tay khác trạng thái dự kiến; không ghi đè: "+identity);
                continue;
            }
            if(owned!=null&&existing!=owned.Date&&existing!=day)
            {
                unmatched++;
                warnings.Add("Cột K đã bị sửa bên ngoài HISA. Không ghi đè: "+identity);
                continue;
            }
            // An already-matching value typed by the operator stays operator-owned.
            // Claim ownership only when HISA actually changes the cell.
            if(owned==null && existing==day)
            {
                validDeliveryRows.Add(matched.SourceRow);
                continue;
            }
            validDeliveryRows.Add(matched.SourceRow);
            // No date means in production/not yet handed over. On reversal
            // restore an EMPTY K, not the old work-text status.
            actions.Add((matched.SourceRow,day,identity,""));
            next[identity]=new Receipt{Identity=identity,Column="K",Date=day,
                PreviousDate=""};
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
            actions.Add((matched[0].SourceRow,"",old.Identity,""));
        }
        var edits=new List<MotorWorkbookCellPatch.Edit>();
        // Do not write even a correct serial into I unless its physical row
        // has a verified confirmed delivery date to write (or already has
        // exactly that date). An unresolved K means the I/K pair stays pending.
        var newSerialEdits=staged.Where(x=>validDeliveryRows.Contains(x.Key))
            .Select(x=>new MotorWorkbookCellPatch.Edit(
                x.Key,9,x.Value.Serial)).ToList();
        edits.AddRange(newSerialEdits);
        if(newlyAdded && actions.Count>0)
            edits.Add(new MotorWorkbookCellPatch.Edit(2,dateColumn,ColumnTitle));
        foreach(var action in actions)
        {
            var cell=sheet.Cell(action.Row,dateColumn);
            if(DateText(cell)==(action.NewDay??""))continue;
            if(action.NewDay==null || action.NewDay.Length==0)
            {
                edits.Add(new MotorWorkbookCellPatch.Edit(action.Row,dateColumn,null));
                cleared++;
            }
            else if(DateTime.TryParseExact(action.NewDay,"yyyy-MM-dd",
                CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            {
                edits.Add(new MotorWorkbookCellPatch.Edit(action.Row,dateColumn,
                    action.NewDay,true));
                written++;
            }
            else
                throw new InvalidDataException(
                    "Cột K chỉ nhận ngày bàn giao đã xác nhận, không ghi trạng thái chữ.");
        }
        // Stage I and K together in one validated worksheet patch, with
        // a backup and full original-workbook SHA-256 lease. Never use two
        // independent saves where the serial could be written but K fails.
        if(edits.Count>0)
            MotorWorkbookCellPatch.Apply(path,excelBackupDirectory,edits,
                "MAY-KEO-truoc-serial-va-ngay-ban-giao",sourceHash);
        // The receipt file only changes after Excel was safely committed.
        if(prior.Count!=next.Count || prior.Any(x=>!next.TryGetValue(x.Identity,out var y)||
            y.Date!=x.Date||y.PreviousDate!=x.PreviousDate))
            PersistReceipts(receiptPath,legacy
                .Where(x=>!string.Equals(x.Column,"K",StringComparison.OrdinalIgnoreCase) ||
                    !next.ContainsKey(x.Identity))
                .Concat(next.Values)
                .OrderBy(x=>x.Column).ThenBy(x=>x.Identity).ToList());
        return new Outcome(written,cleared,unmatched,warnings,
            newSerialEdits.Count);
    }
}
