using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCDToolkit.Desktop.Services;
using SCDToolkit.Core.Services;
using Avalonia.Threading;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class NormalizeViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<SelectableScdFile> scdFiles = new();

        [ObservableProperty]
        private ObservableCollection<NormalizeFolderGroupViewModel> folderGroups = new();

        [ObservableProperty]
        private bool isFullNormalize = false;

        [ObservableProperty]
        private bool isFloatPatch = true;

        [ObservableProperty]
        private double volumeMultiplier = 1.4;

        [ObservableProperty]
        private bool isBusy;

        [ObservableProperty]
        private double progressValue;

        [ObservableProperty]
        private string progressText = string.Empty;

        [ObservableProperty]
        private string statusText = string.Empty;

        private Action? _closeAction;

        public void SetCloseAction(Action closeAction)
        {
            _closeAction = closeAction;
        }

        partial void OnIsFullNormalizeChanged(bool value)
        {
            if (value)
            {
                IsFloatPatch = false;
            }
        }

        partial void OnIsFloatPatchChanged(bool value)
        {
            if (value)
            {
                IsFullNormalize = false;
            }
        }

        public void RebuildFolderGroups()
        {
            var byFolder = ScdFiles
                .GroupBy(f => SafeGetDirectoryName(f.FilePath), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FolderGroups.Clear();
            foreach (var g in byFolder)
            {
                var folderPath = g.Key;
                var group = new NormalizeFolderGroupViewModel
                {
                    FolderPath = folderPath,
                    DisplayName = string.IsNullOrWhiteSpace(folderPath) ? "(unknown)" : Path.GetFileName(folderPath) ?? folderPath,
                    IsExpanded = true
                };

                foreach (var f in g.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    group.Items.Add(f);
                }

                group.AttachSelectionTracking();
                FolderGroups.Add(group);
            }
        }

        private static string SafeGetDirectoryName(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                return Path.GetDirectoryName(full) ?? string.Empty;
            }
            catch
            {
                return Path.GetDirectoryName(path) ?? string.Empty;
            }
        }

        [RelayCommand]
        private async Task NormalizeSelected()
        {
            if (IsBusy) return;

            var selected = ScdFiles.Where(f => f.IsSelected).ToList();
            if (!selected.Any())
            {
                StatusText = "No files selected.";
                return;
            }

            IsBusy = true;
            ProgressValue = 0;
            ProgressText = "Starting…";
            StatusText = string.Empty;

            var errors = new List<string>();
            var total = selected.Count;

            try
            {
                if (IsFloatPatch)
                {
                    var profileKey = $"float_patch_only;volumeFloat={(float)VolumeMultiplier:0.###}";
                    var maxDop = Math.Clamp(Environment.ProcessorCount, 2, 8);
                    using var gate = new SemaphoreSlim(maxDop, maxDop);
                    var errorBag = new ConcurrentBag<string>();
                    var completed = 0;

                    var tasks = selected.Select(async file =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            await Task.Run(() => ScdVolumePatcher.PatchVolume(file.FilePath, (float)VolumeMultiplier));
                            ScdNormalizationManager.RecordAppliedProfile(file.FilePath, profileKey, userTuned: false);
                        }
                        catch (Exception ex)
                        {
                            errorBag.Add($"{file.DisplayName}: {ex.Message}");
                        }
                        finally
                        {
                            var done = Interlocked.Increment(ref completed);
                            Dispatcher.UIThread.Post(() =>
                            {
                                ProgressValue = total <= 0 ? 0 : (double)done / total;
                                ProgressText = $"Processing {done}/{total}: {file.DisplayName}";
                            });
                            gate.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);

                    if (!errorBag.IsEmpty)
                    {
                        errors.AddRange(errorBag);
                    }
                }
                else
                {
                    var opts = new ScdNormalizationOptions(
                        Normalize: true,
                        TargetLufs: -12.0,
                        PatchVolume: true,
                        VolumeFloat: (float)VolumeMultiplier);

                    var maxDop = Math.Clamp(Environment.ProcessorCount, 2, 6);
                    var result = await ScdNormalizationManager.NormalizeManyAsync(
                        selected.Select(s => s.FilePath),
                        opts,
                        maxDegreeOfParallelism: maxDop,
                        progress: p =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                var done = p.Completed + p.Skipped + p.Errors;
                                ProgressValue = p.Total <= 0 ? 0 : (double)done / p.Total;
                                ProgressText = $"{done}/{p.Total}: {Path.GetFileName(p.CurrentFile)} ({p.Message})";
                            });
                        },
                        force: false,
                        cancellationToken: CancellationToken.None);

                    if (result.ErrorMessages.Count > 0)
                    {
                        errors.AddRange(result.ErrorMessages);
                    }

                    if (result.Skipped > 0)
                    {
                        StatusText = $"Normalized {result.Completed} file(s), skipped {result.Skipped}.";
                    }
                }

                ProgressValue = 1;
                ProgressText = "Done.";

                if (errors.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(StatusText))
                    {
                        StatusText = $"Normalized {total} file(s).";
                    }
                }
                else
                {
                    StatusText = $"Completed with {errors.Count} error(s). See Output/Debug log.";
                    foreach (var e in errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"Normalize error: {e}");
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task NormalizeAll()
        {
            foreach (var file in ScdFiles)
            {
                file.IsSelected = true;
            }
            await NormalizeSelected();
        }

        [RelayCommand]
        private void Cancel()
        {
            _closeAction?.Invoke();
        }
    }
}
