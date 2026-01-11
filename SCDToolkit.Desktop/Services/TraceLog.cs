using System;
using System.Diagnostics;
using System.IO;

namespace SCDToolkit.Desktop.Services
{
    public static class TraceLog
    {
        private static readonly object Gate = new();
        private static bool _initialized;

        public static string GetLogFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "SCDToolkit");
            Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "log.txt");
        }

        public static void Initialize()
        {
            lock (Gate)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    var path = GetLogFilePath();
                    var stream = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    var writer = new StreamWriter(stream) { AutoFlush = true };

                    Trace.AutoFlush = true;
                    Trace.Listeners.Add(new TextWriterTraceListener(writer));
                    Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TraceLog initialized");
                }
                catch
                {
                    // Best-effort.
                }
            }
        }
    }
}
