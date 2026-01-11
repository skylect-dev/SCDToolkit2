using System.Text.Json.Serialization;

namespace SCDToolkit.Desktop.Services
{
    public sealed class NormalizationCacheEntry
    {
        [JsonPropertyName("file_last_write_utc_ticks")]
        public long FileLastWriteUtcTicks { get; set; }

        [JsonPropertyName("file_length")]
        public long FileLength { get; set; }

        // A key that identifies what normalization operation was applied (mode + params).
        [JsonPropertyName("profile_key")]
        public string ProfileKey { get; set; } = string.Empty;

        // If true, batch normalization should never overwrite this file.
        [JsonPropertyName("user_tuned")]
        public bool IsUserTuned { get; set; }

        [JsonPropertyName("updated_utc_ticks")]
        public long UpdatedUtcTicks { get; set; }
    }
}
