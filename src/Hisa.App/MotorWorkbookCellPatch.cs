using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Non-destructive OOXML writer for the original MÁY KÉO.xlsx.  ClosedXML is
/// used for read-only validation, but MUST NOT resave an operator workbook:
/// Excel extensions/drawings/validations can be lost in a full round-trip.
/// Only the XML entry for the workbook's first sheet is changed; every other
/// ZIP part is copied without parsing or rewriting it.
/// </summary>
public static class MotorWorkbookCellPatch
{
    public sealed record Edit(int Row,int Column,string? Value,bool Date=false);
    private static readonly XNamespace Spreadsheet=
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DocumentRel=
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel=
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static XDocument LoadXml(ZipArchive archive,string path)
    {
        var entry=archive.GetEntry(path)??throw new InvalidDataException(
            "Excel thiếu thành phần "+path+". Không ghi file.");
        using var stream=entry.Open();
        return XDocument.Load(stream,LoadOptions.PreserveWhitespace);
    }

    private static string FirstWorksheetPath(ZipArchive archive)
    {
        var workbook=LoadXml(archive,"xl/workbook.xml");
        var first=workbook.Descendants(Spreadsheet+"sheet").FirstOrDefault()??
            throw new InvalidDataException("Excel không có sheet nguồn.");
        var id=(string?)first.Attribute(DocumentRel+"id")??
            throw new InvalidDataException("Sheet nguồn thiếu mã quan hệ.");
        var relations=LoadXml(archive,"xl/_rels/workbook.xml.rels");
        var relationship=relations.Descendants(PackageRel+"Relationship")
            .SingleOrDefault(x=>(string?)x.Attribute("Id")==id)??
            throw new InvalidDataException("Không xác định được sheet đầu của Excel.");
        if(string.Equals((string?)relationship.Attribute("TargetMode"),"External",
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Không được ghi vào sheet liên kết ngoài.");
        var target=(string?)relationship.Attribute("Target")??"";
        if(target.Length==0)throw new InvalidDataException("Đường dẫn sheet trống.");
        // URI path normalization handles both /xl/... and worksheets/sheetN.xml.
        var resolved=new Uri(new Uri("https://hisa.invalid/xl/workbook.xml"),
            target.Replace('\\','/'));
        if(resolved.Host!="hisa.invalid" ||
            !resolved.AbsolutePath.StartsWith("/xl/worksheets/",StringComparison.Ordinal))
            throw new InvalidDataException("Quan hệ sheet không hợp lệ.");
        var path=Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
        if(archive.GetEntry(path)==null)
            throw new InvalidDataException("Excel thiếu "+path+".");
        return path;
    }

    private static int ColumnNumber(string reference)
    {
        int column=0;
        foreach(char c in reference)
        {
            if(c>='A'&&c<='Z')column=checked(column*26+c-'A'+1);
            else if(c>='a'&&c<='z')column=checked(column*26+c-'a'+1);
            else break;
        }
        return column;
    }

    private static string CellReference(int row,int column)
    {
        var letters="";
        while(column>0)
        {
            column--;
            letters=(char)('A'+column%26)+letters;
            column/=26;
        }
        return letters+row.ToString(CultureInfo.InvariantCulture);
    }

    private static XDocument PatchSheet(XDocument document,IReadOnlyList<Edit> edits)
    {
        var root=document.Root??throw new InvalidDataException("Sheet XML rỗng.");
        var data=root.Element(Spreadsheet+"sheetData")??
            throw new InvalidDataException("Sheet không có dữ liệu.");
        var merged=root.Element(Spreadsheet+"mergeCells");
        foreach(var edit in edits)
        {
            if(edit.Row<2 || edit.Column is not (9 or 11) ||
               (edit.Row==2 && edit.Column!=11))
                throw new InvalidDataException("Chỉ được ghi serial cột I hoặc ngày bàn giao cột K.");
            if(edit.Date && (edit.Value==null ||
                !DateTime.TryParseExact(edit.Value,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _)))
                throw new InvalidDataException("Ngày bàn giao cần đúng yyyy-MM-dd.");
            var reference=CellReference(edit.Row,edit.Column);
            // Merged targets are unsafe: a value in a non-anchor merged cell may
            // trigger an Excel repair or silently appear on a different row.
            if(merged!=null && merged.Elements(Spreadsheet+"mergeCell").Any(x=>
            {
                var range=((string?)x.Attribute("ref")??"").Split(':');
                if(range.Length==0)return false;
                var a=range[0];
                var b=range.Length>1?range[1]:a;
                int firstCol=ColumnNumber(a),lastCol=ColumnNumber(b);
                static int RowNumber(string cellRef)=>
                    int.TryParse(new string(cellRef.SkipWhile(char.IsLetter).ToArray()),
                        out var number)?number:0;
                return edit.Column>=Math.Min(firstCol,lastCol) &&
                    edit.Column<=Math.Max(firstCol,lastCol) &&
                    edit.Row>=Math.Min(RowNumber(a),RowNumber(b)) &&
                    edit.Row<=Math.Max(RowNumber(a),RowNumber(b));
            }))
                throw new InvalidDataException("Ô "+reference+
                    " đang nằm trong vùng gộp ô, chưa ghi để tránh làm lỗi mẫu Excel.");
            var row=data.Elements(Spreadsheet+"row").SingleOrDefault(x=>
                (int?)x.Attribute("r")==edit.Row)??
                throw new InvalidDataException("Không tìm thấy dòng nguồn "+edit.Row);
            var cell=row.Elements(Spreadsheet+"c").SingleOrDefault(x=>
                string.Equals((string?)x.Attribute("r"),reference,
                    StringComparison.OrdinalIgnoreCase));
            if(cell==null)
            {
                cell=new XElement(Spreadsheet+"c",new XAttribute("r",reference));
                var next=row.Elements(Spreadsheet+"c").FirstOrDefault(x=>
                    ColumnNumber((string?)x.Attribute("r")??"")>edit.Column);
                if(next==null)row.Add(cell);
                else next.AddBeforeSelf(cell);
            }
            if(cell.Element(Spreadsheet+"f")!=null)
                throw new InvalidDataException("Cột "+reference+
                    " chứa công thức; không được ghi đè.");
            cell.Elements(Spreadsheet+"v").Remove();
            cell.Elements(Spreadsheet+"is").Remove();
            cell.Attribute("t")?.Remove();
            if(edit.Value==null)
                continue; // Clear contents but keep original cell styling.
            if(edit.Date)
            {
                // t=d is an ISO-8601 OpenXML date cell.  Unlike numeric dates
                // it does not need a newly injected style/numFmt in styles.xml.
                cell.SetAttributeValue("t","d");
                cell.Add(new XElement(Spreadsheet+"v",
                    edit.Value+"T00:00:00"));
            }
            else
            {
                // Inline string retains leading zeros and needs no shared-string
                // table changes. In particular serials are always stored as text.
                cell.SetAttributeValue("t","inlineStr");
                cell.Add(new XElement(Spreadsheet+"is",
                    new XElement(Spreadsheet+"t",edit.Value)));
            }
        }
        return document;
    }

    private static string HashFile(string path)
    {
        using var source=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(source));
    }

    public static void Apply(string excelPath,string backupFolder,
        IReadOnlyList<Edit> edits,string backupPrefix)
    {
        if(edits.Count==0)return;
        if(edits.Select(x=>(x.Row,x.Column)).Distinct().Count()!=edits.Count)
            throw new InvalidDataException("Trùng ô Excel trong kế hoạch ghi.");
        var full=Path.GetFullPath(excelPath);
        var folder=Path.GetDirectoryName(full)!;
        var tmp=Path.Combine(folder,"."+Path.GetFileNameWithoutExtension(full)+
            "-patch-"+Guid.NewGuid().ToString("N")+".tmp.xlsx");
        string fingerprint;
        try
        {
            using(var locked=new FileStream(full,FileMode.Open,FileAccess.ReadWrite,
                FileShare.None))
            {
                fingerprint=Convert.ToHexString(SHA256.HashData(locked));
                locked.Position=0;
                using var archive=new ZipArchive(locked,ZipArchiveMode.Read,leaveOpen:true);
                if(archive.Entries.Any(x=>x.FullName.StartsWith("_xmlsignatures/",
                    StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(
                        "Excel có chữ ký số; HISA không thay đổi workbook đã ký.");
                var sheetPath=FirstWorksheetPath(archive);
                var sheet=PatchSheet(LoadXml(archive,sheetPath),edits);
                using(var output=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,
                    FileShare.None))
                using(var destination=new ZipArchive(output,ZipArchiveMode.Create))
                {
                    foreach(var entry in archive.Entries)
                    {
                        var copy=destination.CreateEntry(entry.FullName,
                            CompressionLevel.Optimal);
                        copy.LastWriteTime=entry.LastWriteTime;
                        copy.ExternalAttributes=entry.ExternalAttributes;
                        using var writer=copy.Open();
                        if(entry.FullName==sheetPath)
                        {
                            using var stream=new MemoryStream();
                            sheet.Save(stream,SaveOptions.DisableFormatting);
                            stream.Position=0;
                            stream.CopyTo(writer);
                        }
                        else
                        {
                            using var source=entry.Open();
                            source.CopyTo(writer);
                        }
                    }
                }
                // The patch must remain a valid workbook before any source is
                // replaced. No writes to the real workbook happen at this point.
                using var verify=new XLWorkbook(tmp);
                if(verify.Worksheets.Count==0)
                    throw new InvalidDataException("Bản Excel thử ghi không có sheet.");
                Directory.CreateDirectory(backupFolder);
                var backup=Path.Combine(backupFolder,backupPrefix+"-"+
                    DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+
                    Guid.NewGuid().ToString("N")[..8]+".xlsx");
                locked.Position=0;
                using var safe=new FileStream(backup,FileMode.CreateNew,
                    FileAccess.Write,FileShare.None);
                locked.CopyTo(safe);
                safe.Flush(true);
            }
            // Fail closed if any other writer changed the original while the
            // staging file was created. File.Move also fails for locked Excel.
            if(HashFile(full)!=fingerprint)
                throw new IOException("Excel vừa thay đổi; chưa ghi đè. Tải lại nguồn.");
            File.Move(tmp,full,true);
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or
            System.Xml.XmlException or InvalidOperationException)
        {
            throw new IOException("Không thể cập nhật MÁY KÉO.xlsx an toàn. "+
                "File nguồn được giữ nguyên và đã tạo bản sao trước khi thay thế. "+
                "Hãy đóng Excel và kiểm tra file: "+ex.Message,ex);
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
