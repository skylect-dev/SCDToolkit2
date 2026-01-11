using System;
using System.IO;
using System.Text.Json;

namespace SCDToolkit.Desktop.Services
{
    public static class ConfigLoader
    {
        private static string ConfigPath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(appData, "SCDToolkit");
                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, "config.json");
            }
        }

        public static string GetConfigFilePath() => ConfigPath;

        public static bool ReplaceConfigFromFile(string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath)) return false;
                if (!File.Exists(sourcePath)) return false;
                File.Copy(sourcePath, ConfigPath, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static AppConfig Load()
        {
            var config = new AppConfig();
            try
            {
                var path = ConfigPath;
                if (!File.Exists(path))
                {
                    return config;
                }

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null)
                {
                    config = loaded;
                }
            }
            catch (Exception)
            {
                // Ignore and return defaults; we don't want config load to crash the app.
            }
            return config;
        }

        public static void Save(AppConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception)
            {
                // Best effort; ignore errors.
            }
        }
    }
}
