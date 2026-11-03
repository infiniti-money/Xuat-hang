using System.Globalization;
using System.IO;
using System.IO.Compression;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Read-only health report for the operator's original MÁY KÉO.xlsx.
/// Checks the ZIP container and the machine sheet; NEVER invokes Save/SaveAs,
/// restores a backup, changes a serial or edits a delivery date.
/// It cannot certify that Excel itself would not display a repair warning.
/// </summary>
public static class MotorWorkbookHealth
{
    public sealed record Finding(string Level,string Detail);
    public sealed record Report(
        int Machines,int HandedOver,int Working,IReadOnlyList<Finding> Findings)
    {
        public bool CanWrite => !Findings.Any(x=>x.Level=="Lỗi");
        public string Summary => (CanWrite?"Đọc được file nguồn.":"KHÔNG NÊN GHI FILE.")+
            $" {Machines} dòng máy kéo; {HandedOver} đã có ngày bàn giao K; "+
            $"{Working} chưa có ngày bàn giao K.";
    }

    private static readonly string[] MandatoryParts=[
        "[Content_Types].xml","_rels/.rels",
        "xl/workbook.xml","xl/_rels/workbook.xml.rels"];

    public static Report Inspect(string path)
    {
        var findings=new List<Finding>();
        void Problem(string message)=>findings.Add(new Finding("Lỗi",message));
        void Caution(string message)=>findings.Add(new Finding("Cảnh báo",message));
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
        {
            Problem("Chưa chọn được file MÁY KÉO.xlsx. Kiểm tra Cấu hình nguồn dữ liệu.");
            return new Report(0,0,0,findings);
        }

        var rows=0;var handed=0;var working=0;
        try
        {
            var original=new FileInfo(path);
            var originalLength=original.Length;
            var originalStamp=original.LastWriteTimeUtc;
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,
                FileShare.ReadWrite|FileShare.Delete))
            using(var zip=new ZipArchive(input,ZipArchiveMode.Read))
            {
                var parts=zip.Entries.Select(x=>x.FullName).ToList();
                var duplicated=parts.GroupBy(x=>x,StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(x=>x.Count()>1);
                if(duplicated!=null)
                    Problem("Workbook chứa nhiều thành phần ZIP trùng tên: "+
                        duplicated.Key);
                foreach(var required in MandatoryParts)
                    if(!parts.Contains(required,StringComparer.Ordinal))
                        Problem("Workbook thiếu thành phần bắt buộc: "+required);
                if(!parts.Any(x=>x.StartsWith("xl/worksheets/",StringComparison.Ordinal) &&
                                  x.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)))
                    Problem("Workbook không có dữ liệu worksheet.");
                // Read every ZIP entry, not merely the directory, so corrupt
                // compressed data cannot silently pass a superficial check.
                foreach(var entry in zip.Entries.Where(x=>!x.FullName.EndsWith('/')))
                {
                    using var data=entry.Open();
                    data.CopyTo(Stream.Null);
                }
            }
            if(findings.Any(x=>x.Level=="Lỗi"))
                return new Report(0,0,0,findings);

            using(var book=new XLWorkbook(path))
            {
                var sheet=book.Worksheets.FirstOrDefault();
                if(sheet==null)
                    Problem("Không tìm thấy sheet máy kéo.");
                else
                {
                    if(Rules.Code(sheet.Cell(2,4).GetString())!="HD")
                        Problem("Thiếu tiêu đề hợp đồng HĐ tại D2.");
                    var serialHeader=Rules.Code(sheet.Cell(2,9).GetString());
                    if(serialHeader.Length>0 &&
                        !serialHeader.Contains("SERI",StringComparison.Ordinal) &&
                        serialHeader is not ("SN" or "SERIALNO"))
                        Problem("Cột I có tiêu đề không phải Serial/Seri.");
                    var handoverHeader=Rules.Code(sheet.Cell(2,11).GetString());
                    if(handoverHeader.Length>0 &&
                        handoverHeader is not (
                            "NGAYBANGIAO" or "NGAYXUATKHO" or
                            "TRANGTHAI" or "TINHTRANG" or
                            "TRANGTHAIMAYKEO" or "TRANGTHAIGIACONG" or
                            "TINHTRANGMAYKEO" or "TINHTRANGLAM"))
                        Problem("Cột K không phải ngày bàn giao/trạng thái máy kéo: "+
                            sheet.Cell(2,11).GetString());
                }
            }

            if(findings.Any(x=>x.Level=="Lỗi"))
                return new Report(0,0,0,findings);
            var machines=EquipmentListReader.Read(path,"Máy kéo");
            rows=machines.Count;
            if(rows==0)Problem("Không tìm thấy máy kéo hợp lệ (HĐ ở cột D).");
            var duplicates=machines.Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
                .GroupBy(x=>Rules.Code(x.Serial),StringComparer.Ordinal)
                .Where(x=>x.Count()>1).Take(5).ToList();
            if(duplicates.Count>0)
                Caution("Có serial trùng trong nguồn (ví dụ "+
                    string.Join(", ",duplicates.Select(x=>x.First().Serial))+
                    "). HISA phải kiểm tra lại trước khi ghi ngày.");
            foreach(var machine in machines)
            {
                if(string.IsNullOrWhiteSpace(machine.MotorColumnK) ||
                    machine.MotorColumnK is "Đã làm" or "Chưa làm")
                {
                    working++;
                    if(machine.MotorColumnK.Length>0)
                        Caution("Cột K dòng "+machine.SourceRow+
                            " đang chứa trạng thái chữ cũ; chưa được coi là ngày bàn giao.");
                }
                else if(DateTime.TryParseExact(machine.MotorColumnK,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                    handed++;
                else
                {
                    working++;
                    Caution("Cột K dòng "+machine.SourceRow+
                        " chưa phải ngày hợp lệ; không tự coi là đã bàn giao.");
                }
            }
            if(new FileInfo(path) is { } current &&
               (current.Length!=originalLength||current.LastWriteTimeUtc!=originalStamp))
                Problem("File đã thay đổi trong khi đang kiểm tra. Đóng Excel và kiểm tra lại.");
        }
        catch(Exception error) when(error is IOException or
            InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or
            FormatException or System.Xml.XmlException)
        {
            Problem("Không kiểm tra được nội dung Excel: "+error.Message);
        }
        return new Report(rows,handed,working,findings);
    }
}
