using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SCDToolkit.Desktop.Services;

public sealed class UpdateService
{
    // New repo for Avalonia version.
    private const string RepoOwner = "skylect-dev";
    private const string RepoName = "SCDToolkit2";

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });

    public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes);

    public sealed record UpdateInfo(
        string ZipUrl,
        string Tag,
        Version? CurrentVersion,
        Version? LatestVersion,
        bool IsUpdateAvailable,
        string Message);

    public async Task<UpdateInfo> GetUpdateInfoAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        var (zipUrl, tag) = await GetLatestZipAssetAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(zipUrl))
        {
            return new UpdateInfo(
                ZipUrl: string.Empty,
                Tag: tag,
                CurrentVersion: currentVersion,
                LatestVersion: TryParseTagVersion(tag),
                IsUpdateAvailable: false,
                Message: "No update ZIP asset found on the latest GitHub release.");
        }

        var latestVersion = TryParseTagVersion(tag);
        var isUpdateAvailable = latestVersion != null && (currentVersion == null || latestVersion > currentVersion);

        var message = isUpdateAvailable
            ? $"Update available. Current: v{currentVersion}, Latest: {tag}."
            : $"You are already up to date. Current: v{currentVersion}, Latest: {tag}.";

        return new UpdateInfo(zipUrl, tag, currentVersion, latestVersion, isUpdateAvailable, message);
    }

    public async Task<string> DownloadUpdateZipAsync(
        string zipUrl,
        string tag,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipUrl)) throw new ArgumentException("Zip URL is required.", nameof(zipUrl));
        if (string.IsNullOrWhiteSpace(tag)) tag = "latest";

        var zipPath = Path.Combine(Path.GetTempPath(), $"SCDToolkit2_update_{tag}_{Guid.NewGuid():N}.zip");
        await DownloadAsync(zipUrl, zipPath, progress, cancellationToken);
        return zipPath;
    }

    public (bool Started, string Message) TryStartUpdaterFromZip(string currentExePath, string zipPath)
    {
        if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
        {
            return (false, "Could not determine the current executable path.");
        }

        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            return (false, "Update zip not found.");
        }

        var appDir = Path.GetDirectoryName(currentExePath);
        if (string.IsNullOrWhiteSpace(appDir) || !Directory.Exists(appDir))
        {
            return (false, "Could not determine the install directory.");
        }

        var (installDir, relaunchExeName) = ResolveInstallRootAndRelaunchExe(appDir, currentExePath);

        var updaterPath = Path.Combine(installDir, "updater.exe");
        if (!File.Exists(updaterPath))
        {
            // Dev builds may still have updater next to the app.
            updaterPath = Path.Combine(appDir, "updater.exe");
        }
        if (!File.Exists(updaterPath))
        {
            return (false, "Updater executable not found (updater.exe). It must be next to the launcher or the app.");
        }

        try
        {
            var pid = Environment.ProcessId;
            var args = $"\"{zipPath}\" \"{installDir}\" \"{relaunchExeName}\" --pid {pid}";

            System.Diagnostics.Trace.WriteLine($"[Update] Starting updater: {updaterPath}");
            System.Diagnostics.Trace.WriteLine($"[Update] Args: {args}");
            System.Diagnostics.Trace.WriteLine($"[Update] WorkingDirectory: {installDir}");

            Process? proc = null;
            try
            {
                // Prefer direct process creation for reliability.
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = installDir
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Update] Updater Process.Start failed (UseShellExecute=false): {ex}");
            }

            if (proc == null)
            {
                // Fallback: shell execute.
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = installDir
                });
            }

            if (proc == null)
            {
                return (false, "Failed to start updater process.");
            }

            // If it dies immediately, treat as failure so we don't close the app.
            try
            {
                System.Threading.Thread.Sleep(200);
                if (proc.HasExited)
                {
                    return (false, $"Updater exited immediately (code {proc.ExitCode}).");
                }
            }
            catch
            {
                // Ignore; still treat as started.
            }

            return (true, $"Launching updater… (PID {proc.Id})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Started, string Message)> TryUpdateToLatestAsync(string currentExePath)
    {
        if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
        {
            return (false, "Could not determine the current executable path.");
        }

        var appDir = Path.GetDirectoryName(currentExePath);
        if (string.IsNullOrWhiteSpace(appDir) || !Directory.Exists(appDir))
        {
            return (false, "Could not determine the install directory.");
        }

        var (installDir, relaunchExeName) = ResolveInstallRootAndRelaunchExe(appDir, currentExePath);

        var updaterPath = Path.Combine(installDir, "updater.exe");
        if (!File.Exists(updaterPath))
        {
            // Dev builds may still have updater next to the app.
            updaterPath = Path.Combine(appDir, "updater.exe");
        }
        if (!File.Exists(updaterPath))
        {
            return (false, "Updater executable not found (updater.exe). It must be next to the launcher or the app.");
        }

        try
        {
            var info = await GetUpdateInfoAsync();
            if (!info.IsUpdateAvailable)
            {
                return (false, info.Message);
            }

            var zipPath = await DownloadUpdateZipAsync(info.ZipUrl, info.Tag);

            var pid = Environment.ProcessId;

            var started = Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = $"\"{zipPath}\" \"{installDir}\" \"{relaunchExeName}\" --pid {pid}",
                // Use ShellExecute so the WinForms GUI shows reliably.
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = installDir
            });

            if (started == null)
            {
                return (false, "Failed to start updater process.");
            }

            return (true, $"Updating to {info.Tag}… The app will close to apply the update.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (string InstallRoot, string RelaunchExeName) ResolveInstallRootAndRelaunchExe(string appDir, string currentExePath)
    {
        // Installed layout:
        //   <root>\SCDToolkit.exe         (launcher)
        //   <root>\updater.exe
        //   <root>\app\SCDToolkit.Desktop.exe  (real app)
        // In this case, updates must be applied to <root> and we should relaunch the launcher.
        var dirName = Path.GetFileName(Path.GetFullPath(appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        if (string.Equals(dirName, "app", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(appDir)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                return (parent, "SCDToolkit.exe");
            }
        }

        // Fallback: treat the app directory as the install root.
        return (appDir, Path.GetFileName(currentExePath));
    }

    private static Version? GetCurrentVersion()
    {
        try
        {
            // Prefer informational version if present, otherwise fall back to assembly version.
            var asm = typeof(UpdateService).Assembly;
            var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(info))
            {
                var v = TryParseTagVersion(info);
                if (v != null) return v;
            }

            return asm.GetName().Version;
        }
        catch
        {
            return null;
        }
    }

    private static Version? TryParseTagVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        // Accept tags like: v1.2.3, 1.2.3, v1.2.3-beta, 1.2
        var s = tag.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            s = s[1..];
        }

        var core = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (string.IsNullOrWhiteSpace(core)) return null;

        // Normalize to at least Major.Minor.Build
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) core = parts[0] + ".0.0";
        else if (parts.Length == 2) core = parts[0] + "." + parts[1] + ".0";

        return Version.TryParse(core, out var v) ? v : null;
    }

    private static async Task DownloadAsync(string url, string filePath, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("SCDToolkit2");

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;

        await using var fs = File.Create(filePath);
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new DownloadProgress(received, total));
        }
    }

    private static async Task<(string ZipUrl, string Tag)> GetLatestZipAssetAsync(CancellationToken cancellationToken)
    {
        var apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        req.Headers.UserAgent.ParseAdd("SCDToolkit2");

        using var resp = await Http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? (tagEl.GetString() ?? "latest") : "latest";

        if (!root.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
        {
            return (string.Empty, tag);
        }

        foreach (var asset in assetsEl.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            if (asset.TryGetProperty("browser_download_url", out var urlEl))
            {
                return (urlEl.GetString() ?? string.Empty, tag);
            }
        }

        return (string.Empty, tag);
    }
}
