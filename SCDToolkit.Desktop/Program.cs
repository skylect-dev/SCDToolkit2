using Avalonia;
using Avalonia.ReactiveUI;
using SCDToolkit.Desktop.Services;

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
			BuildAvaloniaApp()
				.StartWithClassicDesktopLifetime(args);
		}
	}
}
