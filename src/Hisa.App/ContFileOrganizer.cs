using System.IO;
using System.Security.Cryptography;

namespace Hisa.App;

/// <summary>
/// Safely separates sent goods (Hàng gửi) and successfully imported Hưng Yên workbooks.
/// All operations are confined to the selected CONT ĐI source folder and preserve
/// numbered subfolders. Archived workbooks remain readable for future rebuilds.
/// </summary>
public static class ContFileOrganizer
{
    public sealed record Candidate(string Source, string Root, long Length, long LastWriteTicks);
    public static bool HasHangGuiName(string path) =>
        Rules.Code(Path.GetFileNameWithoutExtension(path)).Contains("HANGGUI",StringComparison.Ordinal);

    private static bool Under(string path,string directory)
    {
        var relative=Path.GetRelativePath(directory,path);
        return relative!="." && relative!=".." && !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static bool IsArchiveSegment(string segment) =>
        Rules.Code(segment) is "DANHAPKHO" or "HANGGUI";

    public static bool IsAlreadySorted(string path,string root)
    {
        if(!Under(path,root))return true;
        var relative=Path.GetRelativePath(root,path);
        return relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(IsArchiveSegment);
    }

    public static bool IsHangGuiFolder(string path) =>
        Path.GetDirectoryName(path)?.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
            .Any(x=>Rules.Code(x)=="HANGGUI") == true;

    public static Candidate? MakeCandidate(string file,IEnumerable<string> roots)
    {
        var path=Path.GetFullPath(file);
        var root=roots.Where(Directory.Exists).Select(Path.GetFullPath)
            .OrderByDescending(x=>x.Length)
            .FirstOrDefault(x=>Under(path,x)&&!IsAlreadySorted(path,x));
        if(root==null)return null;
        var info=new FileInfo(path);
        return info.Exists?new Candidate(path,root,info.Length,info.LastWriteTimeUtc.Ticks):null;
    }

    // Keep IDs for source packages stable even after their file is moved under
    // the archive folder, preventing history references from changing on re-scan.
    public static string OriginalPath(string path)
    {
        var parts=Path.GetFullPath(path).Split(Path.DirectorySeparatorChar);
        var index=Array.FindIndex(parts,x=>Rules.Code(x)=="DANHAPKHO");
        if(index<0)return path;
        return string.Join(Path.DirectorySeparatorChar,parts.Where((_,i)=>i!=index));
    }

    public static List<string> MoveFiles(IEnumerable<Candidate> candidates,string subfolder)
    {
        if(subfolder is not ("Hàng gửi" or "Đã nhập kho"))
            throw new ArgumentException("Thư mục phân loại không hợp lệ.");
        var warnings=new List<string>();
        foreach(var candidate in candidates.DistinctBy(x=>x.Source,StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if(IsAlreadySorted(candidate.Source,candidate.Root))continue;
                var source=new FileInfo(candidate.Source);
                if(!source.Exists||source.Length!=candidate.Length||source.LastWriteTimeUtc.Ticks!=candidate.LastWriteTicks)
                {
                    warnings.Add("File đang thay đổi, chưa chuyển: "+Path.GetFileName(candidate.Source));
                    continue;
                }
                var relative=Path.GetRelativePath(candidate.Root,candidate.Source);
                var folder=Path.Combine(candidate.Root,subfolder,Path.GetDirectoryName(relative)??"");
                Directory.CreateDirectory(folder);
                var target=Path.Combine(folder,Path.GetFileName(relative));
                if(File.Exists(target))
                {
                    // Never overwrite archive history. A unique name also prevents data loss.
                    target=Path.Combine(folder,Path.GetFileNameWithoutExtension(relative)+"-"+
                        DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6]+Path.GetExtension(relative));
                }
                File.Move(candidate.Source,target);
            }
            catch(Exception ex) { warnings.Add("Chưa phân loại "+Path.GetFileName(candidate.Source)+": "+ex.Message); }
        }
        return warnings;
    }
}
