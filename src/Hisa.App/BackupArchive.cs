using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// Backup V2 keeps the original ZIP entry names from older releases, adds
/// ai_schedules and publishes an integrity manifest. Never restores files
/// without an explicit, separately validated restore operation.
/// </summary>
public static class BackupArchive
{
    public sealed record FileDigest(string Name,long Length,string Sha256);
    public sealed record Manifest(int FormatVersion,string CreatedAt,List<FileDigest> Files);
    private static readonly JsonSerializerOptions Json=new() {WriteIndented=true};

    private static string Hash(string path)
    {
        using var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void Create(string destination,string dataDirectory,AppSettings settings)
    {
        if(File.Exists(destination))throw new IOException("Đường dẫn tệp ZIP tạm đã tồn tại.");
        var inputs=new List<(string Source,string Name)>();
        foreach(var file in Directory.EnumerateFiles(dataDirectory,"*.json",SearchOption.TopDirectoryOnly))
            inputs.Add((file,Path.GetFileName(file)));
        var scheduleFolder=Path.Combine(dataDirectory,"ai_schedules");
        if(Directory.Exists(scheduleFolder))
            foreach(var file in Directory.EnumerateFiles(scheduleFolder,"lich-kho-*.json",SearchOption.TopDirectoryOnly))
                inputs.Add((file,"ai_schedules/"+Path.GetFileName(file)));
        inputs.Add((settings.MasterPath,"Excel/Tổng hợp.xlsx"));
        if(File.Exists(settings.ProductExportPath))
            inputs.Add((settings.ProductExportPath,"Excel/Thành phẩm xuất kho.xlsx"));
        if(File.Exists(settings.KtPath))
            inputs.Add((settings.KtPath,"Excel/KT tổng hợp.xlsx"));

        var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var digests=new List<FileDigest>();
        using var archive=ZipFile.Open(destination,ZipArchiveMode.Create);
        foreach(var (source,name) in inputs.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase))
        {
            if(!unique.Add(name))throw new InvalidDataException("Tên mục sao lưu bị trùng: "+name);
            var before=Hash(source);
            archive.CreateEntryFromFile(source,name,CompressionLevel.Optimal);
            var after=Hash(source);
            if(!before.Equals(after,StringComparison.Ordinal))
                throw new IOException("Nguồn đã thay đổi khi sao lưu: "+Path.GetFileName(source));
            digests.Add(new FileDigest(name,new FileInfo(source).Length,before));
        }
        var manifest=new Manifest(2,DateTimeOffset.Now.ToString("O"),digests);
        var entry=archive.CreateEntry("backup-manifest.json",CompressionLevel.Optimal);
        using var stream=new StreamWriter(entry.Open(),new UTF8Encoding(false));
        stream.Write(JsonSerializer.Serialize(manifest,Json));
    }

    public static Manifest Verify(string zipPath)
    {
        using var archive=ZipFile.OpenRead(zipPath);
        if(archive.Entries.Count>50000)throw new InvalidDataException("ZIP chứa quá nhiều tệp.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in archive.Entries)
        {
            var name=entry.FullName.Replace('\\','/');
            if(name.StartsWith("/",StringComparison.Ordinal) ||
               name.Split('/').Any(x=>x is ".." or "." or "") ||
               !names.Add(name))
                throw new InvalidDataException("ZIP chứa mục trùng hoặc đường dẫn không hợp lệ.");
        }
        var meta=archive.GetEntry("backup-manifest.json")??
            throw new InvalidDataException("Bản ZIP này chưa có manifest kiểm tra (định dạng V1).");
        if(meta.Length>2_000_000)throw new InvalidDataException("Manifest sao lưu vượt giới hạn.");
        Manifest manifest;
        using(var reader=new StreamReader(meta.Open(),Encoding.UTF8))
            manifest=JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd(),Json)
                ??throw new InvalidDataException("Manifest sao lưu rỗng.");
        if(manifest.FormatVersion!=2||manifest.Files==null||manifest.Files.Count!=archive.Entries.Count-1)
            throw new InvalidDataException("Danh sách tệp trong ZIP không khớp với manifest.");
        var mentioned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files)
        {
            if(file.Name.Length==0||!mentioned.Add(file.Name)||
               file.Sha256.Length!=64||file.Length<0)
                throw new InvalidDataException("Manifest thiếu hoặc trùng tên/chuỗi kiểm tra.");
            var entry=archive.GetEntry(file.Name)??throw new InvalidDataException(
                "Thiếu mục trong bản sao: "+file.Name);
            if(entry.Length!=file.Length)throw new InvalidDataException("Sai kích thước mục "+file.Name);
            using var stream=entry.Open();
            var hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if(!hash.Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checksum của mục "+file.Name+" không khớp.");
        }
        if(!mentioned.Contains("Excel/Tổng hợp.xlsx"))
            throw new InvalidDataException("Bản sao thiếu Tổng hợp.xlsx bắt buộc.");
        return manifest;
    }
}
