using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCDToolkit.Desktop.Services;

using SCDToolkit.Core.Services;

namespace SCDToolkit.Desktop.ViewModels;

public partial class MusicPackCreatorViewModel : ObservableObject
{
    private readonly List<SourceFileOptionViewModel> _allSources;
    private readonly SourceFileOptionViewModel _notAssigned = new("", "-- Not Assigned --");
    private readonly MusicPackExporter _exporter = new();

    private readonly ScdParser _scdParser = new();
    private IStorageProvider? _storageProvider;

    private readonly List<MusicPackTrackViewModel> _subscribedTracks = new();

    [ObservableProperty]
    private ObservableCollection<MusicPackTrackViewModel> tracks = new();

    [ObservableProperty]
    private ObservableCollection<MusicPackTrackViewModel> filteredTracks = new();

    [ObservableProperty]
    private ObservableCollection<SourceFileOptionViewModel> sourceOptions = new();

    [ObservableProperty]
    private ObservableCollection<SourceFileOptionViewModel> filteredSources = new();

    [ObservableProperty]
    private MusicPackTrackViewModel? selectedTrack;

    [ObservableProperty]
    private SourceFileOptionViewModel? selectedLibrarySource;

    [ObservableProperty]
    private string sourceSearchText = string.Empty;

    partial void OnSourceSearchTextChanged(string value) => ApplySourceFilter();

    [ObservableProperty]
    private string trackSearchText = string.Empty;

    partial void OnTrackSearchTextChanged(string value) => ApplyTrackFilter();

    [ObservableProperty]
    private int slot;

    partial void OnSlotChanged(int value)
    {
        if (ExportSlot1And2 && value != 1)
        {
            ExportSlot1And2 = false;
        }

        RaiseSlotFlags();
    }

    [ObservableProperty]
    private bool exportSlot1And2;

    partial void OnExportSlot1And2Changed(bool value)
    {
        if (value)
        {
            Slot = 1;
        }

        RaiseSlotFlags();
    }

    public bool IsSlot0
    {
        get => !ExportSlot1And2 && Slot == 0;
        set
        {
            if (!value) return;
            ExportSlot1And2 = false;
            Slot = 0;
            RaiseSlotFlags();
        }
    }

    public bool IsSlot1
    {
        get => !ExportSlot1And2 && Slot == 1;
        set
        {
            if (!value) return;
            ExportSlot1And2 = false;
            Slot = 1;
            RaiseSlotFlags();
        }
    }

    public bool IsSlot2
    {
        get => !ExportSlot1And2 && Slot == 2;
        set
        {
            if (!value) return;
            ExportSlot1And2 = false;
            Slot = 2;
            RaiseSlotFlags();
        }
    }

    public bool IsSlot1And2
    {
        get => ExportSlot1And2;
        set
        {
            if (!value) return;
            ExportSlot1And2 = true;
            Slot = 1;
            RaiseSlotFlags();
        }
    }

    private void RaiseSlotFlags()
    {
        OnPropertyChanged(nameof(IsSlot0));
        OnPropertyChanged(nameof(IsSlot1));
        OnPropertyChanged(nameof(IsSlot2));
        OnPropertyChanged(nameof(IsSlot1And2));
    }

    [ObservableProperty]
    private string packName = "My Music Pack";

    [ObservableProperty]
    private string author = "";

    [ObservableProperty]
    private string description = "";

    [ObservableProperty]
    private string inGameDescription = "";

    [ObservableProperty]
    private int? packNameWidth;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string status = "";

    [ObservableProperty]
    private string librarySummary = "";

    [ObservableProperty]
    private string trackListSummary = "";

    [ObservableProperty]
    private string assignedSummary = "Assigned: 0 / 0";

    [ObservableProperty]
    private bool usePerLanguage;

    public bool UseSingleLanguage => !UsePerLanguage;

    [RelayCommand]
    private void TogglePerLanguage()
    {
        UsePerLanguage = !UsePerLanguage;
    }

    partial void OnUsePerLanguageChanged(bool value)
    {
        if (value)
        {
            // Initialize per-language fields with current values for convenience.
            PackNameEn = PackName;
            PackNameIt = PackName;
            PackNameGr = PackName;
            PackNameFr = PackName;
            PackNameSp = PackName;

            DescriptionEn = InGameDescription;
            DescriptionIt = InGameDescription;
            DescriptionGr = InGameDescription;
            DescriptionFr = InGameDescription;
            DescriptionSp = InGameDescription;
        }

        OnPropertyChanged(nameof(UseSingleLanguage));
    }

    public void AttachStorageProvider(IStorageProvider? storageProvider)
    {
        _storageProvider = storageProvider;
    }

    [RelayCommand]
    private void SelectSlot0()
    {
        ExportSlot1And2 = false;
        Slot = 0;
        RaiseSlotFlags();
    }

    [RelayCommand]
    private void SelectSlot1()
    {
        ExportSlot1And2 = false;
        Slot = 1;
        RaiseSlotFlags();
    }

    [RelayCommand]
    private void SelectSlot2()
    {
        ExportSlot1And2 = false;
        Slot = 2;
        RaiseSlotFlags();
    }

    [RelayCommand]
    private void SelectSlot1And2()
    {
        ExportSlot1And2 = true;
        Slot = 1;
        RaiseSlotFlags();
    }

    [ObservableProperty] private string packNameEn = "";
    [ObservableProperty] private string packNameIt = "";
    [ObservableProperty] private string packNameGr = "";
    [ObservableProperty] private string packNameFr = "";
    [ObservableProperty] private string packNameSp = "";

    [ObservableProperty] private string descriptionEn = "";
    [ObservableProperty] private string descriptionIt = "";
    [ObservableProperty] private string descriptionGr = "";
    [ObservableProperty] private string descriptionFr = "";
    [ObservableProperty] private string descriptionSp = "";

    public MusicPackCreatorViewModel(IEnumerable<LibraryItemViewModel> libraryItems)
    {
        // Pack Creator intentionally only supports SCD sources.
        _allSources = libraryItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Path) && File.Exists(i.Path))
            .Where(i => string.Equals(Path.GetExtension(i.Path), ".scd", StringComparison.OrdinalIgnoreCase))
            .Select(i => new SourceFileOptionViewModel(i.Path!, i.DisplayName, IsItemLooped(i)))
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        UpdateLibrarySummary();

        SourceOptions = new ObservableCollection<SourceFileOptionViewModel>(new[] { _notAssigned }.Concat(_allSources));
        ApplySourceFilter();

        LoadTrackList();
        ApplyTrackFilter();
    }

    private void UpdateLibrarySummary()
    {
        var total = _allSources.Count;
        var looped = _allSources.Count(s => s.IsLooped);
        LibrarySummary = $"Total SCD files: {total} ({looped} looped)";
    }

    private bool IsItemLooped(LibraryItemViewModel item)
    {
        // Prefer already-loaded loop points.
        if (item.LoopEnd > item.LoopStart)
        {
            return item.LoopEnd > 0;
        }

        // Fall back to parsing the SCD if needed.
        try
        {
            if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
                return false;
            var loop = _scdParser.ReadLoopPoints(item.Path);
            return loop != null && loop.EndSample > loop.StartSample;
        }
        catch
        {
            return false;
        }
    }

    private void LoadTrackList()
    {
        var root = MusicPackExporter.ResolveMusicPackCreatorRoot();
        if (root == null)
        {
            Status = "Templates not found: could not locate music_pack_creator folder.";
            TrackListSummary = "Loaded 0 tracks.";
            return;
        }

        var trackListPath = Path.Combine(root, "TrackList.txt");
        if (!File.Exists(trackListPath))
        {
            Status = $"TrackList.txt not found: {trackListPath}";
            TrackListSummary = "Loaded 0 tracks.";
            return;
        }

        var parsed = new List<MusicPackTrackViewModel>();
        foreach (var rawLine in File.ReadAllLines(trackListPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("#")) continue;

            var parts = line.Split(" - ", 2, StringSplitOptions.None);
            if (parts.Length != 2) continue;

            var name = parts[0].Trim();
            var filename = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(filename)) continue;

            parsed.Add(new MusicPackTrackViewModel(name, filename));
        }

        Tracks = new ObservableCollection<MusicPackTrackViewModel>(parsed);
        foreach (var t in Tracks)
        {
            t.SelectedSource = _notAssigned;
        }

        AttachTrackChangeHandlers();
        UpdateAssignedStatus();

        TrackListSummary = $"Loaded {Tracks.Count} tracks.";
        Status = "";
    }

    private void AttachTrackChangeHandlers()
    {
        foreach (var old in _subscribedTracks)
        {
            old.PropertyChanged -= TrackOnPropertyChanged;
        }
        _subscribedTracks.Clear();

        foreach (var t in Tracks)
        {
            t.PropertyChanged += TrackOnPropertyChanged;
            _subscribedTracks.Add(t);
        }
    }

    private void TrackOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MusicPackTrackViewModel.SourcePath) or nameof(MusicPackTrackViewModel.SelectedSource))
        {
            UpdateAssignedStatus();
        }
    }

    private void ApplyTrackFilter()
    {
        var query = (TrackSearchText ?? string.Empty).Trim();
        IEnumerable<MusicPackTrackViewModel> items = Tracks;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var lowered = query.ToLowerInvariant();
            items = items.Where(t =>
                t.TrackName.ToLowerInvariant().Contains(lowered) ||
                t.VanillaFilename.ToLowerInvariant().Contains(lowered) ||
                t.SourceDisplay.ToLowerInvariant().Contains(lowered));
        }

        FilteredTracks = new ObservableCollection<MusicPackTrackViewModel>(items);
        UpdateAssignedStatus();
    }

    private void ApplySourceFilter()
    {
        var query = (SourceSearchText ?? string.Empty).Trim();
        IEnumerable<SourceFileOptionViewModel> items = _allSources;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var lowered = query.ToLowerInvariant();
            items = items.Where(s =>
                (s.DisplayName?.ToLowerInvariant().Contains(lowered) ?? false) ||
                s.FileName.ToLowerInvariant().Contains(lowered) ||
                s.Path.ToLowerInvariant().Contains(lowered));
        }

        FilteredSources = new ObservableCollection<SourceFileOptionViewModel>(items.Take(500));
    }

    [RelayCommand]
    private void ClearAllAssignments()
    {
        foreach (var t in Tracks)
        {
            t.SelectedSource = _notAssigned;
        }
        UpdateAssignedStatus();
    }

    [RelayCommand]
    private void ClearSelected()
    {
        if (SelectedTrack == null) return;
        SelectedTrack.SelectedSource = _notAssigned;
        UpdateAssignedStatus();
    }

    [RelayCommand]
    private void AssignSelected()
    {
        if (SelectedTrack == null) return;
        if (SelectedLibrarySource == null) return;
        AssignTrack(SelectedTrack, SelectedLibrarySource.Path);
    }

    public void AssignTrack(MusicPackTrackViewModel track, string? path)
    {
        if (track == null) return;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            track.SelectedSource = _notAssigned;
            UpdateAssignedStatus();
            return;
        }

        var match = SourceOptions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Path) && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        track.SelectedSource = match ?? new SourceFileOptionViewModel(path, Path.GetFileName(path));
        UpdateAssignedStatus();
    }

    [RelayCommand]
    private async Task ExportZip()
    {
        if (IsBusy) return;
        if (Tracks.Count == 0) return;

        if (_storageProvider == null)
        {
            Status = "StorageProvider unavailable.";
            return;
        }

        var suggested = ExportSlot1And2
            ? SanitizeFileName($"{PackName}_slot1+2.zip")
            : SanitizeFileName($"{PackName}_slot{Slot}.zip");
        var saveResult = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggested,
            DefaultExtension = "zip",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new FilePickerFileType("ZIP") { Patterns = new List<string> { "*.zip" } }
            }
        });

        var outputPath = saveResult?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var assignments = Tracks
            .Where(t => t.IsAssigned && !string.IsNullOrWhiteSpace(t.SourcePath))
            .ToDictionary(t => t.VanillaFilename, t => t.SourcePath!, StringComparer.OrdinalIgnoreCase);

        var packNamesByLang = UsePerLanguage ? BuildLangMap(PackNameEn, PackNameIt, PackNameGr, PackNameFr, PackNameSp) : null;
        var descByLang = UsePerLanguage ? BuildLangMap(DescriptionEn, DescriptionIt, DescriptionGr, DescriptionFr, DescriptionSp) : null;

        IsBusy = true;
        try
        {
            Status = "Exporting...";
            var prog = new Progress<MusicPackExportProgress>(p => Status = $"{p.Percent}% - {p.Message}");
            if (ExportSlot1And2)
            {
                var (slot1, slot2) = DeriveSlot1And2ZipPaths(outputPath);

                var request1 = new MusicPackExportRequest(1, slot1, PackName, Author, Description, InGameDescription, PackNameWidth, assignments, packNamesByLang, descByLang);
                var request2 = new MusicPackExportRequest(2, slot2, PackName, Author, Description, InGameDescription, PackNameWidth, assignments, packNamesByLang, descByLang);

                await _exporter.ExportAsync(request1, prog);
                await _exporter.ExportAsync(request2, prog);
                Status = $"Export complete.\n{Path.GetFileName(slot1)}\n{Path.GetFileName(slot2)}";
            }
            else
            {
                var request = new MusicPackExportRequest(Slot, outputPath, PackName, Author, Description, InGameDescription, PackNameWidth, assignments, packNamesByLang, descByLang);
                await _exporter.ExportAsync(request, prog);
                Status = "Export complete.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateAssignedStatus()
    {
        var assigned = Tracks.Count(t => t.IsAssigned);
        AssignedSummary = $"Assigned: {assigned} / {Tracks.Count}";
    }

    private static IReadOnlyDictionary<string, string> BuildLangMap(string en, string it, string gr, string fr, string sp)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = en ?? string.Empty,
            ["it"] = it ?? string.Empty,
            ["gr"] = gr ?? string.Empty,
            ["fr"] = fr ?? string.Empty,
            ["sp"] = sp ?? string.Empty,
        };
    }

    [RelayCommand]
    private async Task OpenMusicPack()
    {
        if (_storageProvider == null)
        {
            Status = "StorageProvider unavailable.";
            return;
        }

        var openResult = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Music Pack",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Music Pack") { Patterns = new List<string> { "*.zip", "*.json" } },
                new("ZIP") { Patterns = new List<string> { "*.zip" } },
                new("JSON") { Patterns = new List<string> { "*.json" } },
            }
        });

        var path = openResult?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Status = "Opening pack...";

            string json;
            if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), ".scdtoolkit_map.json", StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    Status = "No .scdtoolkit_map.json found in ZIP.";
                    return;
                }

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                json = await reader.ReadToEndAsync();
            }
            else
            {
                json = await File.ReadAllTextAsync(path);
            }

            ApplyMapJson(json);
        }
        catch (Exception ex)
        {
            Status = $"Open failed: {ex.Message}";
        }
    }

    private void ApplyMapJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("slot", out var slotEl) && slotEl.ValueKind == JsonValueKind.Number)
        {
            ExportSlot1And2 = false;
            Slot = slotEl.GetInt32();
        }

        if (root.TryGetProperty("mod_metadata", out var modMeta))
        {
            if (modMeta.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) PackName = t.GetString() ?? PackName;
            if (modMeta.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.String) Author = a.GetString() ?? Author;
            if (modMeta.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String) Description = d.GetString() ?? Description;
        }

        if (root.TryGetProperty("game_metadata", out var gameMeta))
        {
            if (gameMeta.TryGetProperty("name_width", out var w) && w.ValueKind == JsonValueKind.Number) PackNameWidth = w.GetInt32();
        }

        if (root.TryGetProperty("sys_languages", out var sysLang) && sysLang.ValueKind == JsonValueKind.Object)
        {
            if (sysLang.TryGetProperty("pack_name", out var names) && names.ValueKind == JsonValueKind.Object)
            {
                UsePerLanguage = true;
                PackNameEn = names.TryGetProperty("en", out var vEn) ? (vEn.GetString() ?? "") : PackName;
                PackNameIt = names.TryGetProperty("it", out var vIt) ? (vIt.GetString() ?? "") : PackName;
                PackNameGr = names.TryGetProperty("gr", out var vGr) ? (vGr.GetString() ?? "") : PackName;
                PackNameFr = names.TryGetProperty("fr", out var vFr) ? (vFr.GetString() ?? "") : PackName;
                PackNameSp = names.TryGetProperty("sp", out var vSp) ? (vSp.GetString() ?? "") : PackName;
            }

            if (sysLang.TryGetProperty("description", out var descs) && descs.ValueKind == JsonValueKind.Object)
            {
                UsePerLanguage = true;
                DescriptionEn = descs.TryGetProperty("en", out var vEn) ? (vEn.GetString() ?? "") : Description;
                DescriptionIt = descs.TryGetProperty("it", out var vIt) ? (vIt.GetString() ?? "") : Description;
                DescriptionGr = descs.TryGetProperty("gr", out var vGr) ? (vGr.GetString() ?? "") : Description;
                DescriptionFr = descs.TryGetProperty("fr", out var vFr) ? (vFr.GetString() ?? "") : Description;
                DescriptionSp = descs.TryGetProperty("sp", out var vSp) ? (vSp.GetString() ?? "") : Description;
            }
        }

        var missing = new List<string>();
        if (root.TryGetProperty("tracks", out var tracksEl) && tracksEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tracksEl.EnumerateArray())
            {
                if (!t.TryGetProperty("vanilla_filename", out var vfn) || vfn.ValueKind != JsonValueKind.String) continue;
                if (!t.TryGetProperty("source_path", out var sp) || sp.ValueKind != JsonValueKind.String) continue;

                var vanilla = vfn.GetString() ?? "";
                var sourcePath = sp.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(vanilla)) continue;

                var track = Tracks.FirstOrDefault(x => string.Equals(x.VanillaFilename, vanilla, StringComparison.OrdinalIgnoreCase));
                if (track == null) continue;

                if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
                {
                    AssignTrack(track, sourcePath);
                }
                else
                {
                    track.SelectedSource = _notAssigned;
                    if (!string.IsNullOrWhiteSpace(sourcePath)) missing.Add(Path.GetFileName(sourcePath));
                }
            }
        }

        UpdateAssignedStatus();
        TrackListSummary = $"Loaded {Tracks.Count} tracks.";
        Status = missing.Count > 0
            ? $"Opened pack. {missing.Count} source files missing on this PC."
            : "Opened pack.";
    }

    private static (string slot1, string slot2) DeriveSlot1And2ZipPaths(string baseZipPath)
    {
        var dir = Path.GetDirectoryName(baseZipPath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(baseZipPath);
        var ext = Path.GetExtension(baseZipPath);
        if (!string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            ext = ".zip";
        }

        // Special-case "slot1+2" style names so we don't produce "slot2+2".
        var slot1Name = name;
        var slot2Name = name;

        var comboTokens = new[] { "slot1+2", "slot1&2", "slot1_2", "slot1-2", "slot1and2" };
        var comboToken = comboTokens.FirstOrDefault(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
        if (comboToken != null)
        {
            slot1Name = ReplaceIgnoreCase(name, comboToken, "slot1");
            slot2Name = ReplaceIgnoreCase(name, comboToken, "slot2");
        }
        else if (name.Contains("slot1", StringComparison.OrdinalIgnoreCase))
        {
            slot2Name = ReplaceIgnoreCase(name, "slot1", "slot2");
        }
        else if (name.Contains("slot2", StringComparison.OrdinalIgnoreCase))
        {
            slot1Name = ReplaceIgnoreCase(name, "slot2", "slot1");
        }
        else
        {
            slot1Name = name + "_slot1";
            slot2Name = name + "_slot2";
        }

        return (Path.Combine(dir, slot1Name + ext), Path.Combine(dir, slot2Name + ext));
    }

    private static string ReplaceIgnoreCase(string text, string search, string replace)
    {
        var idx = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return text;
        return text.Substring(0, idx) + replace + text.Substring(idx + search.Length);
    }

    // StorageProvider is injected by the view via AttachStorageProvider().

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
