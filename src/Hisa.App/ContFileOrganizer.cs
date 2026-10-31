using System.IO;

namespace Hisa.App;

/// <summary>
/// Keeps pending CONT ĐI files separate from sibling "Đã nhập kho" and "Hàng gửi".
/// Source files are archived as received only when EVERY physical source package in
/// that workbook has a persisted OriginPackageId in Tổng hợp.xlsx.
/// </summary>
public static class ContFileOrganizer
{
    public sealed record Candidate(string Source,string Root,long Length,long LastWriteTicks);

    public static bool HasHangGuiName(string path) =>
        Rules.Code(Path.GetFileNameWithoutExtension(path)).Contains("HANGGUI",StringComparison.Ordinal);

    private static bool Under(string path,string directory)
    {
        var relative=Path.GetRelativePath(directory,path);
        return relative!="." && relative!=".." &&
            !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static bool IsArchiveSegment(string segment) =>
        Rules.Code(segment) is "DANHAPKHO" or "HANGGUI";

    /// <summary>Sibling directory, never a child of the folder selected as CONT ĐI.</summary>
    public static string SiblingFolder(string root,string category)
    {
        if(category is not ("Đã nhập kho" or "Hàng gửi"))
            throw new ArgumentException("Danh mục phân loại không hợp lệ.",nameof(category));
        var parent=Directory.GetParent(Path.GetFullPath(root))?.FullName ??
            throw new InvalidOperationException("Nguồn cont phải có thư mục cha để phân loại.");
        return Path.Combine(parent,category);
    }

    public static bool IsAlreadySorted(string path,string root)
    {
        if(!Under(path,root))return true;
        var relative=Path.GetRelativePath(root,path);
        return relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(IsArchiveSegment);
    }

    public static bool IsHangGuiFolder(string path) =>
        Path.GetDirectoryName(path)?.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
            .Any(x=>Rules.Code(x)=="HANGGUI")==true;

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

    /// <summary>
    /// Map an archived workbook to the ORIGINAL source path. This is essential:
    /// the source package ID persisted in Excel must not change when a file moves.
    /// </summary>
    public static string OriginalPath(string path,IEnumerable<string>? roots=null)
    {
        path=Path.GetFullPath(path);
        foreach(var source in roots??[])
        {
            var root=Path.GetFullPath(source);
            var oldArchive=Path.Combine(root,"Đã nhập kho");
            if(Under(path,oldArchive))
                return Path.Combine(root,Path.GetRelativePath(oldArchive,path));
            var sibling=SiblingFolder(root,"Đã nhập kho");
            if(Under(path,sibling))
                return Path.Combine(root,Path.GetRelativePath(sibling,path));
        }
        // Historical callers without a source-root hint still support the old
        // CONT ĐI/Đã nhập kho/... layout.
        var parts=path.Split(Path.DirectorySeparatorChar);
        var index=Array.FindIndex(parts,x=>Rules.Code(x)=="DANHAPKHO");
        if(index<0)return path;
        return string.Join(Path.DirectorySeparatorChar,parts.Where((_,i)=>i!=index));
    }

    // Master "Cont đi Hưng Yên" generates its own IDs. The source workbook IDs
    // are path-based, so a direct equality check between them is NOT valid.
    // Match through unique physical package signatures, but be conservative:
    // two identical packages in separate source files require two confirmed
    // master receipts before either file can be considered fully received.
    private static string PhysicalSignature(Package package) =>
        package.Identity+"|"+package.SourceDate+"|"+
        PlannedArrivalWorkflow.MaterialFingerprint(package);

    private static HashSet<string> FullyReceivedFiles(IEnumerable<Package> scan,
        WorkbookSnapshot master)
    {
        var sources=scan.Where(p=>p.SourcePath.Length>0&&p.Id.Length>0).ToList();
        var sourceCounts=sources.GroupBy(PhysicalSignature)
            .ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
        var receivedIds=master.DetailPackages.Where(p=>p.OriginPackageId.Length>0)
            .Select(p=>p.OriginPackageId).ToHashSet(StringComparer.Ordinal);
        var receivedCounts=master.ContPackages.Where(p=>receivedIds.Contains(p.Id))
            .DistinctBy(p=>p.Id).GroupBy(PhysicalSignature)
            .ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
        return sources.GroupBy(p=>Path.GetFullPath(p.SourcePath),StringComparer.OrdinalIgnoreCase)
            .Where(group=>group.All(p=>
                receivedCounts.GetValueOrDefault(PhysicalSignature(p)) >=
                sourceCounts[PhysicalSignature(p)]))
            .Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Only move workbooks with all physical packages recorded in master detail.</summary>
    public static List<Candidate> ConfirmedCandidates(
        IEnumerable<Candidate> candidates,IEnumerable<Package> sourcePackages,
        WorkbookSnapshot master)
    {
        var confirmed=FullyReceivedFiles(sourcePackages,master);
        return candidates.Where(c=>confirmed.Contains(Path.GetFullPath(c.Source)))
            .DistinctBy(c=>c.Source,StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Move candidate files to sibling Hàng gửi / Đã nhập kho, retaining dates and
    /// refusing to overwrite historical files or move a file that changed meanwhile.
    /// </summary>
    public static List<string> MoveFiles(IEnumerable<Candidate> candidates,string category)
    {
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
                var folder=Path.Combine(SiblingFolder(candidate.Root,category),
                    Path.GetDirectoryName(relative)??"");
                Directory.CreateDirectory(folder);
                var target=Path.Combine(folder,Path.GetFileName(relative));
                if(File.Exists(target))
                {
                    // Never overwrite history. A collision on a source file must be
                    // resolved explicitly to preserve the origin package identity.
                    warnings.Add("Tên file trùng trong "+category+", chưa chuyển: "+target);
                    continue;
                }
                File.Move(candidate.Source,target);
            }
            catch(Exception ex)
            {
                warnings.Add("Chưa phân loại "+Path.GetFileName(candidate.Source)+": "+ex.Message);
            }
        }
        return warnings;
    }

    /// <summary>
    /// Correct the old layout created before v1.9.4. Previous versions mistakenly
    /// labeled every scanned workbook "Đã nhập kho", even before physical receipt.
    /// Unconfirmed old workbooks go BACK to CONT ĐI; only confirmed ones may move
    /// into the sibling Đã nhập kho folder. Old Hàng gửi files go to sibling Hàng gửi.
    /// </summary>
    public static List<string> RelocateLegacyFolders(IEnumerable<string> roots,
        IEnumerable<Package> scannedPackages,WorkbookSnapshot master)
    {
        var warnings=new List<string>();
        var confirmed=FullyReceivedFiles(scannedPackages,master);
        foreach(var root in roots.Where(Directory.Exists).Select(Path.GetFullPath))
        foreach(var category in new[]{"Hàng gửi","Đã nhập kho"})
        {
            var legacy=Path.Combine(root,category);
            if(!Directory.Exists(legacy))continue;
            foreach(var file in Directory.EnumerateFiles(legacy,"*",SearchOption.AllDirectories).ToArray())
            {
                try
                {
                    var relative=Path.GetRelativePath(legacy,file);
                    bool wasReceived=confirmed.Contains(Path.GetFullPath(file));
                    var destination=category=="Hàng gửi" || wasReceived
                        ? Path.Combine(SiblingFolder(root,category),relative)
                        : Path.Combine(root,relative);
                    if(File.Exists(destination))
                    {
                        warnings.Add("Cần xử lý file trùng tên, giữ nguyên nguồn cũ: "+file);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(file,destination);
                }
                catch(Exception ex)
                {
                    warnings.Add("Không thể di chuyển file cũ "+Path.GetFileName(file)+": "+ex.Message);
                }
            }
            // Remove ONLY emptied legacy subdirectories. Never delete source files.
            foreach(var dir in Directory.EnumerateDirectories(legacy,"*",SearchOption.AllDirectories)
                .OrderByDescending(x=>x.Length))
            {
                if(!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
            }
            if(!Directory.EnumerateFileSystemEntries(legacy).Any())Directory.Delete(legacy);
        }
        return warnings;
    }
}
