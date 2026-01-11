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
        var logPath = Path.Combine(AppContext.BaseDirectory, "launcher.log");
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var appDir = Path.Combine(baseDir, "app");
            File.WriteAllText(logPath, $"BaseDir: {baseDir}\nAppDir: {appDir}\nExists: {Directory.Exists(appDir)}\n");

            var targetExe = ResolveTargetExe(appDir);
            File.AppendAllText(logPath, $"TargetExe: {targetExe}\n");
            
            if (targetExe is null)
            {
                MessageBox.Show(
                    $"Could not find the app executable in:\n{appDir}\n\nBase: {baseDir}\nExists: {Directory.Exists(appDir)}",
                    "SCDToolkit Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = targetExe,
                WorkingDirectory = Path.GetDirectoryName(targetExe) ?? appDir,
                UseShellExecute = true,
            };

            // When UseShellExecute is true, use Arguments instead of ArgumentList
            if (args.Length > 0)
            {
                psi.Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            }

            File.AppendAllText(logPath, $"Starting: {psi.FileName}\nWorkingDir: {psi.WorkingDirectory}\n");
            var proc = Process.Start(psi);
            File.AppendAllText(logPath, $"Process started: {proc != null}\nPID: {proc?.Id}\n");
            
            if (proc == null)
            {
                MessageBox.Show(
                    $"Process.Start returned null for:\n{targetExe}",
                    "SCDToolkit Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"ERROR: {ex}\n");
            MessageBox.Show(
                $"Failed to start the app:\n{ex.Message}\n\n{ex.StackTrace}",
                "SCDToolkit Launcher",
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
