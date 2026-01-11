using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SCDToolkit.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var appDir = Path.Combine(baseDir, "app");

        var targetExe = ResolveTargetExe(appDir);
        if (targetExe is null)
        {
            MessageBox.Show(
                $"Could not find the app executable in:\n{appDir}",
                "SCDToolkit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetExe,
                WorkingDirectory = Path.GetDirectoryName(targetExe) ?? appDir,
                UseShellExecute = false,
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start the app:\n{ex.Message}",
                "SCDToolkit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string? ResolveTargetExe(string appDir)
    {
        var defaultPath = Path.Combine(appDir, "SCDToolkit.Desktop.exe");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        if (!Directory.Exists(appDir))
        {
            return null;
        }

        var candidates = Directory.EnumerateFiles(appDir, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(p => !string.Equals(Path.GetFileName(p), "updater.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        return candidates.FirstOrDefault(p => p.EndsWith(".Desktop.exe", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }
}
