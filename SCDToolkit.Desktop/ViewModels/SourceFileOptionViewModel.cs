namespace SCDToolkit.Desktop.ViewModels;

public sealed class SourceFileOptionViewModel
{
    public SourceFileOptionViewModel(string path, string displayName, bool isLooped = false)
    {
        Path = path;
        DisplayName = displayName;
        IsLooped = isLooped;
    }

    public string Path { get; }

    public string DisplayName { get; }

    public bool IsLooped { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName;
}
