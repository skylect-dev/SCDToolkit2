using System;
using System.IO;
using System.IO.Compression;

namespace SCDToolkit.Desktop.Services;

public sealed class TitleScreenModExporter
{
    public void Export(string sourceScdPath, string outputZipPath)
    {
        if (string.IsNullOrWhiteSpace(sourceScdPath)) throw new ArgumentException("Source SCD path is required", nameof(sourceScdPath));
        if (!File.Exists(sourceScdPath)) throw new FileNotFoundException("Source SCD not found", sourceScdPath);
        if (!sourceScdPath.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source file must be a .scd");

        if (string.IsNullOrWhiteSpace(outputZipPath)) throw new ArgumentException("Output zip path is required", nameof(outputZipPath));

        var templatesRoot = MusicPackExporter.ResolveMusicPackCreatorRoot();
        if (templatesRoot == null)
        {
            throw new InvalidOperationException("Could not locate 'music_pack_creator' templates folder.");
        }

        var templateDir = Path.Combine(templatesRoot, "TitleScreenMusicReplacer_Template");
        if (!Directory.Exists(templateDir))
        {
            throw new DirectoryNotFoundException($"Template folder not found: {templateDir}");
        }

        var suffix = Path.GetFileNameWithoutExtension(sourceScdPath);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"scdtoolkit_titlemod_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var packRoot = Path.Combine(tempRoot, "TitleScreenMusicReplacer");
            CopyDirectory(templateDir, packRoot);

            var titleDir = Path.Combine(packRoot, "title");
            Directory.CreateDirectory(titleDir);

            var destScd = Path.Combine(titleDir, "Title.win32.scd");
            File.Copy(sourceScdPath, destScd, overwrite: true);

            var modYmlPath = Path.Combine(packRoot, "mod.yml");
            if (File.Exists(modYmlPath))
            {
                UpdateModTitle(modYmlPath, suffix);
            }

            var outDir = Path.GetDirectoryName(outputZipPath);
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            if (File.Exists(outputZipPath))
            {
                File.Delete(outputZipPath);
            }

            ZipFile.CreateFromDirectory(packRoot, outputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void UpdateModTitle(string modYmlPath, string suffix)
    {
        var lines = File.ReadAllLines(modYmlPath);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("title:", StringComparison.Ordinal))
            {
                continue;
            }

            var current = lines[i].Substring("title:".Length).Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                lines[i] = $"title: {suffix}";
                continue;
            }

            var desiredSuffix = $"- {suffix}";
            if (current.EndsWith(desiredSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lines[i] = $"title: {current} {desiredSuffix}";
            break;
        }

        File.WriteAllLines(modYmlPath, lines);
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
}
