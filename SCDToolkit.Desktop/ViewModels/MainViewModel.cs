using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCDToolkit.Core.Abstractions;
using SCDToolkit.Core.Models;
using SCDToolkit.Core.Services;
using SCDToolkit.Desktop.Services;
using System.IO;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ILibraryService _libraryService;
        private readonly IPlaybackService _playbackService;
        private readonly IFolderPickerService _folderPicker;
        private readonly Kh2HookService _kh2HookService = new();

        private CancellationTokenSource? _kh2WarmupCts;
        private Task? _kh2WarmupTask;
        private List<LibraryItem> _lastScan = new();
        private readonly DispatcherTimer _tick;
        private bool _isSeeking;
        private int _currentIndex = -1;
        private string? _currentPlayingPath;
        private DateTime _lastKh2StatusCheckUtc = DateTime.MinValue;

        private int _autoUpdateCheckQueued;

        private bool _suppressConfigSave;

        public bool IsSeeking
        {
            get => _isSeeking;
            set => _isSeeking = value;
        }

        [ObservableProperty]
        private ObservableCollection<LibraryItemViewModel> libraryItems = new();

        [ObservableProperty]
        private ObservableCollection<LibraryItemViewModel> selectedItems = new();

        public bool HasAnySelection => SelectedItems.Count > 0;

        [ObservableProperty]
        private ObservableCollection<FolderGroupViewModel> folderGroups = new();

        [ObservableProperty]
        private ObservableCollection<LibraryTreeNodeViewModel> folderTreeRoots = new();

        [ObservableProperty]
        private LibraryItemViewModel? selectedItem;

        public bool HasSelectedItem => SelectedItem != null;

        [ObservableProperty]
        private ObservableCollection<FileTypeFilterOptionViewModel> fileTypeFilters = new();

        public string FileTypeFilterSummary
        {
            get
            {
                if (FileTypeFilters.Count == 0) return "All";
                var checkedCount = FileTypeFilters.Count(f => f.IsChecked);
                if (checkedCount == 0) return "None";
                if (checkedCount == FileTypeFilters.Count) return "All";
                return string.Join(", ", FileTypeFilters.Where(f => f.IsChecked).Select(f => f.DisplayName));
            }
        }

        partial void OnSelectedItemChanged(LibraryItemViewModel? value)
        {
            if (value != null)
            {
                _currentIndex = LibraryItems.IndexOf(value);

                if (!string.IsNullOrEmpty(_currentPlayingPath) && string.Equals(value.Path, _currentPlayingPath, StringComparison.OrdinalIgnoreCase))
                {
                    LoopStart = value.LoopStart;
                    LoopEnd = value.LoopEnd;
                }
            }
            UpdateLoopSeconds();
            OnPropertyChanged(nameof(CanConvertToScd));
            OnPropertyChanged(nameof(CanConvertToWav));
            OnPropertyChanged(nameof(HasSelectedItem));
        }

        partial void OnSelectedItemsChanged(ObservableCollection<LibraryItemViewModel> value)
        {
            OnPropertyChanged(nameof(HasAnySelection));
        }

        partial void OnLoopStartChanged(int value) => UpdateLoopSeconds();
        partial void OnLoopEndChanged(int value) => UpdateLoopSeconds();
        partial void OnSampleRateChanged(int value) => UpdateLoopSeconds();

        [ObservableProperty]
        private int loopStart;

        [ObservableProperty]
        private int loopEnd;

        [ObservableProperty]
        private double loopStartSeconds;

        [ObservableProperty]
        private double loopEndSeconds;
        [ObservableProperty]
        private bool loopEnabled = true;
        partial void OnLoopEnabledChanged(bool value)
        {
            // Update loop state dynamically without reloading
            _playbackService.LoopEnabled = value;
        }

        [ObservableProperty]
        private ObservableCollection<string> scanFolders = new();

        [ObservableProperty]
        private string? selectedScanFolder;

        [ObservableProperty]
        private bool scanSubdirectories = true;

        partial void OnScanSubdirectoriesChanged(bool value)
        {
            SaveConfig();
        }

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private bool groupByFolder = true;

        partial void OnGroupByFolderChanged(bool value)
        {
            ApplyFilter();
        }

        [ObservableProperty]
        private string nowPlayingTitle = "No file loaded";

        [ObservableProperty]
        private string nowPlayingInfo = string.Empty;

        [ObservableProperty]
        private string audioFormat = "Unknown";

        [ObservableProperty]
        private int sampleRate;

        [ObservableProperty]
        private int channels;

        [ObservableProperty]
        private string loopInfo = "No loop";

        [ObservableProperty]
        private double positionSeconds;

        [ObservableProperty]
        private double durationSeconds;

        [ObservableProperty]
        private string positionText = "00:00";

        [ObservableProperty]
        private string durationText = "00:00";

        [ObservableProperty]
        private double volume = 0.7;

        [ObservableProperty]
        private bool isPlaying;

        [ObservableProperty]
        private string kh2HookStatus = "KH2 Hook: Waiting for KH2...";

        [ObservableProperty]
        private bool isScdHookConnected;

        public IBrush ScdHookIndicatorBrush => IsScdHookConnected ? Brushes.LimeGreen : Brushes.Orange;

        [ObservableProperty]
        private string scdHookQueuedFieldName = "(none)";

        [ObservableProperty]
        private string scdHookQueuedBattleName = "(none)";

        [ObservableProperty]
        private string? scdHookQueuedField;

        [ObservableProperty]
        private string? scdHookQueuedBattle;

        [ObservableProperty]
        private string? scdHookLoadedField;

        [ObservableProperty]
        private string? scdHookLoadedBattle;

        [ObservableProperty]
        private string? khRandoMusicFolder;

        [ObservableProperty]
        private bool showKhRando;

        public double KhRandoPanelWidth => ShowKhRando ? 360 : 0;

        [ObservableProperty]
        private string khRandoStatus = "KH Rando: Not set";

        [ObservableProperty]
        private bool isRescanning;

        [ObservableProperty]
        private ObservableCollection<RandoCategoryViewModel> khRandoCategories = new();

        [ObservableProperty]
        private RandoCategoryViewModel? selectedKhRandoCategory;

        [ObservableProperty]
        private ObservableCollection<RandoFolderGroupViewModel> khRandoFolderGroups = new();

        [ObservableProperty]
        private RandoFolderGroupViewModel? selectedKhRandoFolderGroup;

        public bool CanConvertToScd => SelectedItem?.Path != null && !SelectedItem.Path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase);
        public bool CanConvertToWav => SelectedItem?.Path != null && !SelectedItem.Path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        [ObservableProperty]
        private string playPauseGlyph = "▶";

        [ObservableProperty]
        private string playPauseIconData = "M8,5 L8,19 L19,12 Z";

        public MainViewModel()
            : this(new FileSystemLibraryService(), new StubPlaybackService(), new NoopFolderPickerService())
        {
        }

        public MainViewModel(ILibraryService libraryService, IPlaybackService playbackService, IFolderPickerService folderPicker)
        {
            _libraryService = libraryService;
            _playbackService = playbackService;
            _folderPicker = folderPicker;
            _playbackService.Volume = Volume;

            _tick = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _tick.Tick += (_, _) =>
            {
                RefreshTransport();
                RefreshKh2HookStatusThrottled();
            };
            _tick.Start();
        }

        partial void OnIsScdHookConnectedChanged(bool value)
        {
            OnPropertyChanged(nameof(ScdHookIndicatorBrush));
        }

        partial void OnShowKhRandoChanged(bool value)
        {
            OnPropertyChanged(nameof(KhRandoPanelWidth));
            SaveConfig();
        }

        public async Task ApplyConfigAsync(AppConfig config)
        {
            _suppressConfigSave = true;
            try
            {
                if (config.LibraryFolders != null)
                {
                    ScanFolders.Clear();
                    foreach (var folder in config.LibraryFolders)
                    {
                        if (!string.IsNullOrWhiteSpace(folder))
                        {
                            ScanFolders.Add(folder);
                        }
                    }
                }

                if (config.ScanSubdirs.HasValue)
                {
                    ScanSubdirectories = config.ScanSubdirs.Value;
                }

                if (config.Volume.HasValue)
                {
                    var vol = Math.Clamp(config.Volume.Value / 100.0, 0.0, 1.0);
                    Volume = vol;
                }

                if (!string.IsNullOrWhiteSpace(config.KhRandoMusicFolder))
                {
                    KhRandoMusicFolder = config.KhRandoMusicFolder;
                    RefreshKhRandoState();
                }

                // Auto-rescan to restore library on startup
                if (ScanFolders.Any())
                {
                    await Rescan();
                }
            }
            finally
            {
                _suppressConfigSave = false;
            }
        }

        public void StartKh2HookWarmup()
        {
            if (_kh2WarmupTask != null && !_kh2WarmupTask.IsCompleted)
            {
                return;
            }

            _kh2WarmupCts?.Cancel();
            _kh2WarmupCts = new CancellationTokenSource();
            var ct = _kh2WarmupCts.Token;

            _kh2WarmupTask = Task.Run(async () =>
            {
                var delayMs = 750;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var ok = _kh2HookService.TryWarmupPointersMarkerOnly(out var message);
                        Dispatcher.UIThread.Post(() =>
                        {
                            // Keep the main indicator fresh.
                            RefreshScdHookStatus();
                            if (ok)
                            {
                                // Non-invasive status update; do not overwrite user-visible errors.
                                if (string.IsNullOrWhiteSpace(Kh2HookStatus) || Kh2HookStatus.StartsWith("SCDHook:", StringComparison.OrdinalIgnoreCase))
                                {
                                    Kh2HookStatus = "SCDHook: Ready.";
                                }
                            }
                            else
                            {
                                // Only surface warmup status if nothing else is showing.
                                if (string.IsNullOrWhiteSpace(Kh2HookStatus))
                                {
                                    Kh2HookStatus = $"SCDHook: {message}";
                                }
                            }
                        });

                        if (ok)
                        {
                            // Once pointers are resolved, we can check less frequently.
                            delayMs = 4000;
                        }
                        else
                        {
                            // Exponential-ish backoff while waiting for process/markers.
                            delayMs = Math.Min(4000, (int)(delayMs * 1.3));
                        }
                    }
                    catch
                    {
                        delayMs = 2000;
                    }

                    try
                    {
                        await Task.Delay(delayMs, ct);
                    }
                    catch
                    {
                        // canceled
                    }
                }
            }, ct);
        }

        public void StopKh2HookWarmup()
        {
            try { _kh2WarmupCts?.Cancel(); } catch { }
        }

        [RelayCommand]
        private async Task ImportLegacyConfig()
        {
            var window = GetMainWindow();
            if (window?.StorageProvider == null)
            {
                await ShowOkAsync("Import Legacy Config", "StorageProvider unavailable.");
                return;
            }

            var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import legacy SCDToolkit config.json",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("JSON") { Patterns = new List<string> { "*.json" } },
                    new("All") { Patterns = new List<string> { "*" } }
                }
            });

            var selected = result?.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            if (!Services.ConfigLoader.ReplaceConfigFromFile(selected))
            {
                await ShowOkAsync("Import Legacy Config", "Failed to replace the current config.");
                return;
            }

            // Reload and apply immediately.
            var loaded = Services.ConfigLoader.Load();
            await ApplyConfigAsync(loaded);
            await Rescan();
            await ShowOkAsync("Import Legacy Config", "Imported and reloaded config.");
        }

        [RelayCommand]
        private void OpenHelpGuide()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var docPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "doc", "KH2_SCD_Format.md"));
                if (!File.Exists(docPath))
                {
                    docPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "SCDToolkit_PYTHON_ORIG_SOURCE", "README.md"));
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = docPath,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Best-effort.
            }
        }

        [RelayCommand]
        private async Task CheckForUpdates()
        {
            var owner = GetMainWindow();
            if (owner == null)
            {
                return;
            }

            await CheckAndMaybeUpdateAsync(showUpToDateMessage: true, closeOwnerIfUpdating: true);
        }

        public void QueueAutoUpdateCheck()
        {
            // Only run once per app lifetime.
            if (Interlocked.Exchange(ref _autoUpdateCheckQueued, 1) == 1)
            {
                return;
            }

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await CheckAndMaybeUpdateAsync(showUpToDateMessage: false, closeOwnerIfUpdating: true);
                }
                catch
                {
                    // Best effort, never block startup.
                }
            }, DispatcherPriority.Background);
        }

        private async Task CheckAndMaybeUpdateAsync(bool showUpToDateMessage, bool closeOwnerIfUpdating)
        {
            var owner = GetMainWindow();
            if (owner == null)
            {
                return;
            }

            try
            {
                var service = new Services.UpdateService();
                var info = await service.GetUpdateInfoAsync();

                if (!info.IsUpdateAvailable)
                {
                    if (showUpToDateMessage)
                    {
                        await ShowOkAsync("Update", info.Message);
                    }
                    return;
                }

                var proceed = await ConfirmAsync(
                    title: "Update Available",
                    message: $"{info.Message}\n\nDownload and install now?\n\nThe app will close to apply the update.",
                    okText: "Update",
                    isDanger: false);

                if (!proceed)
                {
                    return;
                }

                var currentExe = Environment.ProcessPath ?? string.Empty;
                var zipPath = await DownloadUpdateWithProgressAsync(owner, service, info);
                if (string.IsNullOrWhiteSpace(zipPath))
                {
                    return;
                }

                var (started, message) = service.TryStartUpdaterFromZip(currentExe, zipPath);
                if (!started)
                {
                    await ShowOkAsync("Update", message);
                    return;
                }

                if (closeOwnerIfUpdating)
                {
                    try
                    {
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                        {
                            lifetime.Shutdown();
                        }
                        else
                        {
                            owner.Close();
                        }
                    }
                    catch
                    {
                        owner.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                if (showUpToDateMessage)
                {
                    await ShowOkAsync("Update", ex.Message);
                }
            }
        }

        private async Task<string?> DownloadUpdateWithProgressAsync(Window owner, Services.UpdateService service, Services.UpdateService.UpdateInfo info)
        {
            var statusText = new TextBlock
            {
                Text = "Downloading update...",
                TextWrapping = TextWrapping.Wrap
            };

            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 18,
                IsIndeterminate = true
            };

            var cancel = new Button { Content = "Cancel", MinWidth = 90 };
            using var cts = new CancellationTokenSource();

            var dialog = new Window
            {
                Width = 520,
                Height = 190,
                Title = "Downloading Update",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner.Background,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        statusText,
                        progressBar,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { cancel }
                        }
                    }
                }
            };

            cancel.Click += (_, _) =>
            {
                try { cts.Cancel(); } catch { }
                try { dialog.Close(); } catch { }
            };

            // Show the dialog without blocking the async download.
            _ = dialog.ShowDialog(owner);

            try
            {
                var progress = new Progress<Services.UpdateService.DownloadProgress>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (p.TotalBytes.HasValue && p.TotalBytes.Value > 0)
                        {
                            progressBar.IsIndeterminate = false;
                            var pct = (double)p.BytesReceived / p.TotalBytes.Value * 100.0;
                            progressBar.Value = Math.Clamp(pct, 0, 100);
                            statusText.Text = $"Downloading update... {FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes.Value)}";
                        }
                        else
                        {
                            progressBar.IsIndeterminate = true;
                            statusText.Text = $"Downloading update... {FormatBytes(p.BytesReceived)}";
                        }
                    });
                });

                var zipPath = await service.DownloadUpdateZipAsync(info.ZipUrl, info.Tag, progress, cts.Token);
                try { dialog.Close(); } catch { }
                return zipPath;
            }
            catch (OperationCanceledException)
            {
                await ShowOkAsync("Update", "Update download canceled.");
                return null;
            }
            catch (Exception ex)
            {
                try { dialog.Close(); } catch { }
                await ShowOkAsync("Update", ex.Message);
                return null;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            var kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0.0} KB";
            var mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.0} MB";
            var gb = mb / 1024.0;
            return $"{gb:0.00} GB";
        }

        [RelayCommand]
        private async Task ViewLogFile()
        {
            var logPath = Services.TraceLog.GetLogFilePath();
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            {
                await ShowOkAsync("Log", "No log file found yet.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = logPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                await ShowOkAsync("Log", ex.Message);
            }
        }

        [RelayCommand]
        private Task OpenLogFile()
        {
            return ViewLogFile();
        }

        [RelayCommand]
        private void OpenKofi()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/skylect", UseShellExecute = true });
            }
            catch { }
        }

        [RelayCommand]
        private void OpenDiscord()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/FqePtT2BBM", UseShellExecute = true });
            }
            catch { }
        }

        private void SaveConfig()
        {
            if (_suppressConfigSave)
            {
                return;
            }

            // Merge with existing config so we don't wipe fields managed by other systems
            // (e.g., normalization_cache).
            var existing = ConfigLoader.Load();
            existing.LibraryFolders = ScanFolders.ToList();
            existing.ScanSubdirs = ScanSubdirectories;
            existing.Volume = Volume * 100.0;
            existing.KhRandoMusicFolder = KhRandoMusicFolder;
            existing.ShowKhRando = ShowKhRando;

            ConfigLoader.Save(existing);
        }

        [RelayCommand]
        private void ToggleKhRando()
        {
            ShowKhRando = !ShowKhRando;
        }

        [RelayCommand]
        private void Exit()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        [RelayCommand]
        private async Task ShowAbout()
        {
            await ShowOkAsync(
                "About",
                "SCD Toolkit\n\nLibrary management, loop editing, normalization, and KH2 SCDHook utilities.");
        }

        [RelayCommand]
        private async Task AddRandoFolder()
        {
            var picked = await _folderPicker.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            KhRandoMusicFolder = picked;
            RefreshKhRandoState();
            SaveConfig();
        }

        [RelayCommand]
        private void ClearRandoFolder()
        {
            KhRandoMusicFolder = null;
            KhRandoCategories.Clear();
            SelectedKhRandoCategory = null;
            KhRandoStatus = "KH Rando: Not set";
            SaveConfig();
        }

        [RelayCommand]
        private void ExportSelectedToRando()
        {
            if (SelectedItem?.Path == null)
            {
                KhRandoStatus = "KH Rando: Select an item first.";
                return;
            }

            var destFolder = SelectedKhRandoFolderGroup?.FolderPath ?? SelectedKhRandoCategory?.FolderPath;
            if (string.IsNullOrWhiteSpace(destFolder))
            {
                KhRandoStatus = "KH Rando: Select a destination folder.";
                return;
            }

            if (!SelectedItem.Path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                KhRandoStatus = "KH Rando: Only .scd export is supported.";
                return;
            }

            try
            {
                var sourcePath = Path.GetFullPath(SelectedItem.Path);
                Directory.CreateDirectory(destFolder);
                var destPath = Path.Combine(destFolder, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destPath, overwrite: true);
                var destName = SelectedKhRandoFolderGroup?.DisplayName ?? SelectedKhRandoCategory?.DisplayName ?? Path.GetFileName(destFolder);
                KhRandoStatus = $"KH Rando: Exported {Path.GetFileName(sourcePath)} → {destName}";
            }
            catch (Exception ex)
            {
                KhRandoStatus = $"KH Rando: Export failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void SelectKhRandoFolder(RandoFolderGroupViewModel? folder)
        {
            if (folder == null) return;
            SelectedKhRandoFolderGroup = folder;
        }

        partial void OnSelectedKhRandoFolderGroupChanged(RandoFolderGroupViewModel? value)
        {
            foreach (var g in KhRandoFolderGroups)
            {
                g.IsSelected = ReferenceEquals(g, value);
            }
        }

        private void RefreshKhRandoState()
        {
            KhRandoCategories.Clear();
            SelectedKhRandoCategory = null;
            KhRandoFolderGroups.Clear();
            SelectedKhRandoFolderGroup = null;

            if (string.IsNullOrWhiteSpace(KhRandoMusicFolder))
            {
                KhRandoStatus = "KH Rando: Not set";
                return;
            }

            var musicFolder = Path.GetFullPath(KhRandoMusicFolder);
            if (!Directory.Exists(musicFolder))
            {
                KhRandoStatus = "KH Rando: Folder not found";
                return;
            }

            // Detect categories (subdirectories)
            var dirs = Directory.GetDirectories(musicFolder)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var dir in dirs)
            {
                KhRandoCategories.Add(new RandoCategoryViewModel
                {
                    FolderPath = dir,
                    DisplayName = Path.GetFileName(dir) ?? dir
                });

                var group = new RandoFolderGroupViewModel
                {
                    FolderPath = dir,
                    DisplayName = Path.GetFileName(dir) ?? dir,
                    IsExpanded = false,
                    IsSelected = false
                };

                try
                {
                    var files = Directory.GetFiles(dir, "*.scd", SearchOption.TopDirectoryOnly)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                    foreach (var f in files)
                    {
                        group.Files.Add(Path.GetFileName(f) ?? f);
                    }
                }
                catch
                {
                    // Best-effort only.
                }

                KhRandoFolderGroups.Add(group);
            }

            var parent = Directory.GetParent(musicFolder)?.FullName;
            var musiclistPath = parent == null ? null : Path.Combine(parent, "musiclist.json");
            var hasMusiclist = musiclistPath != null && File.Exists(musiclistPath);

            var folderName = Path.GetFileName(musicFolder) ?? musicFolder;
            KhRandoStatus = hasMusiclist
                ? $"KH Rando: {folderName} (musiclist.json found)"
                : $"KH Rando: {folderName} (musiclist.json missing)";
        }

        [RelayCommand]
        private async Task AddFolder()
        {
            var picked = await _folderPicker.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            if (!ScanFolders.Contains(picked))
            {
                ScanFolders.Add(picked);
            }

            SaveConfig();
            await Rescan();
        }

        [RelayCommand]
        private async Task RemoveFolder()
        {
            if (!string.IsNullOrWhiteSpace(SelectedScanFolder) && ScanFolders.Contains(SelectedScanFolder))
            {
                ScanFolders.Remove(SelectedScanFolder);
                SelectedScanFolder = null;
                SaveConfig();
                await Rescan();
            }
            else if (ScanFolders.Any())
            {
                // Fallback: remove last if nothing selected
                ScanFolders.RemoveAt(ScanFolders.Count - 1);
                SaveConfig();
                await Rescan();
            }
        }

        [RelayCommand]
        private async Task Rescan()
        {
            if (IsRescanning)
            {
                return;
            }
            if (!ScanFolders.Any())
            {
                return;
            }

            try
            {
                IsRescanning = true;
                _lastScan = (await _libraryService.ScanAsync(ScanFolders.ToList(), ScanSubdirectories)).ToList();

                RefreshFileTypeFilters();
                ApplyFilter();
            }
            finally
            {
                IsRescanning = false;
            }
        }

        private void RefreshFileTypeFilters()
        {
            // Preserve existing checked states.
            var existing = FileTypeFilters.ToDictionary(f => f.Extension, f => f.IsChecked, StringComparer.OrdinalIgnoreCase);

            var extCounts = _lastScan
                .Select(i => Path.GetExtension(i.Path) ?? string.Empty)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .GroupBy(e => e.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count());

            var options = extCounts
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new FileTypeFilterOptionViewModel
                {
                    Extension = kv.Key,
                    DisplayName = kv.Key.TrimStart('.').ToUpperInvariant(),
                    Count = kv.Value,
                    IsChecked = existing.TryGetValue(kv.Key, out var prev) ? prev : true
                })
                .ToList();

            FileTypeFilters = new ObservableCollection<FileTypeFilterOptionViewModel>(options);

            foreach (var opt in FileTypeFilters)
            {
                opt.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FileTypeFilterOptionViewModel.IsChecked))
                    {
                        OnPropertyChanged(nameof(FileTypeFilterSummary));
                        ApplyFilter();
                    }
                };
            }

            OnPropertyChanged(nameof(FileTypeFilterSummary));
        }

        [RelayCommand]
        private void LoadFile()
        {
            // Placeholder: future file picker and decode hook.
        }

        [RelayCommand]
        private async Task Play()
        {
            if (SelectedItem == null)
            {
                return;
            }
            var model = SelectedItem.ToModel();
            _currentPlayingPath = model.Path;
            
            // For SCD files, we'll get accurate loop points after decoding from vgmstream
            // For now, pass the model's loop points (will be overridden for SCD)
            var loop = (LoopEnabled && model.LoopPoints != null) 
                ? model.LoopPoints 
                : new LoopPoints(0, 0);
            await _playbackService.PlayAsync(model, loop);
            NowPlayingTitle = System.IO.Path.GetFileNameWithoutExtension(model.DisplayName);
            NowPlayingInfo = model.Details;
            
            // Update detailed info
            var ext = System.IO.Path.GetExtension(model.Path).ToUpperInvariant().TrimStart('.');
            AudioFormat = ext;
            
            // Try to get metadata from decoded file (for SCD) or from original file (for WAV)
            bool metadataFound = false;
            LoopPoints? actualLoopPoints = model.LoopPoints;
            
            if (ext == "SCD" && _playbackService is VgmstreamPlaybackService vgmService)
            {
                // For SCD files, get accurate loop points from vgmstream output
                var vgmLoopInfo = vgmService.GetVgmstreamLoopInfo();
                if (vgmLoopInfo != null)
                {
                    actualLoopPoints = vgmLoopInfo;
                    // Update the playback with accurate loop points
                    if (LoopEnabled)
                    {
                        await _playbackService.PlayAsync(model, actualLoopPoints);
                    }
                }
                
                var decodedPath = vgmService.GetDecodedPath();
                if (!string.IsNullOrEmpty(decodedPath) && System.IO.File.Exists(decodedPath))
                {
                    var (_, wavMeta) = WavLoopTagReader.Read(decodedPath);
                    if (wavMeta != null && wavMeta.SampleRate > 0)
                    {
                        SampleRate = wavMeta.SampleRate;
                        Channels = wavMeta.Channels;
                        metadataFound = true;
                    }
                }
            }
            else if (ext == "WAV")
            {
                // For WAV files, read directly from the file
                var (_, wavMeta) = WavLoopTagReader.Read(model.Path);
                if (wavMeta != null && wavMeta.SampleRate > 0)
                {
                    SampleRate = wavMeta.SampleRate;
                    Channels = wavMeta.Channels;
                    metadataFound = true;
                }
            }
            
            // Fallback to model metadata if we didn't get it from file
            if (!metadataFound)
            {
                if (model.Metadata != null && model.Metadata.SampleRate > 0)
                {
                    SampleRate = model.Metadata.SampleRate;
                    Channels = model.Metadata.Channels;
                }
                else
                {
                    SampleRate = 0;
                    Channels = 0;
                }
            }
            
            if (actualLoopPoints != null && actualLoopPoints.EndSample > actualLoopPoints.StartSample)
            {
                LoopStart = actualLoopPoints.StartSample;
                LoopEnd = actualLoopPoints.EndSample;
                if (SelectedItem != null)
                {
                    SelectedItem.LoopStart = actualLoopPoints.StartSample;
                    SelectedItem.LoopEnd = actualLoopPoints.EndSample;
                }
                UpdateLoopSeconds();

                var loopStartSec = SampleRate > 0 ? actualLoopPoints.StartSample / (double)SampleRate : 0;
                var loopEndSec = SampleRate > 0 ? actualLoopPoints.EndSample / (double)SampleRate : 0;
                LoopInfo = $"{actualLoopPoints.StartSample:N0} → {actualLoopPoints.EndSample:N0} ({FormatTime(loopStartSec)} → {FormatTime(loopEndSec)})";
            }
            else
            {
                LoopStart = 0;
                LoopEnd = 0;
                if (SelectedItem != null)
                {
                    SelectedItem.LoopStart = 0;
                    SelectedItem.LoopEnd = 0;
                }
                UpdateLoopSeconds();
                LoopInfo = "No loop";
            }
            
            RefreshTransport();
        }

        [RelayCommand]
        private async Task TogglePlayPause()
        {
            if (IsPlaying)
            {
                await Pause();
            }
            else
            {
                // If we're paused on the currently loaded track, resume instead of re-decoding/restarting.
                if (SelectedItem != null
                    && !string.IsNullOrWhiteSpace(_currentPlayingPath)
                    && string.Equals(SelectedItem.Path, _currentPlayingPath, StringComparison.OrdinalIgnoreCase)
                    && _playbackService is SCDToolkit.Desktop.Services.VgmstreamPlaybackService vgmService)
                {
                    await vgmService.ResumeAsync();
                    RefreshTransport();
                    return;
                }

                await Play();
            }
        }

        [RelayCommand]
        private Task Pause()
        {
            return _playbackService.PauseAsync();
        }

        public void BeginSeek()
        {
            IsSeeking = true;
            _tick.IsEnabled = false;
        }

        public async Task EndSeekAsync(double seconds)
        {
            await _playbackService.SeekAsync(TimeSpan.FromSeconds(seconds));
            IsSeeking = false;
            _tick.IsEnabled = true;
        }

        [RelayCommand]
        private async Task Prev()
        {
            var allItems = GetAllItemsInOrder();
            if (allItems.Count == 0) return;
            
            var currentIdx = SelectedItem != null ? allItems.IndexOf(SelectedItem) : 0;
            if (currentIdx < 0) currentIdx = 0;
            
            currentIdx--;
            if (currentIdx < 0) currentIdx = allItems.Count - 1;
            
            SelectedItem = allItems[currentIdx];
            await Play();
        }

        [RelayCommand]
        private async Task Next()
        {
            var allItems = GetAllItemsInOrder();
            if (allItems.Count == 0) return;
            
            var currentIdx = SelectedItem != null ? allItems.IndexOf(SelectedItem) : 0;
            if (currentIdx < 0) currentIdx = 0;
            
            currentIdx++;
            if (currentIdx >= allItems.Count) currentIdx = 0;
            
            SelectedItem = allItems[currentIdx];
            await Play();
        }

        private List<LibraryItemViewModel> GetAllItemsInOrder()
        {
            if (GroupByFolder && FolderTreeRoots.Count > 0)
            {
                var list = new List<LibraryItemViewModel>();
                foreach (var root in FolderTreeRoots)
                {
                    FlattenLeafItems(root, list);
                }
                return list;
            }

            return LibraryItems.ToList();
        }

        private static void FlattenLeafItems(LibraryTreeNodeViewModel node, List<LibraryItemViewModel> output)
        {
            foreach (var child in node.Children)
            {
                if (child is LibraryItemViewModel item)
                {
                    output.Add(item);
                }
                else if (child is LibraryTreeNodeViewModel folder)
                {
                    FlattenLeafItems(folder, output);
                }
            }
        }

        [RelayCommand]
        private Task Stop()
        {
            return _playbackService.StopAsync();
        }

        [RelayCommand]
        private async Task DeleteSelected()
        {
            if (SelectedItem == null) return;
            if (string.IsNullOrWhiteSpace(SelectedItem.Path)) return;

            var confirm = await ConfirmAsync($"Delete '{SelectedItem.DisplayName}'?");
            if (!confirm) return;

            var filePath = SelectedItem.Path;
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return;

            try
            {
                // Send to recycle bin using Windows API
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    filePath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                // Refresh library
                await Rescan();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting file: {ex.Message}");
            }
        }

        private async Task<bool> ConfirmAsync(string message)
        {
            return await ConfirmAsync(title: "Confirm", message: message, okText: "Delete", isDanger: true);
        }

        private async Task<bool> ConfirmAsync(string title, string message, string okText, bool isDanger)
        {
            var owner = GetMainWindow();

            var tcs = new TaskCompletionSource<bool>();
            var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
            var okButton = new Button { Content = okText, MinWidth = 80 };
            if (isDanger)
            {
                okButton.Background = Avalonia.Media.Brushes.IndianRed;
                okButton.Foreground = Avalonia.Media.Brushes.White;
            }

            var dialog = new Window
            {
                Width = 360,
                Height = 180,
                Title = title,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner?.Background,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                cancelButton,
                                okButton
                            }
                        }
                    }
                }
            };

            cancelButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
            okButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
            dialog.Closed += (_, _) => tcs.TrySetResult(false);

            if (owner != null)
            {
                _ = dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
            return await tcs.Task;
        }

        private async Task ShowOkAsync(string title, string message)
        {
            var owner = GetMainWindow();

            var tcs = new TaskCompletionSource<bool>();
            var okButton = new Button { Content = "OK", MinWidth = 80 };

            var dialog = new Window
            {
                Width = 460,
                Height = 200,
                Title = title,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner?.Background,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { okButton }
                        }
                    }
                }
            };

            okButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
            dialog.Closed += (_, _) => tcs.TrySetResult(true);

            if (owner != null)
            {
                _ = dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }

            await tcs.Task;
        }

        private enum QuickNormalizeMode
        {
            Full,
            FloatPatchOnly
        }

        private async Task<QuickNormalizeMode?> PromptQuickNormalizeModeAsync(string fileName)
        {
            var owner = GetMainWindow();

            var tcs = new TaskCompletionSource<QuickNormalizeMode?>();
            var fullButton = new Button { Content = "Full Normalize (-12 LUFS + float patch)", MinWidth = 260 };
            var floatButton = new Button { Content = "Float patch only (quick)", MinWidth = 260 };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 100 };

            var dialog = new Window
            {
                Width = 520,
                Height = 240,
                Title = "Normalize",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner?.Background,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Normalize '{fileName}'\n\nChoose a method:",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Spacing = 8,
                            Children = { fullButton, floatButton }
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { cancelButton }
                        }
                    }
                }
            };

            fullButton.Click += (_, _) => { tcs.TrySetResult(QuickNormalizeMode.Full); dialog.Close(); };
            floatButton.Click += (_, _) => { tcs.TrySetResult(QuickNormalizeMode.FloatPatchOnly); dialog.Close(); };
            cancelButton.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
            dialog.Closed += (_, _) => tcs.TrySetResult(null);

            if (owner != null)
            {
                _ = dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }

            return await tcs.Task;
        }

        private static Window? GetMainWindow()
        {
            return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }

        [RelayCommand]
        private void OpenFileLocationItem(LibraryItemViewModel? item)
        {
            var path = item?.Path;
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // Use as-is.
            }

            try
            {
                string? folderToOpen = null;
                string? selectTarget = null;

                if (File.Exists(path))
                {
                    selectTarget = path;
                }
                else if (Directory.Exists(path))
                {
                    folderToOpen = path;
                }
                else
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        // Fallback: open containing folder.
                        folderToOpen = dir;
                    }
                }

                var args = selectTarget != null
                    ? $"/select,\"{selectTarget}\""
                    : folderToOpen != null
                        ? $"\"{folderToOpen}\""
                        : null;

                if (args == null)
                {
                    _ = ShowOkAsync("Open file location", "Could not resolve a folder to open.");
                    return;
                }

                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = args,
                    UseShellExecute = true
                });

                if (p == null)
                {
                    _ = ShowOkAsync("Open file location", "Explorer did not start.");
                }
            }
            catch (Exception ex)
            {
                _ = ShowOkAsync("Open file location", $"Could not open Explorer: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task OpenLoopEditor()
        {
            if (SelectedItem?.Path == null)
            {
                return;
            }

            var owner = GetMainWindow();
            var busy = new Window
            {
                Width = 420,
                Height = 160,
                Title = "Opening Loop Editor...",
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner?.Background,
                ShowInTaskbar = false,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Opening Loop Editor…", FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = SelectedItem.DisplayName, TextWrapping = TextWrapping.Wrap },
                        new ProgressBar { IsIndeterminate = true, Height = 6 }
                    }
                }
            };

            try
            {
                // Show the indicator first so the UI updates even if VM construction is slow.
                if (owner != null) busy.Show(owner);
                else busy.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                // Release any locked file handles before opening the loop editor.
                await Stop();

                var editedPath = SelectedItem.Path;
                var vm = new LoopEditorViewModel(SelectedItem.Path)
                {
                    OnSaved = async () => await ReloadLoopInfoFromDiskAsync(editedPath)
                };

                var window = new Views.LoopEditorWindow
                {
                    DataContext = vm
                };
                vm.SetCloseAction(() => window.Close());

                // Close the busy indicator *before* showing the editor, so focus doesn't bounce back.
                try { busy.Close(); } catch { }

                if (owner != null)
                {
                    window.Show(owner);
                }
                else
                {
                    window.Show();
                }

                window.Activate();
                window.Focus();
            }
            finally
            {
                try { busy.Close(); } catch { }
            }
        }

        [RelayCommand]
        private void OpenMusicPackCreator()
        {
            var vm = new MusicPackCreatorViewModel(LibraryItems);
            var window = new Views.MusicPackCreatorWindow
            {
                DataContext = vm
            };
            window.Show();
        }

        private Task ReloadLoopInfoFromDiskAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.CompletedTask;
            }

            try
            {
                var ext = System.IO.Path.GetExtension(path);
                LoopPoints? loop = null;
                if (string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    (loop, _) = WavLoopTagReader.Read(path);
                }
                else if (string.Equals(ext, ".scd", StringComparison.OrdinalIgnoreCase))
                {
                    var scdParser = new ScdParser();
                    (loop, _) = scdParser.ReadScdInfo(path);
                }

                if (loop != null && loop.EndSample > loop.StartSample)
                {
                    if (SelectedItem != null && string.Equals(SelectedItem.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedItem.LoopStart = loop.StartSample;
                        SelectedItem.LoopEnd = loop.EndSample;
                    }

                    if (!string.IsNullOrEmpty(_currentPlayingPath) && string.Equals(_currentPlayingPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        LoopStart = loop.StartSample;
                        LoopEnd = loop.EndSample;
                        UpdateLoopSeconds();
                    }
                }
            }
            catch
            {
                // Best-effort only.
            }

            return Task.CompletedTask;
        }

        [RelayCommand]
        private void OpenNormalize()
        {
            var vm = new NormalizeViewModel();
            
            // Populate with SCD files from library
            foreach (var item in _lastScan.Where(i => i.Path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)))
            {
                vm.ScdFiles.Add(new SelectableScdFile
                {
                    DisplayName = item.DisplayName,
                    FilePath = item.Path,
                    IsSelected = false
                });
            }

            vm.RebuildFolderGroups();

            var window = new Views.NormalizeWindow
            {
                DataContext = vm
            };
            vm.SetCloseAction(() => window.Close());
            window.Show();
        }

        [RelayCommand]
        private async Task ConvertToScd()
        {
            if (SelectedItem == null) return;
            if (string.IsNullOrWhiteSpace(SelectedItem.Path)) return;
            var sourcePath = SelectedItem.Path;
            
            var ext = System.IO.Path.GetExtension(sourcePath).ToUpperInvariant();
            if (ext == ".SCD")
            {
                System.Diagnostics.Debug.WriteLine("Already an SCD file, no conversion needed");
                return;
            }

            try
            {
                // If the source is not WAV, convert it to WAV first
                string wavPath = sourcePath;
                if (ext != ".WAV")
                {
                    var ffmpegPath = ResolveFfmpegPath();
                    if (string.IsNullOrWhiteSpace(ffmpegPath))
                    {
                        System.Diagnostics.Debug.WriteLine("ffmpeg.exe not found - cannot convert non-WAV files to SCD");
                        return;
                    }

                    if (Path.IsPathRooted(ffmpegPath) && !System.IO.File.Exists(ffmpegPath))
                    {
                        System.Diagnostics.Debug.WriteLine("ffmpeg.exe not found at: " + ffmpegPath);
                        return;
                    }

                    // Create temporary WAV file
                    wavPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scdtoolkit_convert_{Guid.NewGuid():N}.wav");

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-y -hide_banner -nostats -i \"{sourcePath}\" -vn -map 0:a:0 -c:a pcm_s16le \"{wavPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = System.Diagnostics.Process.Start(psi))
                    {
                        if (process == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to start ffmpeg");
                            return;
                        }

                        await process.StandardOutput.ReadToEndAsync();
                        var err = await process.StandardError.ReadToEndAsync();

                        await process.WaitForExitAsync();
                        
                        if (process.ExitCode != 0 || !System.IO.File.Exists(wavPath))
                        {
                            System.Diagnostics.Debug.WriteLine("ffmpeg conversion failed: " + err);
                            return;
                        }
                    }
                }

                // Now proceed with WAV to SCD conversion
                // Ensure loop tags exist on the WAV so the encoder can preserve them
                var (existingLoop, _) = WavLoopTagReader.Read(wavPath);
                if (existingLoop == null && SelectedItem.LoopEnd > SelectedItem.LoopStart)
                {
                    try
                    {
                        WavLoopTagWriter.Write(wavPath, SelectedItem.LoopStart, SelectedItem.LoopEnd);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Unable to write WAV loop tags before encode: {ex.Message}");
                    }
                }

                // Find a base SCD to use as template (or use a default one)
                var baseScd = _lastScan.FirstOrDefault(item => 
                    System.IO.Path.GetExtension(item.Path).Equals(".scd", StringComparison.OrdinalIgnoreCase));
                
                if (baseScd == null)
                {
                    System.Diagnostics.Debug.WriteLine("No SCD file found to use as template");
                    // Clean up temp WAV if we created one
                    if (ext != ".WAV" && System.IO.File.Exists(wavPath))
                    {
                        try { System.IO.File.Delete(wavPath); } catch { }
                    }
                    return;
                }

                var window = GetMainWindow();
                if (window?.StorageProvider == null)
                {
                    System.Diagnostics.Debug.WriteLine("StorageProvider unavailable");
                    // Clean up temp WAV if we created one
                    if (ext != ".WAV" && System.IO.File.Exists(wavPath))
                    {
                        try { System.IO.File.Delete(wavPath); } catch { }
                    }
                    return;
                }

                var saveResult = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(sourcePath) + ".scd",
                    DefaultExtension = "scd",
                    FileTypeChoices = new List<FilePickerFileType> { new FilePickerFileType("SCD") { Patterns = new List<string> { "*.scd" } } }
                });
                var outputPath = saveResult?.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    // Clean up temp WAV if we created one
                    if (ext != ".WAV" && System.IO.File.Exists(wavPath))
                    {
                        try { System.IO.File.Delete(wavPath); } catch { }
                    }
                    return;
                }
                
                var encoder = new SCDToolkit.Core.Services.ScdEncoderService();
                var resultPath = await encoder.EncodeAsync(baseScd.Path, wavPath, quality: 10, fullLoop: false);

                // Move/copy to chosen path if encoder wrote elsewhere
                if (!string.Equals(resultPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Copy(resultPath, outputPath, overwrite: true);
                }

                // Clean up temp WAV if we created one
                if (ext != ".WAV" && System.IO.File.Exists(wavPath))
                {
                    try { System.IO.File.Delete(wavPath); } catch { }
                }
                
                // Refresh library to show new file
                await Rescan();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting to SCD: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ConvertToWav()
        {
            if (SelectedItem == null) return;
            if (string.IsNullOrWhiteSpace(SelectedItem.Path)) return;
            var sourcePath = SelectedItem.Path;
            
            var ext = System.IO.Path.GetExtension(sourcePath).ToUpperInvariant();
            if (ext == ".WAV")
            {
                System.Diagnostics.Debug.WriteLine("Already a WAV file, no conversion needed");
                return;
            }

            try
            {
                var window = GetMainWindow();
                if (window?.StorageProvider == null)
                {
                    System.Diagnostics.Debug.WriteLine("StorageProvider unavailable");
                    return;
                }

                var saveResult = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(sourcePath) + ".wav",
                    DefaultExtension = "wav",
                    FileTypeChoices = new List<FilePickerFileType> { new FilePickerFileType("WAV") { Patterns = new List<string> { "*.wav" } } }
                });
                var outputPath = saveResult?.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(outputPath)) return;

                LoopPoints? loopPoints = null;

                // For SCD files, use vgmstream and try to extract loop info
                if (ext == ".SCD")
                {
                    var scdParser = new ScdParser();
                    var (scdLoop, _) = scdParser.ReadScdInfo(sourcePath);
                    
                    var vgmstreamPath = ResolveVgmstreamPath();
                    if (!System.IO.File.Exists(vgmstreamPath))
                    {
                        System.Diagnostics.Debug.WriteLine("vgmstream-cli.exe not found");
                        return;
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = vgmstreamPath,
                        Arguments = $"-i -o \"{outputPath}\" \"{sourcePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = System.Diagnostics.Process.Start(psi))
                    {
                        if (process == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to start vgmstream");
                            return;
                        }

                        var outputText = await process.StandardOutput.ReadToEndAsync();
                        outputText += await process.StandardError.ReadToEndAsync();

                        await process.WaitForExitAsync();
                        
                        if (process.ExitCode != 0 || !System.IO.File.Exists(outputPath))
                        {
                            System.Diagnostics.Debug.WriteLine("vgmstream conversion failed");
                            return;
                        }

                        loopPoints = ParseVgmstreamLoopInfo(outputText) ?? scdLoop;
                    }
                }
                else
                {
                    // For other formats (MP3, OGG, etc.), use ffmpeg
                    var ffmpegPath = ResolveFfmpegPath();
                    if (string.IsNullOrWhiteSpace(ffmpegPath))
                    {
                        System.Diagnostics.Debug.WriteLine("ffmpeg.exe not found");
                        return;
                    }

                    if (Path.IsPathRooted(ffmpegPath) && !System.IO.File.Exists(ffmpegPath))
                    {
                        System.Diagnostics.Debug.WriteLine("ffmpeg.exe not found at: " + ffmpegPath);
                        return;
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-y -hide_banner -nostats -i \"{sourcePath}\" -vn -map 0:a:0 -c:a pcm_s16le \"{outputPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = System.Diagnostics.Process.Start(psi))
                    {
                        if (process == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to start ffmpeg");
                            return;
                        }

                        await process.StandardOutput.ReadToEndAsync();
                        var err = await process.StandardError.ReadToEndAsync();

                        await process.WaitForExitAsync();
                        
                        if (process.ExitCode != 0 || !System.IO.File.Exists(outputPath))
                        {
                            System.Diagnostics.Debug.WriteLine("ffmpeg conversion failed: " + err);
                            return;
                        }
                    }
                }

                // Try to write loop tags if we have loop points
                if (loopPoints != null && loopPoints.EndSample > loopPoints.StartSample)
                {
                    try
                    {
                        WavLoopTagWriter.Write(outputPath, loopPoints.StartSample, loopPoints.EndSample);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Unable to write WAV loop tags: {ex.Message}");
                    }
                }

                // Refresh library to show new file
                await Rescan();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting to WAV: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task CreateTitleScreenMod(LibraryItemViewModel? item)
        {
            if (item == null) return;
            if (!item.IsScd) return;
            if (string.IsNullOrWhiteSpace(item.Path)) return;
            if (!System.IO.File.Exists(item.Path)) return;

            var window = GetMainWindow();
            if (window?.StorageProvider == null)
            {
                System.Diagnostics.Debug.WriteLine("StorageProvider unavailable");
                return;
            }

            var suffix = System.IO.Path.GetFileNameWithoutExtension(item.Path);
            var saveResult = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"Title Screen Music Replacer - {suffix}.zip",
                DefaultExtension = "zip",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Zip") { Patterns = new List<string> { "*.zip" } }
                }
            });

            var outputPath = saveResult?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(outputPath)) return;

            if (string.IsNullOrWhiteSpace(System.IO.Path.GetExtension(outputPath)))
            {
                outputPath += ".zip";
            }

            try
            {
                new TitleScreenModExporter().Export(item.Path, outputPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating Title Screen mod zip: {ex.Message}");
            }
        }

        private static string ResolveVgmstreamPath()
        {
            var baseDir = AppContext.BaseDirectory;
            var local = System.IO.Path.Combine(baseDir, "vgmstream-cli.exe");
            if (System.IO.File.Exists(local))
                return local;

            // Try repo-level vgmstream folder
            var repoLevel = System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "vgmstream", "vgmstream-cli.exe");
            if (System.IO.File.Exists(repoLevel))
                return System.IO.Path.GetFullPath(repoLevel);

            return "vgmstream-cli.exe";
        }

        private static string? ResolveFfmpegPath()
        {
            var env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            if (!string.IsNullOrWhiteSpace(env) && System.IO.File.Exists(env))
            {
                return env;
            }

            var baseDir = AppContext.BaseDirectory;
            var local = System.IO.Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe");
            if (System.IO.File.Exists(local))
                return local;

            // Try repo-level ffmpeg folder
            var repoLevel = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "SCDToolkit.Desktop", "ffmpeg", "bin", "ffmpeg.exe"));
            if (System.IO.File.Exists(repoLevel))
                return repoLevel;

            return "ffmpeg.exe";
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
        }

        private IEnumerable<LibraryItem> Filter(IEnumerable<LibraryItem> items, string text)
        {
            // Filetype filter
            if (FileTypeFilters.Count > 0)
            {
                var allowed = FileTypeFilters.Where(f => f.IsChecked).Select(f => f.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (allowed.Count > 0 && allowed.Count != FileTypeFilters.Count)
                {
                    items = items.Where(i => allowed.Contains(Path.GetExtension(i.Path) ?? string.Empty));
                }
                else if (allowed.Count == 0)
                {
                    items = Enumerable.Empty<LibraryItem>();
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return items;
            }
            var lowered = text.Trim().ToLowerInvariant();
            return items.Where(i => i.DisplayName.ToLowerInvariant().Contains(lowered) || i.Details.ToLowerInvariant().Contains(lowered));
        }

        private void ApplyFilter()
        {
            var filtered = Filter(_lastScan, SearchText).ToList();

            var vms = filtered.Select(ToVm).ToList();
            var byPath = vms
                .Where(vm => !string.IsNullOrWhiteSpace(vm.Path))
                .GroupBy(vm => vm.Path!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            LibraryItems = new ObservableCollection<LibraryItemViewModel>(vms);

            if (GroupByFolder)
            {
                // Flat per-folder view (no nested tree)
                var groups = filtered
                    .GroupBy(item => System.IO.Path.GetDirectoryName(item.Path) ?? "Unknown")
                    .OrderBy(g => g.Key)
                    .Select(g => new FolderGroupViewModel
                    {
                        FolderPath = g.Key,
                        DisplayName = System.IO.Path.GetFileName(g.Key) ?? g.Key,
                        Items = new ObservableCollection<LibraryItemViewModel>(g.Select(i => byPath.TryGetValue(i.Path, out var vm) ? vm : ToVm(i))),
                        IsExpanded = true
                    });
                FolderGroups = new ObservableCollection<FolderGroupViewModel>(groups);
            }
            else
            {
                FolderGroups.Clear();
            }
        }

        private Dictionary<string, bool> CaptureFolderTreeExpansionState()
        {
            var state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in FolderTreeRoots)
            {
                CaptureFolderTreeExpansionStateRecursive(root, state);
            }
            return state;
        }

        private static void CaptureFolderTreeExpansionStateRecursive(LibraryTreeNodeViewModel node, Dictionary<string, bool> state)
        {
            if (!string.IsNullOrWhiteSpace(node.FolderPath))
            {
                state[node.FolderPath] = node.IsExpanded;
            }

            foreach (var child in node.Children.OfType<LibraryTreeNodeViewModel>())
            {
                CaptureFolderTreeExpansionStateRecursive(child, state);
            }
        }

        private static void RestoreFolderTreeExpansionState(IEnumerable<LibraryTreeNodeViewModel> roots, Dictionary<string, bool> state)
        {
            if (state.Count == 0)
            {
                return;
            }

            foreach (var root in roots)
            {
                RestoreFolderTreeExpansionStateRecursive(root, state);
            }
        }

        private static void RestoreFolderTreeExpansionStateRecursive(LibraryTreeNodeViewModel node, Dictionary<string, bool> state)
        {
            if (!string.IsNullOrWhiteSpace(node.FolderPath) && state.TryGetValue(node.FolderPath, out var expanded))
            {
                node.IsExpanded = expanded;
            }

            foreach (var child in node.Children.OfType<LibraryTreeNodeViewModel>())
            {
                RestoreFolderTreeExpansionStateRecursive(child, state);
            }
        }

        private IEnumerable<LibraryTreeNodeViewModel> BuildFolderTree(List<LibraryItem> filtered, Dictionary<string, LibraryItemViewModel> byPath)
        {
            var roots = new List<LibraryTreeNodeViewModel>();

            var scanRoots = ScanFolders
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => Path.GetFullPath(s.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rootNodes = new Dictionary<string, LibraryTreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in scanRoots)
            {
                var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = root;
                }

                rootNodes[root] = new LibraryTreeNodeViewModel
                {
                    FolderPath = root,
                    DisplayName = name,
                    IsExpanded = true
                };
            }

            const string otherRootKey = "(Other)";
            rootNodes[otherRootKey] = new LibraryTreeNodeViewModel
            {
                FolderPath = otherRootKey,
                DisplayName = otherRootKey,
                IsExpanded = true
            };

            var nodeByFullPath = new Dictionary<string, LibraryTreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in rootNodes)
            {
                nodeByFullPath[kvp.Key] = kvp.Value;
            }

            foreach (var item in filtered)
            {
                if (string.IsNullOrWhiteSpace(item.Path)) continue;
                if (!byPath.TryGetValue(item.Path, out var itemVm)) continue;

                var fullItemPath = Path.GetFullPath(item.Path);
                var root = FindBestScanRoot(scanRoots, fullItemPath) ?? otherRootKey;

                var dir = Path.GetDirectoryName(fullItemPath) ?? root;
                var currentNode = nodeByFullPath[root];

                if (!string.Equals(root, otherRootKey, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(root, dir);
                    if (!string.IsNullOrWhiteSpace(rel) && rel != ".")
                    {
                        foreach (var segment in SplitPath(rel))
                        {
                            var nextFull = Path.Combine(currentNode.FolderPath, segment);
                            if (!nodeByFullPath.TryGetValue(nextFull, out var nextNode))
                            {
                                nextNode = new LibraryTreeNodeViewModel
                                {
                                    FolderPath = nextFull,
                                    DisplayName = segment,
                                    IsExpanded = true
                                };
                                currentNode.Children.Add(nextNode);
                                nodeByFullPath[nextFull] = nextNode;
                            }
                            currentNode = nextNode;
                        }
                    }
                }

                currentNode.Children.Add(itemVm);
            }

            foreach (var root in scanRoots)
            {
                var node = rootNodes[root];
                SortAndCountRecursive(node);
                if (node.Children.Count > 0)
                {
                    roots.Add(node);
                }
            }

            SortAndCountRecursive(rootNodes[otherRootKey]);
            if (rootNodes[otherRootKey].Children.Count > 0)
            {
                roots.Add(rootNodes[otherRootKey]);
            }

            return roots;
        }

        private static IEnumerable<string> SplitPath(string relative)
        {
            return relative
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s));
        }

        private static string? FindBestScanRoot(List<string> roots, string fullPath)
        {
            string? best = null;
            foreach (var r in roots)
            {
                var rp = EnsureTrailingSeparator(r);
                if (fullPath.StartsWith(rp, StringComparison.OrdinalIgnoreCase) || string.Equals(fullPath, r, StringComparison.OrdinalIgnoreCase))
                {
                    if (best == null || r.Length > best.Length)
                    {
                        best = r;
                    }
                }
            }
            return best;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return path;
            }
            return path + Path.DirectorySeparatorChar;
        }

        private static int SortAndCountRecursive(LibraryTreeNodeViewModel node)
        {
            var folders = node.Children.OfType<LibraryTreeNodeViewModel>()
                .OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var files = node.Children.OfType<LibraryItemViewModel>()
                .OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();

            var newChildren = new ObservableCollection<object>();
            foreach (var f in folders)
            {
                newChildren.Add(f);
            }
            foreach (var f in files)
            {
                newChildren.Add(f);
            }
            node.Children = newChildren;

            var count = 0;
            foreach (var childFolder in folders)
            {
                count += SortAndCountRecursive(childFolder);
            }
            count += files.Count;
            node.ItemCount = count;
            return count;
        }

        private void RefreshTransport()
        {
            if (IsSeeking)
            {
                return;
            }

            PositionSeconds = _playbackService.Position.TotalSeconds;
            DurationSeconds = _playbackService.Duration.TotalSeconds;
            IsPlaying = _playbackService.IsPlaying;
            PositionText = FormatTime(PositionSeconds);
            DurationText = FormatTime(DurationSeconds);
        }

        partial void OnVolumeChanged(double value)
        {
            _playbackService.Volume = value;
            SaveConfig();
        }

        partial void OnIsPlayingChanged(bool value)
        {
            PlayPauseGlyph = value ? "⏸" : "▶";
            PlayPauseIconData = value ? "M6,4 L10,4 L10,20 L6,20 Z M14,4 L18,4 L18,20 L14,20 Z" : "M8,5 L8,19 L19,12 Z";
        }

        private void UpdateLoopSeconds()
        {
            if (SampleRate > 0)
            {
                LoopStartSeconds = LoopStart / (double)SampleRate;
                LoopEndSeconds = LoopEnd / (double)SampleRate;
            }
            else
            {
                LoopStartSeconds = 0;
                LoopEndSeconds = 0;
            }
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0)
            {
                return "00:00";
            }
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.ToString(ts.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
        }

        private static LoopPoints? ParseVgmstreamLoopInfo(string? vgmstreamOutput)
        {
            if (string.IsNullOrWhiteSpace(vgmstreamOutput))
            {
                return null;
            }

            var loopStartMatch = Regex.Match(vgmstreamOutput, @"loop start:\s*(\d+)\s*samples", RegexOptions.IgnoreCase);
            var loopEndMatch = Regex.Match(vgmstreamOutput, @"loop end:\s*(\d+)\s*samples", RegexOptions.IgnoreCase);

            if (loopStartMatch.Success && loopEndMatch.Success)
            {
                if (int.TryParse(loopStartMatch.Groups[1].Value, out var loopStart) &&
                    int.TryParse(loopEndMatch.Groups[1].Value, out var loopEnd) &&
                    loopEnd > loopStart)
                {
                    return new LoopPoints(loopStart, loopEnd);
                }
            }

            return null;
        }

        private void RefreshKh2HookStatusThrottled()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastKh2StatusCheckUtc).TotalSeconds < 1.0)
            {
                return;
            }

            _lastKh2StatusCheckUtc = now;

            RefreshScdHookStatus();
        }

        private void RefreshScdHookStatus()
        {
            IsScdHookConnected = _kh2HookService.TryGetKh2Process(out _);

            ScdHookQueuedFieldName = string.IsNullOrWhiteSpace(ScdHookQueuedField)
                ? "(none)"
                : Path.GetFileName(ScdHookQueuedField);

            ScdHookQueuedBattleName = string.IsNullOrWhiteSpace(ScdHookQueuedBattle)
                ? "(none)"
                : Path.GetFileName(ScdHookQueuedBattle);
        }

        [RelayCommand]
        private async Task ApplySCDHook()
        {
            // Apply queued paths if present, otherwise re-apply last loaded.
            var field = string.IsNullOrWhiteSpace(ScdHookQueuedField) ? ScdHookLoadedField : ScdHookQueuedField;
            var battle = string.IsNullOrWhiteSpace(ScdHookQueuedBattle) ? ScdHookLoadedBattle : ScdHookQueuedBattle;
            await SendToKh2ApplyInternalAsync(field, battle);
        }

        [RelayCommand]
        private void QueueField(LibraryItemViewModel? item)
        {
            if (item != null) SelectedItem = item;
            QueuePathForHook(SelectedItem?.Path, isField: true);
        }

        [RelayCommand]
        private async Task QueueAndPlayField(LibraryItemViewModel? item)
        {
            QueueField(item);
            await ApplySCDHook();
        }

        [RelayCommand]
        private void QueueBattle(LibraryItemViewModel? item)
        {
            if (item != null) SelectedItem = item;
            QueuePathForHook(SelectedItem?.Path, isField: false);
        }

        [RelayCommand]
        private async Task QueueAndPlayBattle(LibraryItemViewModel? item)
        {
            QueueBattle(item);
            await ApplySCDHook();
        }

        private void QueuePathForHook(string? path, bool isField)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                Kh2HookStatus = "SCDHook: Select an .scd file.";
                return;
            }

            var abs = Path.GetFullPath(path);
            if (isField) ScdHookQueuedField = abs;
            else ScdHookQueuedBattle = abs;

            RefreshScdHookStatus();
        }

        [RelayCommand]
        private async Task SendToKh2Field(LibraryItemViewModel? item)
        {
            if (item != null)
            {
                SelectedItem = item;
            }

            QueueField(item);
            await ApplySCDHook();
        }

        [RelayCommand]
        private async Task SendToKh2Battle(LibraryItemViewModel? item)
        {
            if (item != null)
            {
                SelectedItem = item;
            }

            QueueBattle(item);
            await ApplySCDHook();
        }

        [RelayCommand]
        private async Task SendToKh2Both(LibraryItemViewModel? item)
        {
            if (item != null)
            {
                SelectedItem = item;
            }

            QueuePathForHook(SelectedItem?.Path, isField: true);
            QueuePathForHook(SelectedItem?.Path, isField: false);
            await ApplySCDHook();
        }

        private async Task SendToKh2ApplyInternalAsync(string? field, string? battle)
        {
            if (!string.IsNullOrWhiteSpace(field) && !field.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                Kh2HookStatus = "SCDHook: Select an .scd file.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(battle) && !battle.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                Kh2HookStatus = "SCDHook: Select an .scd file.";
                return;
            }

            var fieldAbs = string.IsNullOrWhiteSpace(field) ? null : Path.GetFullPath(field);
            var battleAbs = string.IsNullOrWhiteSpace(battle) ? null : Path.GetFullPath(battle);

            Kh2HookStatus = "SCDHook: Applying...";
            var result = await Task.Run(() => _kh2HookService.ApplyScdPaths(fieldAbs, battleAbs));
            if (result.Success)
            {
                if (!string.IsNullOrWhiteSpace(fieldAbs)) ScdHookLoadedField = fieldAbs;
                if (!string.IsNullOrWhiteSpace(battleAbs)) ScdHookLoadedBattle = battleAbs;
                RefreshScdHookStatus();
                Kh2HookStatus = "SCDHook: Applied.";
            }
            else
            {
                Kh2HookStatus = $"SCDHook: {result.Message}";
            }
        }

        // Context menu helpers (operate on a specific item)
        [RelayCommand]
        private async Task DeleteItem(LibraryItemViewModel? item)
        {
            if (item != null) SelectedItem = item;
            await DeleteSelected();
        }

        [RelayCommand]
        private async Task OpenLoopEditorItem(LibraryItemViewModel? item)
        {
            if (item != null) SelectedItem = item;
            await OpenLoopEditor();
        }

        [RelayCommand]
        private async Task QuickNormalizeItem(LibraryItemViewModel? item)
        {
            if (item != null) SelectedItem = item;

            var path = SelectedItem?.Path;
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                await ShowOkAsync("Normalize", "Quick normalize currently supports .scd files only.");
                return;
            }

            // Stop playback to avoid file locks / stale decode state.
            await Stop();

            var mode = await PromptQuickNormalizeModeAsync(Path.GetFileName(path));
            if (mode == null) return;

            const double targetLufs = -12.0;
            const float volumeFloat = 1.4f;

            try
            {
                if (mode == QuickNormalizeMode.FloatPatchOnly)
                {
                    ScdVolumePatcher.PatchVolume(path, volumeFloat);
                    Services.ScdNormalizationManager.RecordAppliedProfile(path, $"float_patch_only;volumeFloat={volumeFloat:0.###}", userTuned: false);
                }
                else
                {
                    var opts = new Services.ScdNormalizationOptions(
                        Normalize: true,
                        TargetLufs: targetLufs,
                        PatchVolume: true,
                        VolumeFloat: volumeFloat);

                    var (ok, skipped, message) = await Services.ScdNormalizationManager.NormalizeOneAsync(path, opts, force: false);
                    if (!ok)
                    {
                        await ShowOkAsync("Normalize", $"Normalization failed: {message}");
                        return;
                    }

                    if (skipped)
                    {
                        await ShowOkAsync("Normalize", $"Skipped ({message}).");
                    }
                }

                await ReloadLoopInfoFromDiskAsync(path);
            }
            catch (Exception ex)
            {
                await ShowOkAsync("Normalize", $"Normalization failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ConvertToScdItem(LibraryItemViewModel? item)
        {
            if (item != null) SelectedItem = item;
            await ConvertToScd();
        }

        [RelayCommand]
        private async Task ConvertToWavItem(LibraryItemViewModel? item)
        {
            if (item != null) SelectedItem = item;
            await ConvertToWav();
        }

        private static LibraryItemViewModel ToVm(LibraryItem item)
        {
            return new LibraryItemViewModel(item.DisplayName, item.Details)
            {
                LoopStart = item.LoopPoints?.StartSample ?? 0,
                LoopEnd = item.LoopPoints?.EndSample ?? 0,
                Path = item.Path
            };
        }
    }
}
