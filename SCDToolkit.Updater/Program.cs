using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SCDToolkit.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();

            if (args.Length < 3)
            {
                MessageBox.Show(
                    "Updater usage:\n\nupdater.exe <zipPath> <installDir> <exeName> [--pid <pid>]",
                    "SCDToolkit Updater",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 2;
            }

            var zipPath = args[0];
            var installDir = args[1];
            var exeName = args[2];

            int? pid = null;
            for (var i = 3; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var parsed))
                {
                    pid = parsed;
                    break;
                }
            }

            if (!File.Exists(zipPath))
            {
                MessageBox.Show($"Update zip not found:\n{zipPath}", "SCDToolkit Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }

            if (!Directory.Exists(installDir))
            {
                MessageBox.Show($"Install directory not found:\n{installDir}", "SCDToolkit Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 4;
            }

            if (pid.HasValue)
            {
                TryWaitForExit(pid.Value, TimeSpan.FromSeconds(15));
            }

            using var form = new UpdateForm();
            form.Show();
            form.Refresh();

            form.SetStatus("Extracting update...");

            var tempDir = Path.Combine(installDir, "temp_update");
            if (Directory.Exists(tempDir))
            {
                TryDeleteDirectory(tempDir);
            }
            Directory.CreateDirectory(tempDir);

            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

            // GitHub zips frequently contain a single top-level folder.
            var sourceRoot = ResolveSourceRoot(tempDir);

            form.SetStatus("Copying files...");

            CopyDirectory(sourceRoot, installDir, skipFileName: "updater.exe", progress: (done, total) =>
            {
                form.SetProgress(done, total);
                Application.DoEvents();
            });

            form.SetStatus("Cleaning up...");
            TryDeleteDirectory(tempDir);
            TryDeleteFile(zipPath);

            form.SetStatus("Launching updated app...");
            var exePath = Path.Combine(installDir, exeName);
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
            }

            form.SetStatus("Done");
            form.SetProgress(1, 1);
            Thread.Sleep(350);

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                MessageBox.Show(ex.ToString(), "SCDToolkit Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            return 1;
        }
    }

    private static string ResolveSourceRoot(string tempDir)
    {
        var dirs = Directory.GetDirectories(tempDir);
        var files = Directory.GetFiles(tempDir);

        if (files.Length == 0 && dirs.Length == 1)
        {
            return dirs[0];
        }

        return tempDir;
    }

    private static void TryWaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return;
            p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch
        {
            // best-effort
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir, string skipFileName, Action<int, int>? progress)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        var total = files.Length;
        var done = 0;

        foreach (var srcFile in files)
        {
            var rel = Path.GetRelativePath(sourceDir, srcFile);
            var dstFile = Path.Combine(destDir, rel);

            if (string.Equals(Path.GetFileName(dstFile), skipFileName, StringComparison.OrdinalIgnoreCase))
            {
                done++;
                progress?.Invoke(done, total);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);

            // Retry a bit in case the app is still releasing file handles.
            const int maxAttempts = 10;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.Copy(srcFile, dstFile, overwrite: true);
                    break;
                }
                catch when (attempt < maxAttempts)
                {
                    Thread.Sleep(250);
                }
            }

            done++;
            progress?.Invoke(done, total);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
