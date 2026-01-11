using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

    public async Task<(bool Started, string Message)> TryUpdateToLatestAsync(string currentExePath)
    {
        if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
        {
            return (false, "Could not determine the current executable path.");
        }

        var installDir = Path.GetDirectoryName(currentExePath);
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
        {
            return (false, "Could not determine the install directory.");
        }

        var updaterPath = Path.Combine(installDir, "updater.exe");
        if (!File.Exists(updaterPath))
        {
            return (false, "Updater executable not found next to the app (updater.exe).");
        }

        try
        {
            var currentVersion = GetCurrentVersion();
            var (zipUrl, tag) = await GetLatestZipAssetAsync();

            if (string.IsNullOrWhiteSpace(zipUrl))
            {
                return (false, "No update ZIP asset found on the latest GitHub release.");
            }

            var latestVersion = TryParseTagVersion(tag);
            if (currentVersion != null && latestVersion != null && latestVersion <= currentVersion)
            {
                return (false, $"You are already up to date. Current: v{currentVersion}, Latest: {tag}.");
            }

            var zipPath = Path.Combine(Path.GetTempPath(), $"SCDToolkit2_update_{tag}_{Guid.NewGuid():N}.zip");
            await DownloadAsync(zipUrl, zipPath);

            var exeName = Path.GetFileName(currentExePath);
            var pid = Environment.ProcessId;

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = $"\"{zipPath}\" \"{installDir}\" \"{exeName}\" --pid {pid}",
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = installDir
            });

            return (true, $"Updating to {tag}… The app will close to apply the update.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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

    private static async Task DownloadAsync(string url, string filePath)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("SCDToolkit2");

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using var fs = File.Create(filePath);
        await resp.Content.CopyToAsync(fs);
    }

    private static async Task<(string ZipUrl, string Tag)> GetLatestZipAssetAsync()
    {
        var apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        req.Headers.UserAgent.ParseAdd("SCDToolkit2");

        using var resp = await Http.SendAsync(req);
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
