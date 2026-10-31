using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Restores selected data from verified HISA ZIP archives. Always stages and validates
/// all selected entries first; keeps an on-disk safety copy and rolls back partial writes.
/// </summary>
public static class BackupRestore
{
    public sealed record Preview(bool Legacy, string CreatedAt, IReadOnlyList<string> Files)
    {
        public int DataCount => Files.Count(x=>x.EndsWith(".json",StringComparison.OrdinalIgnoreCase) && x!="backup-manifest.json");
        public int ExcelCount => Files.Count(x=>x.StartsWith("Excel/",StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] Workbooks =
        ["Excel/Tổng hợp.xlsx","Excel/Thành phẩm xuất kho.xlsx","Excel/KT tổng hợp.xlsx"];
    private const long MaxEntryBytes=512L*1024*1024;
    private const long MaxTotalBytes=1024L*1024*1024;

    private static bool Allowed(string path) =>
        (path.EndsWith(".json",StringComparison.OrdinalIgnoreCase)
            && path!="backup-manifest.json"
            && (path.IndexOf('/')<0 ||
                (path.StartsWith("ai_schedules/",StringComparison.OrdinalIgnoreCase) &&
                 path["ai_schedules/".Length..].StartsWith("lich-kho-",StringComparison.OrdinalIgnoreCase) &&
                 !path["ai_schedules/".Length..].Contains('/'))))
        || Workbooks.Contains(path,StringComparer.OrdinalIgnoreCase);

    private static string ValidateName(ZipArchiveEntry entry)
    {
        string path=entry.FullName;
        if(string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':')
            || path.StartsWith('/') || path.Split('/').Any(x=>x is "" or "." or "..")
            || (entry.ExternalAttributes >> 16 & 0xF000)==0xA000)
            throw new InvalidDataException("ZIP có đường dẫn hoặc liên kết không an toàn.");
        return path;
    }

    public static Preview Inspect(string zipPath)
    {
        using var archive=ZipFile.OpenRead(zipPath);
        if(archive.Entries.Count==0||archive.Entries.Count>50000)
            throw new InvalidDataException("ZIP rỗng hoặc quá nhiều tệp.");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total=0;
        foreach(var entry in archive.Entries)
        {
            string name=ValidateName(entry);
            if(!seen.Add(name) || (!Allowed(name) && name!="backup-manifest.json"))
                throw new InvalidDataException("ZIP chứa mục lạ hoặc trùng: "+name);
            if(entry.Length>MaxEntryBytes || entry.Length<0 ||
                total>MaxTotalBytes-entry.Length)
                throw new InvalidDataException("ZIP vượt dung lượng giải nén an toàn.");
            total+=entry.Length;
        }
        var legacy=!seen.Contains("backup-manifest.json");
        string created;
        if(!legacy)
        {
            var manifest=BackupArchive.Verify(zipPath);
            created=manifest.CreatedAt;
        }
        else
            created=File.GetLastWriteTime(zipPath).ToString("yyyy-MM-dd HH:mm");
        if(!seen.Contains("Excel/Tổng hợp.xlsx"))
            throw new InvalidDataException("Bản sao thiếu Excel/Tổng hợp.xlsx.");
        return new Preview(legacy,created,seen.Where(Allowed).OrderBy(x=>x).ToList());
    }

    private static string Destination(string entryName,string dataDirectory,AppSettings settings)
    {
        if(entryName.StartsWith("ai_schedules/",StringComparison.OrdinalIgnoreCase))
            return Path.Combine(dataDirectory,"ai_schedules",Path.GetFileName(entryName));
        if(!entryName.Contains('/'))
            return Path.Combine(dataDirectory,entryName);
        if(entryName.Equals("Excel/Tổng hợp.xlsx",StringComparison.OrdinalIgnoreCase))
            return settings.MasterPath;
        if(entryName.Equals("Excel/Thành phẩm xuất kho.xlsx",StringComparison.OrdinalIgnoreCase))
            return settings.ProductExportPath;
        if(entryName.Equals("Excel/KT tổng hợp.xlsx",StringComparison.OrdinalIgnoreCase))
            return settings.KtPath;
        throw new InvalidDataException("Đường dẫn ZIP không được phép phục hồi.");
    }

    public static string Restore(string zipPath,string dataDirectory,string backupDirectory,
        AppSettings settings,bool includeData,bool includeExcel,int failAfterWrites=-1)
    {
        if(!includeData&&!includeExcel)
            throw new ArgumentException("Phải chọn ít nhất một nhóm dữ liệu cần phục hồi.");
        var preview=Inspect(zipPath);
        var selected=preview.Files.Where(x=>
            x.StartsWith("Excel/",StringComparison.OrdinalIgnoreCase)?includeExcel:includeData).ToList();
        if(selected.Count==0)throw new InvalidDataException("Không có tệp nào thuộc nhóm đã chọn.");

        Directory.CreateDirectory(backupDirectory);
        var safety=Path.Combine(backupDirectory,"Restore-Safety-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(safety);
        var prepared=new List<(string Name,string Target,string Stage,string? Original)>();
        var changed=new List<(string Target,string? Original)>();
        try
        {
            // Ensure all source files are valid and all destinations are writable BEFORE changing anything.
            using(var archive=ZipFile.OpenRead(zipPath))
            {
                for(int i=0;i<selected.Count;i++)
                {
                    var name=selected[i];
                    var entry=archive.GetEntry(name) ?? throw new InvalidDataException("Tệp ZIP đã thay đổi: "+name);
                    var target=Path.GetFullPath(Destination(name,dataDirectory,settings));
                    if(Path.GetFullPath(zipPath).Equals(target,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Tệp ZIP trùng với đích phục hồi.");
                    var stage=Path.Combine(safety,"stage-"+i.ToString("D5"));
                    using(var input=entry.Open())
                    using(var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        input.CopyTo(output);
                    if(new FileInfo(stage).Length!=entry.Length)
                        throw new InvalidDataException("Giải nén không đủ dữ liệu: "+name);
                    if(name.EndsWith(".json",StringComparison.OrdinalIgnoreCase))
                    {
                        BackupSchemaValidator.Validate(name,stage);
                    }
                    else
                    {
                        using var workbookStream=File.OpenRead(stage);
                        using var workbook=new XLWorkbook(workbookStream);
                        if(workbook.Worksheets.Count==0)
                            throw new InvalidDataException("Workbook rỗng: "+name);
                    }
                    if(File.Exists(target))
                    {
                        using var locked=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                    }
                    prepared.Add((name,target,stage,null));
                }
            }

            // Capture originals as plain files, preserved even after a successful restore.
            for(int i=0;i<prepared.Count;i++)
            {
                var item=prepared[i];
                Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                string? original=null;
                if(File.Exists(item.Target))
                {
                    original=Path.Combine(safety,"original-"+i.ToString("D5"));
                    File.Copy(item.Target,original,false);
                }
                prepared[i]=(item.Name,item.Target,item.Stage,original);
            }
            File.WriteAllText(Path.Combine(safety,"restore-map.json"),
                JsonSerializer.Serialize(prepared.Select(x=>new {x.Name,x.Target,
                    Existed=x.Original!=null,Snapshot=x.Original})));
            // Use a same-directory temporary file so the final rename is atomic on the destination volume.
            foreach(var item in prepared)
            {
                var pending=item.Target+"."+Guid.NewGuid().ToString("N")+".restore";
                try
                {
                    File.Copy(item.Stage,pending,false);
                    File.Move(pending,item.Target,true);
                    changed.Add((item.Target,item.Original));
                    if(failAfterWrites>0 && changed.Count>=failAfterWrites)
                        throw new IOException("Mô phỏng lỗi phục hồi để kiểm thử rollback.");
                }
                finally{if(File.Exists(pending))File.Delete(pending);}
            }
            return safety;
        }
        catch(Exception error)
        {
            var failures=new List<string>();
            foreach(var item in changed.AsEnumerable().Reverse())
            {
                try
                {
                    if(item.Original==null)File.Delete(item.Target);
                    else File.Copy(item.Original,item.Target,true);
                }
                catch(Exception rollbackError){failures.Add(Path.GetFileName(item.Target)+": "+rollbackError.Message);}
            }
            if(failures.Count>0)
                throw new IOException("Phục hồi gặp lỗi và hoàn tác chưa đầy đủ. Giữ thư mục "+safety+
                    " để phục hồi thủ công: "+string.Join("; ",failures),error);
            throw new IOException("Không phục hồi dữ liệu. Mọi tệp đã ghi được hoàn tác. "+
                "Bản sao an toàn: "+safety+". Chi tiết: "+error.Message,error);
        }
        finally
        {
            // Keep original snapshots and mapping for manual recovery; remove staged copies.
            foreach(var item in prepared)
                if(File.Exists(item.Stage))File.Delete(item.Stage);
        }
    }
}
