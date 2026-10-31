using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hisa.App;

/// <summary>
/// Downloads and verifies the official Inno Setup installer in the background.
/// Installation runs on normal application exit to avoid interrupting Excel writes.
/// Installing into Program Files still requires the Windows UAC elevation prompt.
/// </summary>
public static class AutoUpdater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/infiniti-money/Xuat-hang/releases/latest";
    private static readonly HttpClient Http = CreateClient();
    private static string? _readyInstaller;
    private static string? _readyHash;
    private static bool _installRequested;
    public static event Action<string>? UpdateStaged;
    public static event Action<string>? UpdateProgress;
    public static string? LastFailure { get; private set; }
    public static string InstallLogDirectory => Path.Combine(Path.GetTempPath(),"HISA-Verified-Updates");
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim CheckGate = new(1,1);

    public static string? ReadyVersion { get; private set; }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient {Timeout=TimeSpan.FromMinutes(5)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HISA-Kho-Vat-Tu/"+SoftwareInfo.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal static Uri TrustedAssetUri(string value)
    {
        if(!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme!=Uri.UriSchemeHttps
            || uri.Host!="github.com"
            || !uri.AbsolutePath.StartsWith("/infiniti-money/Xuat-hang/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Đường dẫn cập nhật không thuộc GitHub Releases chính thức.");
        return uri;
    }

    private sealed record Release(string Version, string InstallerName, Uri InstallerUrl,
        Uri ChecksumUrl, string? GithubAssetDigest);

    private static async Task<Release?> CheckLatestAsync(CancellationToken cancellationToken)
    {
        using var response=await Http.GetAsync(LatestReleaseApi,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        await using var body=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc=await JsonDocument.ParseAsync(body,cancellationToken:cancellationToken);
        var root=doc.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
        var tag=root.GetProperty("tag_name").GetString()??"";
        if(!tag.StartsWith("v",StringComparison.OrdinalIgnoreCase)
            || !Version.TryParse(tag[1..],out var available)
            || !Version.TryParse(SoftwareInfo.Version,out var current)
            || available<=current)return null;

        var name="HISA-Kho-Vat-Tu-"+available.ToString(3)+"-Setup.exe";
        Uri? setup=null,checksum=null;
        string? assetDigest=null;
        foreach(var asset in root.GetProperty("assets").EnumerateArray())
        {
            var filename=asset.GetProperty("name").GetString()??"";
            var url=asset.GetProperty("browser_download_url").GetString()??"";
            if(filename.Equals(name,StringComparison.Ordinal))
            {
                setup=TrustedAssetUri(url);
                if(asset.TryGetProperty("digest",out var digest)&&digest.ValueKind==JsonValueKind.String)
                    assetDigest=digest.GetString();
            }
            else if(filename.Equals(name+".sha256",StringComparison.Ordinal))
                checksum=TrustedAssetUri(url);
        }
        if(setup==null||checksum==null)return null;
        return new Release(available.ToString(3),name,setup,checksum,assetDigest);
    }

    private static async Task<string> DownloadTextAsync(Uri uri,CancellationToken cancellationToken)
    {
        using var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is > 2048)
            throw new InvalidDataException("Tệp SHA-256 không hợp lệ.");
        var value=await response.Content.ReadAsStringAsync(cancellationToken);
        if(value.Length>2048)throw new InvalidDataException("Tệp SHA-256 quá dài.");
        return value;
    }

    // GitHub Release checksum must name the exact installer; do not accept a ZIP or another version.
    internal static string ParseChecksum(string content,string installerName,string? githubDigest)
    {
        var lines=content.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        if(lines.Length!=1)
            throw new InvalidDataException("Tệp SHA-256 phải có đúng một dòng checksum.");
        var line=lines[0].Trim();
        var match=Regex.Match(line,@"^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<name>[^\\/]+)$");
        if(!match.Success||!string.Equals(match.Groups["name"].Value,installerName,StringComparison.Ordinal))
            throw new InvalidDataException("Tệp checksum cập nhật không khớp với bộ cài.");
        var expected=match.Groups["hash"].Value;
        if(githubDigest is string digest && digest.Length>0 &&
            !string.Equals(digest,"sha256:"+expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 trên GitHub và tệp checksum không khớp.");
        return expected;
    }

    // A persistent .partial file supports a later retry. GitHub may ignore Range:
    // in that case restart from byte zero; never append a full response to partial data.
    internal static bool CanAppend(HttpStatusCode status,long offset,long? rangeStart) =>
        offset>0 && status==HttpStatusCode.PartialContent && rangeStart==offset;

    private static async Task DownloadInstallerAsync(Uri uri,string destination,CancellationToken cancellationToken)
    {
        const long maximum=512L*1024*1024;
        long offset=File.Exists(destination)?new FileInfo(destination).Length:0;
        if(offset>maximum) {File.Delete(destination);offset=0;}
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);
        if(offset>0)request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(offset,null);
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(offset>0 && response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(destination);
            await DownloadInstallerAsync(uri,destination,cancellationToken);
            return;
        }
        response.EnsureSuccessStatusCode();
        bool append=CanAppend(response.StatusCode,offset,response.Content.Headers.ContentRange?.From);
        if(response.StatusCode==HttpStatusCode.PartialContent && !append)
            throw new InvalidDataException("Máy chủ trả về khoảng tải không đúng; từ chối ghép tệp.");
        if(!append)offset=0;
        var length=response.Content.Headers.ContentLength;
        if(length is long l && (l>maximum-offset))
            throw new InvalidDataException("Bản cập nhật vượt dung lượng cho phép.");
        long expectedTotal=response.Content.Headers.ContentRange?.Length ?? (length.HasValue?offset+length.Value:0);
        await using var input=await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output=new FileStream(destination,append?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,81920,true);
        var buffer=new byte[81920];
        long total=offset;
        int previousPercent=-1;
        int read;
        while((read=await input.ReadAsync(buffer,cancellationToken))>0)
        {
            total+=read;
            if(total>maximum)throw new InvalidDataException("Bản cập nhật vượt dung lượng cho phép.");
            await output.WriteAsync(buffer.AsMemory(0,read),cancellationToken);
            int percent=expectedTotal>0?(int)Math.Min(100,total*100/expectedTotal):-1;
            if(percent>=0 && percent/5!=previousPercent/5)
            {
                previousPercent=percent;
                UpdateProgress?.Invoke("Đang tải cập nhật: "+percent+"% ("+
                    (total/1048576)+" MB / "+(expectedTotal/1048576)+" MB)");
            }
        }
        if(total==0 || (expectedTotal>0 && total!=expectedTotal))
            throw new IOException("Tải chưa đủ dữ liệu; HISA đã giữ phần tải dở để thử lại.");
    }

    // Shared by automatic and manual checks; returns the verified downloaded version.
    public static async Task<string?> CheckAndStageAsync(CancellationToken cancellationToken=default)
    {
        await CheckGate.WaitAsync(cancellationToken);
        try
        {
            LastFailure=null;
            return await StageCoreAsync(cancellationToken);
        }
        catch(Exception ex) when (ex is not OperationCanceledException)
        {
            LastFailure=ex.Message;
            UpdateProgress?.Invoke("Cập nhật chưa thành công: "+ex.Message+" · Có thể chọn Thử lại.");
            throw;
        }
        finally { CheckGate.Release(); }
    }

    private static async Task<string?> StageCoreAsync(CancellationToken cancellationToken)
    {
        var release=await CheckLatestAsync(cancellationToken);
        if(release==null)return null;
        var expectedContent=await DownloadTextAsync(release.ChecksumUrl,cancellationToken);
        var expected=ParseChecksum(expectedContent,release.InstallerName,release.GithubAssetDigest);

        // Only installer cache is temporary; all warehouse state and backups remain in Documents.
        var root=Path.Combine(Path.GetTempPath(),"HISA-Verified-Updates");
        Directory.CreateDirectory(root);
        var file=Path.Combine(root,release.InstallerName);
        var staging=file+".partial";
        if(File.Exists(file))
        {
            await using var existing=File.OpenRead(file);
            var cached=Convert.ToHexString(await SHA256.HashDataAsync(existing,cancellationToken));
            if(cached.Equals(expected,StringComparison.OrdinalIgnoreCase))
            {
                lock(Sync){_readyInstaller=file;_readyHash=expected;ReadyVersion=release.Version;}
                UpdateStaged?.Invoke(release.Version);
                UpdateProgress?.Invoke("Bản "+release.Version+" đã tải và xác minh. Sẵn sàng cài.");
                return release.Version;
            }
            File.Delete(file);
        }
        UpdateProgress?.Invoke("Đang chuẩn bị tải bản "+release.Version+"...");
        await DownloadInstallerAsync(release.InstallerUrl,staging,cancellationToken);
        UpdateProgress?.Invoke("Đang xác minh SHA-256 của bộ cài...");
        string actual;
        await using(var stream=File.OpenRead(staging))
            actual=Convert.ToHexString(await SHA256.HashDataAsync(stream,cancellationToken));
        if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(staging);
            throw new InvalidDataException("Sai SHA-256; đã xóa bản tải lỗi, không cho cài.");
        }
        File.Move(staging,file,true);
        lock(Sync){_readyInstaller=file;_readyHash=expected;ReadyVersion=release.Version;}
        UpdateStaged?.Invoke(release.Version);
        UpdateProgress?.Invoke("Đã tải và xác minh bản "+release.Version+".");
        return release.Version;
    }

    /// <summary>Only a user-approved update may run at exit. A background download never forces installation.</summary>
    public static bool RequestInstallOnExit()
    {
        lock(Sync)
        {
            if(_readyInstaller==null||_readyHash==null||!File.Exists(_readyInstaller))return false;
            _installRequested=true;
            return true;
        }
    }

    /// <summary>Starts the verified installer after the warehouse process releases its files.</summary>
    public static void InstallWhenExiting()
    {
        string? setup,expected;
        lock(Sync)
        {
            if(!_installRequested)return;
            setup=_readyInstaller;expected=_readyHash;
            _installRequested=false;
        }
        if(setup==null||expected==null)return;
        try
        {
            // Re-check the executable when launching: the cache may have changed after download.
            using(var stream=File.OpenRead(setup))
            {
                var actual=Convert.ToHexString(SHA256.HashData(stream));
                if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Bộ cài đã thay đổi sau khi tải. Vui lòng kiểm tra cập nhật lại.");
            }
            // /SILENT displays progress and errors instead of failing invisibly.
            // /HISAUPGRADE=1 asks Inno Setup to relaunch HISA only after installation finishes.
            // Windows UAC is never bypassed.
            Directory.CreateDirectory(InstallLogDirectory);
            var log=Path.Combine(InstallLogDirectory,"installer-"+(ReadyVersion??"update")+".log");
            Process.Start(new ProcessStartInfo
            {
                FileName=setup,
                Arguments="/SILENT /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /HISAUPGRADE=1 /LOG=\""+log+"\"",
                UseShellExecute=true,
                Verb="runas",
                WorkingDirectory=Path.GetDirectoryName(setup)!
            });
        }
        catch(Exception ex)
        {
            System.Windows.MessageBox.Show(
                "Không thể mở bộ cài cập nhật: "+ex.Message+
                "\nBản hiện tại và dữ liệu kho vẫn được giữ nguyên. Có thể tải bộ cài trên GitHub Releases.",
                "HISA · Cập nhật chưa thành công",System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }
}
