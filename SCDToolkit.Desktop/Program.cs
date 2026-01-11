using Avalonia;
using Avalonia.ReactiveUI;
using SCDToolkit.Desktop.Services;
using System;
using System.IO;

namespace SCDToolkit.Desktop
{
	internal static class Program
	{
		// Avalonia configuration, don't remove; required for previewer.
		public static AppBuilder BuildAvaloniaApp() =>
			AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.WithInterFont()
				.LogToTrace()
				.UseReactiveUI();

		[STAThread]
		public static void Main(string[] args)
		{
			TraceLog.Initialize();
			ApplyPendingUpdaterUpdate();
			ImportRootConfigIfPresent();
			BuildAvaloniaApp()
				.StartWithClassicDesktopLifetime(args);
		}

		private static void ApplyPendingUpdaterUpdate()
		{
			try
			{
				var currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
				if (string.IsNullOrWhiteSpace(currentExePath))
					return;

				var appDir = Path.GetDirectoryName(currentExePath);
				if (string.IsNullOrWhiteSpace(appDir))
					return;

				// Determine install root (parent of 'app' folder or app folder itself)
				var dirName = Path.GetFileName(Path.GetFullPath(appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
				var rootDir = string.Equals(dirName, "app", StringComparison.OrdinalIgnoreCase)
					? Directory.GetParent(appDir)?.FullName
					: appDir;

				if (string.IsNullOrWhiteSpace(rootDir))
					return;

				var newUpdaterPath = Path.Combine(rootDir, "updater_new.exe");
				var updaterPath = Path.Combine(rootDir, "updater.exe");

				if (!File.Exists(newUpdaterPath))
					return;

				// Try to replace old updater with new one
				if (File.Exists(updaterPath))
				{
					try { File.Delete(updaterPath); }
					catch { }
				}

				File.Move(newUpdaterPath, updaterPath, overwrite: true);

				// Clean up any .next files from previous update attempt
				try { File.Delete(Path.Combine(rootDir, "updater.deps.next.json")); } catch { }
				try { File.Delete(Path.Combine(rootDir, "updater.runtimeconfig.next.json")); } catch { }
			}
			catch
			{
				// Silently fail - don't block app startup if updater swap fails
			}
		}

		private static void ImportRootConfigIfPresent()
		{
			try
			{
				var currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
				if (string.IsNullOrWhiteSpace(currentExePath))
					return;

				var appDir = Path.GetDirectoryName(currentExePath);
				if (string.IsNullOrWhiteSpace(appDir))
					return;

				// Determine install root (parent of 'app' folder or app folder itself)
				var dirName = Path.GetFileName(Path.GetFullPath(appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
				var rootDir = string.Equals(dirName, "app", StringComparison.OrdinalIgnoreCase)
					? Directory.GetParent(appDir)?.FullName
					: appDir;

				if (string.IsNullOrWhiteSpace(rootDir))
					return;

				// Check for scdtoolkit_config.json in root
				var rootConfigPath = Path.Combine(rootDir, "scdtoolkit_config.json");
				if (!File.Exists(rootConfigPath))
					return;

				// Import into AppData config
				ConfigLoader.ReplaceConfigFromFile(rootConfigPath);

				// Optionally delete the root config to avoid re-importing on every startup
				try { File.Delete(rootConfigPath); } catch { }
			}
			catch
			{
				// Silently fail - don't block app startup if config import fails
			}
		}
	}
}
