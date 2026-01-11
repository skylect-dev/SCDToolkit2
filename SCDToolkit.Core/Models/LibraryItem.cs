namespace SCDToolkit.Core.Models
{
    public class LibraryItem
    {
        public LibraryItem(string path, string displayName, string? details = null)
        {
            Path = path;
            DisplayName = displayName;
            Details = details ?? string.Empty;
        }

        public string Path { get; }
        public string DisplayName { get; }
        public string Details { get; }
        public LoopPoints? LoopPoints { get; set; }
        public AudioMetadata? Metadata { get; set; }
    }
}
