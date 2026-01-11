using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SCDToolkit.Core.Services;

namespace SCDToolkit.Desktop.Services;

public sealed record MusicPackExportRequest(
    int Slot,
    string OutputZipPath,
    string PackName,
    string Author,
    string Description,
    string InGameDescription,
    int? PackNameWidth,
    IReadOnlyDictionary<string, string> TrackAssignments,
    IReadOnlyDictionary<string, string>? SysPackNamesByLanguage = null,
    IReadOnlyDictionary<string, string>? SysDescriptionsByLanguage = null
);

public sealed record MusicPackExportProgress(int Percent, string Message);

public sealed class MusicPackExporter
{
    public async Task ExportAsync(MusicPackExportRequest request, IProgress<MusicPackExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var templatesRoot = ResolveMusicPackCreatorRoot();
        if (templatesRoot == null)
        {
            throw new InvalidOperationException("Could not locate 'music_pack_creator' templates folder.");
        }

        var templateDir = Path.Combine(templatesRoot, $"KH2-MusicTemplateSLOT{request.Slot}-main");
        if (!Directory.Exists(templateDir))
        {
            throw new DirectoryNotFoundException($"Template folder not found: {templateDir}");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"scdtoolkit_pack_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var packRoot = Path.Combine(tempRoot, $"KH2-MusicTemplateSLOT{request.Slot}-main");

        try
        {
            progress?.Report(new MusicPackExportProgress(3, "Copying template..."));
            CopyDirectory(templateDir, packRoot);

            cancellationToken.ThrowIfCancellationRequested();

            var bgmDir = Path.Combine(packRoot, "bgm");
            Directory.CreateDirectory(bgmDir);
            ClearDirectory(bgmDir);

            progress?.Report(new MusicPackExportProgress(10, "Updating metadata..."));
            UpdateModYml(Path.Combine(packRoot, "mod.yml"), request);
            UpdateSysYml(Path.Combine(packRoot, "msg", "sys.yml"), request);

            cancellationToken.ThrowIfCancellationRequested();

            var templateScdPath = ResolveDefaultTemplateScdPath();
            if (templateScdPath == null)
            {
                throw new InvalidOperationException("Could not locate a default SCD template (test.scd). Ensure the repo includes SingleEncoder/test.scd or set SCD_TEMPLATE_PATH.");
            }

            var encoder = new ScdEncoderService();

            var total = request.TrackAssignments.Count;
            if (total > 0)
            {
                var index = 0;

                foreach (var (vanillaFilename, sourcePath) in request.TrackAssignments)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    index++;
                    var pct = 15 + (int)Math.Round((index / (double)total) * 70);
                    progress?.Report(new MusicPackExportProgress(pct, $"Processing {vanillaFilename}..."));

                    var destFile = Path.Combine(bgmDir, vanillaFilename);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                    await WriteTrackAsync(encoder, templateScdPath, sourcePath, destFile, cancellationToken);
                }
            }
            else
            {
                progress?.Report(new MusicPackExportProgress(40, "No tracks assigned (metadata-only export)."));
            }

            progress?.Report(new MusicPackExportProgress(88, "Writing mapping..."));
            WriteSourceMap(packRoot, request);

            progress?.Report(new MusicPackExportProgress(92, "Creating ZIP..."));
            var outDir = Path.GetDirectoryName(request.OutputZipPath);
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            if (File.Exists(request.OutputZipPath))
            {
                File.Delete(request.OutputZipPath);
            }

            ZipFile.CreateFromDirectory(packRoot, request.OutputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            progress?.Report(new MusicPackExportProgress(100, "Done"));
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    public static string? ResolveMusicPackCreatorRoot()
    {
        var env = Environment.GetEnvironmentVariable("SCDTOOLKIT_MUSIC_PACK_TEMPLATES");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return env;
        }

        var baseDir = AppContext.BaseDirectory;

        var local = Path.Combine(baseDir, "music_pack_creator");
        if (Directory.Exists(local))
        {
            return local;
        }

        var repoLevel = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "music_pack_creator"));
        if (Directory.Exists(repoLevel))
        {
            return repoLevel;
        }

        var cwd = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "music_pack_creator"));
        if (Directory.Exists(cwd))
        {
            return cwd;
        }

        return null;
    }

    private static void ValidateRequest(MusicPackExportRequest request)
    {
        if (request.Slot is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(request.Slot));
        if (string.IsNullOrWhiteSpace(request.OutputZipPath)) throw new ArgumentException("OutputZipPath is required.");
        if (string.IsNullOrWhiteSpace(request.PackName)) throw new ArgumentException("PackName is required.");
        if (string.IsNullOrWhiteSpace(request.Author)) throw new ArgumentException("Author is required.");
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("Description is required.");

        // Allow partial exports (including metadata-only packs with zero assigned tracks).

        foreach (var (vanilla, src) in request.TrackAssignments)
        {
            if (string.IsNullOrWhiteSpace(vanilla)) throw new ArgumentException("Track assignment key is empty.");
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
            {
                throw new FileNotFoundException($"Assigned source file missing for '{vanilla}'", src);
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void ClearDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); } catch { }
        }

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            try { Directory.Delete(sub, recursive: true); } catch { }
        }
    }

    private static void UpdateModYml(string modYmlPath, MusicPackExportRequest request)
    {
        if (!File.Exists(modYmlPath)) return;

        var lines = File.ReadAllLines(modYmlPath);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("title:", StringComparison.Ordinal))
            {
                lines[i] = $"title: {request.PackName} (Slot {request.Slot}) [KH2]";
            }
            else if (lines[i].StartsWith("originalAuthor:", StringComparison.Ordinal))
            {
                lines[i] = $"originalAuthor: {request.Author}";
            }
            else if (lines[i].StartsWith("description:", StringComparison.Ordinal))
            {
                lines[i] = $"description: {request.Description}";
            }
        }

        File.WriteAllLines(modYmlPath, lines);
    }

    private static void UpdateSysYml(string sysYmlPath, MusicPackExportRequest request)
    {
        if (!File.Exists(sysYmlPath)) return;

        var nameId = request.Slot switch
        {
            0 => "0x5719",
            1 => "0x571B",
            _ => "0x571D"
        };

        var descId = request.Slot switch
        {
            0 => "0x571A",
            1 => "0x571C",
            _ => "0x571E"
        };

        var lines = File.ReadAllLines(sysYmlPath).ToList();
        var output = new List<string>(lines.Count);

        var width = request.PackNameWidth ?? CalculateWidth(request.PackName);

        for (var i = 0; i < lines.Count;)
        {
            var line = lines[i];

            if (line.Contains(nameId, StringComparison.OrdinalIgnoreCase))
            {
                output.Add(line);
                i++;
                while (i < lines.Count && !lines[i].TrimStart().StartsWith("- id:", StringComparison.Ordinal))
                {
                    var cur = lines[i];
                    if (IsLanguageLine(cur, out var lang))
                    {
                        var name = request.SysPackNamesByLanguage != null && request.SysPackNamesByLanguage.TryGetValue(lang, out var n)
                            ? n
                            : request.PackName;

                        output.Add($"  {lang}: \"{{:width {width}}}{EscapeForDoubleQuotes(name)}\"");
                    }
                    else
                    {
                        output.Add(cur);
                    }
                    i++;
                }
                continue;
            }

            if (line.Contains(descId, StringComparison.OrdinalIgnoreCase))
            {
                output.Add(line);
                i++;
                while (i < lines.Count && !lines[i].TrimStart().StartsWith("- id:", StringComparison.Ordinal))
                {
                    var cur = lines[i];
                    if (IsLanguageLine(cur, out var lang))
                    {
                        var desc = request.SysDescriptionsByLanguage != null && request.SysDescriptionsByLanguage.TryGetValue(lang, out var d)
                            ? d
                            : request.InGameDescription;

                        output.Add($"  {lang}: \"{EscapeForDoubleQuotes(desc)}\"");
                        i++;

                        // Skip any multiline continuation lines for the old description.
                        // The templates use multi-line double-quoted scalars like:
                        //   en: "This is the description of your
                        //
                        //     Custom Music Pack!"
                        // Which means we must consume blank/indented continuation lines too.
                        while (i < lines.Count)
                        {
                            var next = lines[i];
                            if (next.TrimStart().StartsWith("- id:", StringComparison.Ordinal) || IsLanguageLine(next, out _))
                            {
                                break;
                            }

                            i++;
                        }

                        continue;
                    }

                    // Skip any non-language lines inside this entry (continuation lines / blank lines).
                    i++;
                }
                continue;
            }

            output.Add(line);
            i++;
        }

        File.WriteAllLines(sysYmlPath, output);
    }

    private static bool IsLanguageLine(string line, out string lang)
    {
        lang = string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("en:") && !trimmed.StartsWith("it:") && !trimmed.StartsWith("gr:") && !trimmed.StartsWith("fr:") && !trimmed.StartsWith("sp:"))
        {
            return false;
        }

        lang = trimmed.Split(':', 2)[0].Trim();
        return true;
    }

    private static int CalculateWidth(string text)
    {
        var charCount = text?.Length ?? 0;
        var width = charCount switch
        {
            <= 15 => 100,
            <= 20 => 90,
            <= 25 => 85,
            _ => 80
        };
        return Math.Max(80, width);
    }

    private static string EscapeForDoubleQuotes(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void WriteSourceMap(string packRoot, MusicPackExportRequest request)
    {
        var mapPath = Path.Combine(packRoot, ".scdtoolkit_map.json");

        var data = new
        {
            version = 1,
            slot = request.Slot,
            mod_metadata = new { title = request.PackName, author = request.Author, description = request.Description },
            game_metadata = new { name = request.PackName, description = request.InGameDescription, name_width = request.PackNameWidth },
            sys_languages = request.SysPackNamesByLanguage != null || request.SysDescriptionsByLanguage != null
                ? new
                {
                    pack_name = request.SysPackNamesByLanguage,
                    description = request.SysDescriptionsByLanguage
                }
                : null,
            tracks = request.TrackAssignments.Select(kvp => new { vanilla_filename = kvp.Key, source_basename = Path.GetFileName(kvp.Value), source_path = kvp.Value }).ToList()
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(mapPath, json);
    }

    private static async Task WriteTrackAsync(ScdEncoderService encoder, string templateScdPath, string sourcePath, string destScdPath, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(sourcePath);
        if (string.Equals(ext, ".scd", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destScdPath, overwrite: true);
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"scdtoolkit_track_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        string? tempWav = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wavPath = Path.Combine(workDir, "input.wav");
            if (string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, wavPath, overwrite: true);
            }
            else
            {
                tempWav = wavPath;
                ConvertToWavWithFfmpeg(sourcePath, wavPath);
            }

            var templateCopy = Path.Combine(workDir, $"template_{Guid.NewGuid():N}.scd");
            File.Copy(templateScdPath, templateCopy, overwrite: true);

            var encodedPath = await encoder.EncodeAsync(templateCopy, wavPath, quality: 10, fullLoop: false);
            File.Copy(encodedPath, destScdPath, overwrite: true);
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { }
        }
    }

    private static void ConvertToWavWithFfmpeg(string sourcePath, string outputWav)
    {
        var ffmpegPath = ResolveFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new InvalidOperationException("ffmpeg.exe not found (set FFMPEG_PATH or place ffmpeg/bin/ffmpeg.exe).");
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -hide_banner -nostats -i \"{sourcePath}\" -vn -map 0:a:0 -c:a pcm_s16le \"{outputWav}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null)
        {
            throw new InvalidOperationException("Failed to start ffmpeg.");
        }

        p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0 || !File.Exists(outputWav))
        {
            throw new InvalidOperationException($"ffmpeg failed to convert to WAV. {err}");
        }
    }

    private static string? ResolveFfmpegPath()
    {
        var baseDir = AppContext.BaseDirectory;

        var env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        var local = Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe");
        if (File.Exists(local))
        {
            return local;
        }

        var repoLevel = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "ffmpeg", "bin", "ffmpeg.exe"));
        if (File.Exists(repoLevel))
        {
            return repoLevel;
        }

        return "ffmpeg.exe";
    }

    private static string? ResolveDefaultTemplateScdPath()
    {
        var env = Environment.GetEnvironmentVariable("SCD_TEMPLATE_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        var baseDir = AppContext.BaseDirectory;

        var local = Path.Combine(baseDir, "test.scd");
        if (File.Exists(local))
        {
            return local;
        }

        var repoSingleEncoder = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "SingleEncoder", "test.scd"));
        if (File.Exists(repoSingleEncoder))
        {
            return repoSingleEncoder;
        }

        var repoWavWithLoops = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "WAVWithLoopPoints", "test.scd"));
        if (File.Exists(repoWavWithLoops))
        {
            return repoWavWithLoops;
        }

        return null;
    }
}
