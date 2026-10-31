using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Hisa.App;

/// <summary>
/// Download and validate a signed-off GitHub Release asset without touching warehouse data.
/// Downloading is NOT consent to installation: only an explicit click can install on exit.
/// </summary>
public static class AutoUpdater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/infiniti-money/Xuat-hang/releases/latest";
    private const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim CheckGate = new(1, 1);
    private static readonly object Sync = new();
    private static string? _readyInstaller;
    private static bool _installRequested;
    public static event Action<long, long>? DownloadProgress;
    public static string? ReadyVersion { get; private set; }

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HISA Kho Vat Tu", "Updater");
    public static string InstallerLogPath => Path.Combine(LogDirectory, "HISA-setup.log");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HISA-Kho-Vat-Tu/" + SoftwareInfo.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Uri TrustedAssetUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com"
            || !uri.AbsolutePath.StartsWith(
                "/infiniti-money/Xuat-hang/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Đường dẫn không thuộc GitHub Releases HISA.");
        return uri;
    }

    private sealed record Release(string Version, string InstallerName, Uri InstallerUrl,
        Uri ChecksumUrl, string? GithubAssetDigest, long Size);

    private static async Task<Release?> CheckLatestAsync(CancellationToken token)
    {
        using var response = await Http.GetAsync(
            LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: token);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v', StringComparison.OrdinalIgnoreCase)
            || !Version.TryParse(tag[1..], out var available)
            || !Version.TryParse(SoftwareInfo.Version, out var current)
            || available <= current) return null;

        var name = "HISA-Kho-Vat-Tu-" + available.ToString(3) + "-Setup.exe";
        Uri? setup = null, checksum = null;
        string? digest = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var filename = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (filename.Equals(name, StringComparison.Ordinal))
            {
                setup = TrustedAssetUri(url);
                size = asset.GetProperty("size").GetInt64();
                if (asset.TryGetProperty("digest", out var sha)
                    && sha.ValueKind == JsonValueKind.String) digest = sha.GetString();
            }
            else if (filename.Equals(name + ".sha256", StringComparison.Ordinal))
                checksum = TrustedAssetUri(url);
        }
        if (setup == null || checksum == null) return null;
        if (size <= 0 || size > MaximumInstallerBytes)
            throw new InvalidDataException("Kích thước bộ cài không hợp lệ.");
        return new Release(available.ToString(3), name, setup, checksum, digest, size);
    }

    private static async Task<string> DownloadTextAsync(Uri uri, CancellationToken token)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 2048)
            throw new InvalidDataException("Tệp SHA-256 quá dài.");
        var value = await response.Content.ReadAsStringAsync(token);
        if (value.Length > 2048) throw new InvalidDataException("Tệp SHA-256 quá dài.");
        return value;
    }

    internal static string ParseChecksum(string content, string installerName)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var line = lines.SingleOrDefault()?.Trim() ?? "";
        var match = Regex.Match(line, @"^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<name>[^\\/]+)$");
        if (!match.Success || !string.Equals(match.Groups["name"].Value,
                installerName, StringComparison.Ordinal))
            throw new InvalidDataException("Tệp checksum không đúng tên hoặc định dạng bộ cài.");
        return match.Groups["hash"].Value;
    }

    internal static bool ValidRange(HttpResponseMessage response, long start, long expected)
    {
        if (response.StatusCode != HttpStatusCode.PartialContent || start <= 0) return false;
        var range = response.Content.Headers.ContentRange;
        return range != null && range.HasLength && range.Length == expected
               && range.HasRange && range.From == start
               && range.To == expected - 1;
    }

    /// <summary>
    /// On interruption a .partial file is retained. A 206 must match the exact byte boundary;
    /// if the server ignores Range and returns 200, restart the same file from byte zero.
    /// </summary>
    internal static async Task DownloadInstallerAsync(
        HttpClient client, Uri uri, string partial, long expected, CancellationToken token)
    {
        if (expected <= 0 || expected > MaximumInstallerBytes)
            throw new InvalidDataException("Dung lượng bộ cài không hợp lệ.");
        long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset >= expected)
        {
            if (offset == expected) return;
            File.Delete(partial);
            offset = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        bool resume = offset > 0 && ValidRange(response, offset, expected);
        if (response.StatusCode == HttpStatusCode.PartialContent && !resume)
            throw new InvalidDataException("Máy chủ trả sai đoạn dữ liệu. Không nối tệp tải bị lệch.");
        if (response.StatusCode != HttpStatusCode.OK && !resume)
            throw new InvalidDataException("Máy chủ không trả bộ cài hoàn chỉnh.");

        long received = resume ? offset : 0;
        if (response.Content.Headers.ContentLength is long size
            && (size <= 0 || size != expected - received))
            throw new InvalidDataException("Dung lượng phản hồi không khớp bản phát hành.");

        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(partial, FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        if (!resume) output.SetLength(0);
        output.Position = received;
        DownloadProgress?.Invoke(received, expected);
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            received += read;
            if (received > expected) throw new InvalidDataException("Tải vượt dung lượng dự kiến.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            DownloadProgress?.Invoke(received, expected);
        }
        await output.FlushAsync(token);
        if (received != expected)
            throw new IOException("Tải bị gián đoạn; đã giữ phần dữ liệu để tiếp tục ở lần thử sau.");
    }

    public static async Task<string?> CheckAndStageAsync(CancellationToken token = default)
    {
        await CheckGate.WaitAsync(token);
        try { return await StageCoreAsync(token); }
        finally { CheckGate.Release(); }
    }

    private static async Task<string?> StageCoreAsync(CancellationToken token)
    {
        var release = await CheckLatestAsync(token);
        if (release == null) return null;
        var expected = ParseChecksum(await DownloadTextAsync(release.ChecksumUrl, token),
            release.InstallerName);
        if (!string.IsNullOrWhiteSpace(release.GithubAssetDigest) &&
            !string.Equals(release.GithubAssetDigest, "sha256:" + expected,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 GitHub và tệp checksum không khớp.");

        var folder = Path.Combine(Path.GetTempPath(), "HISA-Verified-Updates");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, release.InstallerName);
        var partial = file + ".partial";

        async Task<bool> HashMatches(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != release.Size) return false;
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
            return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        if (!await HashMatches(file))
        {
            if (File.Exists(file)) File.Delete(file);
            await DownloadInstallerAsync(Http, release.InstallerUrl, partial, release.Size, token);
            if (!await HashMatches(partial))
            {
                File.Delete(partial);
                throw new InvalidDataException("SHA-256 bộ cài không khớp; đã xóa tệp lỗi.");
            }
            File.Move(partial, file, true);
        }
        lock (Sync)
        {
            _readyInstaller = file;
            ReadyVersion = release.Version;
        }
        return release.Version;
    }

    /// <summary>Only an explicit action in the UI authorizes the installer on application exit.</summary>
    public static bool RequestInstall()
    {
        lock (Sync)
        {
            if (_readyInstaller == null || ReadyVersion == null
                || !File.Exists(_readyInstaller)) return false;
            _installRequested = true;
            return true;
        }
    }

    private static void LogLaunchError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(Path.Combine(LogDirectory, "HISA-update-errors.log"),
                DateTime.Now.ToString("O") + " " + ex.GetType().Name + ": " + ex.Message
                + Environment.NewLine);
        }
        catch { /* Never block warehouse shutdown for diagnostics. */ }
    }

    public static void InstallWhenExiting()
    {
        string? setup;
        lock (Sync)
        {
            if (!_installRequested) return;
            setup = _readyInstaller;
            _installRequested = false;
        }
        if (setup == null || !File.Exists(setup)) return;
        try
        {
            Directory.CreateDirectory(LogDirectory);
            // The normal Inno Setup UI shows any installation error and keeps its /LOG trace.
            // Do not silently restart or install merely because the download finished.
            Process.Start(new ProcessStartInfo
            {
                FileName = setup,
                Arguments = "/NORESTART /CLOSEAPPLICATIONS /LOG=\"" + InstallerLogPath + "\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(setup)!
            }) ?? throw new InvalidOperationException("Windows không khởi chạy được bộ cài.");
        }
        catch (Exception ex)
        {
            LogLaunchError(ex);
            MessageBox.Show("Không mở được trình cài đặt: " + ex.Message
                + "\nNhật ký: " + LogDirectory
                + "\nDữ liệu kho và phiên bản cũ vẫn được giữ nguyên.",
                "HISA · Lỗi nâng cấp", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
