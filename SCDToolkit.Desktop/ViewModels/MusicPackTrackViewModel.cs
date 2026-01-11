using CommunityToolkit.Mvvm.ComponentModel;

namespace SCDToolkit.Desktop.ViewModels;

public partial class MusicPackTrackViewModel : ObservableObject
{
    public MusicPackTrackViewModel(string trackName, string vanillaFilename)
    {
        TrackName = trackName;
        VanillaFilename = vanillaFilename;
    }

    public string TrackName { get; }

    public string VanillaFilename { get; }

    [ObservableProperty]
    private string? sourcePath;

    [ObservableProperty]
    private SourceFileOptionViewModel? selectedSource;

    public bool IsAssigned => !string.IsNullOrWhiteSpace(SourcePath);

    public string SourceDisplay => string.IsNullOrWhiteSpace(SourcePath) ? "(unassigned)" : System.IO.Path.GetFileName(SourcePath);

    partial void OnSourcePathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsAssigned));
        OnPropertyChanged(nameof(SourceDisplay));
    }

    partial void OnSelectedSourceChanged(SourceFileOptionViewModel? value)
    {
        SourcePath = value?.Path;
    }
}
