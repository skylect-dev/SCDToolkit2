using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SCDToolkit.Desktop.Services
{
    public class AppConfig
    {
        [JsonPropertyName("library_folders")]
        public List<string>? LibraryFolders { get; set; }

        [JsonPropertyName("scan_subdirs")]
        public bool? ScanSubdirs { get; set; }

        /// <summary>
        /// Volume as 0-100. Defaults to 70 if not provided.
        /// </summary>
        [JsonPropertyName("volume")]
        public double? Volume { get; set; }

        [JsonPropertyName("kh_rando_music_folder")]
        public string? KhRandoMusicFolder { get; set; }

        [JsonPropertyName("show_kh_rando")]
        public bool? ShowKhRando { get; set; }

        [JsonPropertyName("normalization_cache")]
        public Dictionary<string, NormalizationCacheEntry>? NormalizationCache { get; set; }
    }
}
