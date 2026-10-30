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
    private static readonly object Sync = new();

    public static string? ReadyVersion { get; private set; }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient {Timeout=TimeSpan.FromMinutes(5)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HISA-Kho-Vat-Tu/"+SoftwareInfo.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Uri TrustedAssetUri(string value)
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

    private static async Task DownloadInstallerAsync(Uri uri,string destination,CancellationToken cancellationToken)
    {
        using var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        response.EnsureSuccessStatusCode();
        const long maximum=512L*1024*1024;
        if(response.Content.Headers.ContentLength is long contentLength && contentLength>maximum)
            throw new InvalidDataException("Bản cập nhật vượt dung lượng cho phép.");
        await using var input=await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
        var buffer=new byte[81920];
        long total=0;
        int read;
        while((read=await input.ReadAsync(buffer,cancellationToken))>0)
        {
            total+=read;
            if(total>maximum)throw new InvalidDataException("Bản cập nhật vượt dung lượng cho phép.");
            await output.WriteAsync(buffer.AsMemory(0,read),cancellationToken);
        }
        if(total==0)throw new InvalidDataException("Bản cập nhật rỗng.");
    }

    public static async Task CheckAndStageAsync(CancellationToken cancellationToken=default)
    {
        var release=await CheckLatestAsync(cancellationToken);
        if(release==null)return;
        var expectedContent=await DownloadTextAsync(release.ChecksumUrl,cancellationToken);
        var lines=expectedContent.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        var line=lines.SingleOrDefault()?.Trim()??"";
        var match=Regex.Match(line,@"^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<name>[^\\/]+)$");
        if(!match.Success||!string.Equals(match.Groups["name"].Value,release.InstallerName,StringComparison.Ordinal))
            throw new InvalidDataException("Tệp checksum cập nhật không khớp với bộ cài.");
        var expected=match.Groups["hash"].Value;
        if(release.GithubAssetDigest is string digest && digest.Length>0 &&
            !string.Equals(digest,"sha256:"+expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 trên GitHub và tệp checksum không khớp.");

        // Only installer cache is temporary; all warehouse state and backups remain in Documents.
        var root=Path.Combine(Path.GetTempPath(),"HISA-Verified-Updates");
        Directory.CreateDirectory(root);
        var file=Path.Combine(root,release.InstallerName);
        var staging=file+"."+Guid.NewGuid().ToString("N")+".download";
        try
        {
            await DownloadInstallerAsync(release.InstallerUrl,staging,cancellationToken);
            await using(var stream=File.OpenRead(staging))
            {
                var actual=Convert.ToHexString(await SHA256.HashDataAsync(stream,cancellationToken));
                if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Sai mã SHA-256; đã từ chối cài bản cập nhật.");
            }
            File.Move(staging,file,true);
            lock(Sync){_readyInstaller=file;ReadyVersion=release.Version;}
        }
        finally{if(File.Exists(staging))File.Delete(staging);}
    }

    /// <summary>Starts an unattended upgrade only after the warehouse application exits.</summary>
    public static void InstallWhenExiting()
    {
        string? setup;
        lock(Sync){setup=_readyInstaller;_readyInstaller=null;}
        if(setup==null||!File.Exists(setup))return;
        try
        {
            // /VERYSILENT hides the wizard; UAC consent is required by Windows for Program Files.
            Process.Start(new ProcessStartInfo
            {
                FileName=setup,
                Arguments="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /AUTORESTARTAPP",
                UseShellExecute=true,
                Verb="runas",
                WorkingDirectory=Path.GetDirectoryName(setup)!
            });
        }
        catch(Exception)
        {
            // If elevation is rejected, the current installation and data remain untouched.
        }
    }
}
