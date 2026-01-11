using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCDToolkit.Core.Models;
using SCDToolkit.Core.Services;
using NAudio.Wave;
using System.Collections.Generic;
using System.Linq;
using SCDToolkit.Desktop.Models;
using SCDToolkit.Desktop.Services;
using Avalonia.Threading;
#pragma warning disable MVVMTK0034
using System.Text.RegularExpressions;
#pragma warning disable MVVMTK0034

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class LoopEditorViewModel : ObservableObject
    {
        private readonly string _path;
        private readonly bool _isWav;
        private readonly bool _isScd;
        private readonly ScdParser _scdParser = new();
        private Action? _close;
        private readonly VgmstreamPlaybackService _playbackService = new();
        private readonly DispatcherTimer _playheadTimer;
        private bool _resumeAfterScrub;

        [ObservableProperty]
        private string title = "Loop Editor";

        [ObservableProperty]
        private string fileName = string.Empty;

        [ObservableProperty]
        private int sampleRate;

        [ObservableProperty]
        private int channels;

        [ObservableProperty]
        private int totalSamples;

        [ObservableProperty]
        private string totalSamplesDisplay = string.Empty;

        [ObservableProperty]
        private string durationDisplay = string.Empty;

        [ObservableProperty]
        private int loopStart;

        [ObservableProperty]
        private int loopEnd;

        [ObservableProperty]
        private string loopStartTime = "--:--";

        [ObservableProperty]
        private string loopEndTime = "--:--";

        [ObservableProperty]
        private string status = "Ready";

        [ObservableProperty]
        private bool hasChanges;

        [ObservableProperty]
        private bool loopPreviewEnabled = true;

        [ObservableProperty]
        private bool isPreviewPlaying;

        [ObservableProperty]
        private string previewPlayPauseLabel = "Play";

        partial void OnLoopPreviewEnabledChanged(bool value)
        {
            _playbackService.LoopEnabled = value;
        }

        [ObservableProperty]
        private double waveformPixelWidth = 1600d;

        [ObservableProperty]
        private double viewportWidth = 1200d;

        [ObservableProperty]
        private string analysisResult = string.Empty;

        [ObservableProperty]
        private string analysisStatus = "Not analyzed";

        [ObservableProperty]
        private bool normalizeOnSave = true;

        [ObservableProperty]
        private double targetLufs = -12.0;

        [ObservableProperty]
        private bool patchScdVolumeOnSave = true;

        [ObservableProperty]
        private double scdVolumeFloat = 1.4;

        [ObservableProperty]
        private WaveformData? waveform;

        [ObservableProperty]
        private double samplesPerPixel = 200d;

        [ObservableProperty]
        private int viewStartSample;

        [ObservableProperty]
        private int playheadSample;

        public bool IsWav => _isWav;
        public bool IsScd => _isScd;

        public Func<Task>? OnSaved { get; set; }

        public LoopEditorViewModel(string path)
        {
            _path = path;
            _isWav = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
            _isScd = path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase);
            FileName = Path.GetFileName(path);
            Title = $"Loop Editor — {FileName}";

            _playheadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _playheadTimer.Tick += (_, _) => UpdatePlayheadFromPlayback();

            Load();
            UpdatePreviewLabels();
        }

        partial void OnIsPreviewPlayingChanged(bool value)
        {
            UpdatePreviewLabels();
        }

        private void UpdatePreviewLabels()
        {
            PreviewPlayPauseLabel = IsPreviewPlaying ? "Pause" : "Play";
        }

        public void SetCloseAction(Action close) => _close = close;

        partial void OnLoopStartChanged(int value)
        {
            var clamped = Math.Max(0, Math.Min(value, Math.Max(0, TotalSamples - 1)));
            if (clamped != value)
            {
                loopStart = clamped;
                OnPropertyChanged(nameof(LoopStart));
            }

            if (LoopEnd <= loopStart && TotalSamples > 0)
            {
                loopEnd = Math.Min(Math.Max(loopStart + 1, 1), Math.Max(1, TotalSamples));
                OnPropertyChanged(nameof(LoopEnd));
            }

            HasChanges = true;
            UpdateTimes();
        }

        partial void OnLoopEndChanged(int value)
        {
            var maxSample = Math.Max(1, TotalSamples);
            var clamped = Math.Max(1, Math.Min(value, maxSample));
            if (clamped != value)
            {
                loopEnd = clamped;
                OnPropertyChanged(nameof(LoopEnd));
            }

            if (loopEnd <= LoopStart)
            {
                loopEnd = Math.Min(Math.Max(LoopStart + 1, 1), maxSample);
                OnPropertyChanged(nameof(LoopEnd));
            }

            HasChanges = true;
            UpdateTimes();
        }

        partial void OnTotalSamplesChanged(int value)
        {
            UpdateTimes();
            UpdateWaveformWidth();
        }

        partial void OnSampleRateChanged(int value) => UpdateTimes();

        partial void OnWaveformChanged(WaveformData? value)
        {
            if (value != null)
            {
                SamplesPerPixel = Math.Clamp(value.TotalSamples / 800.0, 1, 4096);
                ViewStartSample = 0;
                UpdateWaveformWidth();
            }
        }

        partial void OnSamplesPerPixelChanged(double value)
        {
            // SamplesPerPixel semantics:
            // - smaller = zoom IN (wider waveform)
            // - larger  = zoom OUT (narrower waveform)
            // We want to prevent zooming OUT past the point where the whole file fits the viewport.
            var effectiveMin = 0.01; // allow deep zoom-in

            double effectiveMax;
            if (ViewportWidth > 1 && TotalSamples > 0)
            {
                // Max zoom-out is exactly "full waveform fits the viewport".
                effectiveMax = Math.Min(16384.0, TotalSamples / ViewportWidth);
                if (effectiveMax < effectiveMin)
                {
                    effectiveMax = effectiveMin;
                }
            }
            else
            {
                effectiveMax = 16384.0;
            }

            var clamped = Math.Clamp(value, effectiveMin, effectiveMax);
            if (Math.Abs(clamped - value) > 0.001)
            {
                samplesPerPixel = clamped;
                OnPropertyChanged(nameof(SamplesPerPixel));
            }

            UpdateWaveformWidth();
        }

        partial void OnViewportWidthChanged(double value)
        {
            // Re-apply zoom clamp and resize waveform when the viewport changes.
            OnSamplesPerPixelChanged(SamplesPerPixel);
        }

        partial void OnViewStartSampleChanged(int value)
        {
            var maxStart = Math.Max(0, TotalSamples - 1);
            var clamped = Math.Clamp(value, 0, maxStart);
            if (clamped != value)
            {
                viewStartSample = clamped;
                OnPropertyChanged(nameof(ViewStartSample));
            }
        }

        private void Load()
        {
            try
            {
                if (_isWav)
                {
                    var (loop, meta) = WavLoopTagReader.Read(_path);
                    SampleRate = meta?.SampleRate ?? 0;
                    Channels = meta?.Channels ?? 0;
                    TotalSamples = meta?.TotalSamples ?? 0;
                    LoopStart = loop?.StartSample ?? 0;
                    LoopEnd = loop?.EndSample ?? Math.Max(LoopStart + 1, TotalSamples);
                    Waveform = LoadWaveformFromWav(_path);
                }
                else if (_isScd)
                {
                    var (loop, meta) = _scdParser.ReadScdInfo(_path);
                    SampleRate = meta?.SampleRate ?? 0;
                    Channels = meta?.Channels ?? 0;
                    TotalSamples = meta?.TotalSamples ?? 0;
                    var loopStart = loop?.StartSample ?? 0;
                    var loopEnd = loop?.EndSample ?? Math.Max(loopStart + 1, TotalSamples);

                    // If metadata missing, decode once via vgmstream to get accurate length
                    if ((SampleRate == 0 || TotalSamples == 0) && File.Exists(_path))
                    {
                        TryPopulateMetadataFromDecoded();
                    }

                    var decoded = DecodeScdToTempWav(out var decodedLoop);
                    if (!string.IsNullOrWhiteSpace(decoded) && File.Exists(decoded))
                    {
                        Waveform = LoadWaveformFromWav(decoded, deleteWhenDone: true);
                    }

                    // Prefer loop info from vgmstream decode (matches playback pipeline)
                    if (decodedLoop != null && decodedLoop.EndSample > decodedLoop.StartSample)
                    {
                        loopStart = decodedLoop.StartSample;
                        loopEnd = decodedLoop.EndSample;
                    }

                    LoopStart = loopStart;
                    LoopEnd = loopEnd;
                }

                Status = "Ready";
                HasChanges = false;
                UpdateTimes();
                DurationDisplay = FormatTime(TotalSamples, SampleRate);
                TotalSamplesDisplay = $"{TotalSamples:N0} samples";
                UpdateWaveformWidth();
                PlayheadSample = LoopStart;
            }
            catch (Exception ex)
            {
                Status = $"Load failed: {ex.Message}";
            }
        }

        private WaveformData? LoadWaveformFromWav(string wavPath, bool deleteWhenDone = false)
        {
            try
            {
                using var reader = new AudioFileReader(wavPath);
                var totalSamples = (int)(reader.Length / reader.WaveFormat.BlockAlign);
                var channels = reader.WaveFormat.Channels;

                var buffer = new float[reader.WaveFormat.Channels];
                var samples = new float[channels][];
                for (int c = 0; c < channels; c++)
                {
                    samples[c] = new float[totalSamples];
                }

                int sampleIndex = 0;
                while (reader.Read(buffer, 0, channels) == channels)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        samples[c][sampleIndex] = buffer[c];
                    }
                    sampleIndex++;
                }

                var peaks = BuildPeaks(samples, sampleIndex);
                SampleRate = reader.WaveFormat.SampleRate;
                Channels = channels;
                TotalSamples = sampleIndex;
                return new WaveformData(sampleIndex, channels, samples, peaks);
            }
            catch (Exception ex)
            {
                Status = $"Waveform load failed: {ex.Message}";
                return null;
            }
            finally
            {
                if (deleteWhenDone)
                {
                    try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
                }
            }
        }

        private List<PeakLevel> BuildPeaks(float[][] samples, int totalSamples)
        {
            var levels = new List<PeakLevel>();
            var blockSizes = new[] { 64, 128, 256, 512, 1024, 2048, 4096 };
            foreach (var block in blockSizes)
            {
                var blocks = (totalSamples + block - 1) / block;
                var min = new float[samples.Length][];
                var max = new float[samples.Length][];
                for (int c = 0; c < samples.Length; c++)
                {
                    min[c] = new float[blocks];
                    max[c] = new float[blocks];
                }

                for (int b = 0; b < blocks; b++)
                {
                    int start = b * block;
                    int end = Math.Min(totalSamples, start + block);
                    for (int c = 0; c < samples.Length; c++)
                    {
                        float mn = 1f, mx = -1f;
                        for (int i = start; i < end; i++)
                        {
                            var v = samples[c][i];
                            if (v < mn) mn = v;
                            if (v > mx) mx = v;
                        }
                        min[c][b] = mn;
                        max[c][b] = mx;
                    }
                }

                levels.Add(new PeakLevel(block, min, max));
            }

            return levels;
        }

        [RelayCommand]
        private async Task Save()
        {
            if (LoopEnd <= LoopStart)
            {
                Status = "Loop end must be greater than start.";
                return;
            }

            try
            {
                if (_isWav)
                {
                    if (NormalizeOnSave)
                    {
                        Status = "Normalizing WAV...";
                        var ok = await NormalizeWavInPlaceAsync(_path, TargetLufs);
                        if (!ok)
                        {
                            Status = "Normalization failed (saved loop points only).";
                        }
                    }

                    WavLoopTagWriter.Write(_path, LoopStart, LoopEnd);
                }
                else if (_isScd)
                {
                    // If normalization or volume patching is requested, we must rebuild the SCD.
                    var needsRebuild = NormalizeOnSave || PatchScdVolumeOnSave;
                    if (needsRebuild)
                    {
                        Status = "Rebuilding SCD (normalize/patch)...";
                        var rebuilt = await RewriteScdViaReencodeAsync(
                            normalize: NormalizeOnSave,
                            targetLufs: TargetLufs,
                            patchVolume: PatchScdVolumeOnSave,
                            volumeFloat: (float)ScdVolumeFloat);

                        if (!rebuilt)
                        {
                            Status = "Could not rebuild SCD.";
                            return;
                        }
                    }
                    else
                    {
                        if (!ScdLoopWriter.TryWriteLoopPoints(_path, LoopStart, LoopEnd, out var error))
                        {
                            Status = "Rebuilding SCD with new loop points...";
                            var rebuilt = await RewriteScdViaReencodeAsync(
                                normalize: false,
                                targetLufs: TargetLufs,
                                patchVolume: false,
                                volumeFloat: (float)ScdVolumeFloat);
                            if (!rebuilt)
                            {
                                Status = $"Could not save loop points: {error ?? "rewrite failed"}";
                                return;
                            }
                        }
                    }
                }

                HasChanges = false;
                Status = "Loop points saved.";

                // Mark the edited file as user-tuned so batch normalization won't overwrite it.
                try
                {
                    Services.ScdNormalizationManager.MarkUserTuned(_path);
                }
                catch
                {
                    // Best-effort.
                }

                if (OnSaved != null)
                {
                    await OnSaved();
                }

                // Close editor after successful save.
                await Close();
            }
            catch (Exception ex)
            {
                Status = $"Error saving: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task Close()
        {
            await StopPreview();
            _playbackService.Dispose();
            _close?.Invoke();
        }

        [RelayCommand]
        private async Task PlayPreview()
        {
            try
            {
                if (IsPreviewPlaying)
                {
                    return;
                }

                if (!File.Exists(_path))
                {
                    Status = "File not found.";
                    return;
                }

                var item = new LibraryItem(_path, FileName);
                var loop = (LoopPreviewEnabled && LoopEnd > LoopStart)
                    ? new LoopPoints(LoopStart, LoopEnd)
                    : new LoopPoints(0, 0);

                _playbackService.LoopEnabled = LoopPreviewEnabled;
                await _playbackService.PlayAsync(item, loop);

                // Start from current playhead (or loop start) if possible.
                if (SampleRate > 0)
                {
                    var startSample = PlayheadSample;
                    if (LoopPreviewEnabled && LoopEnd > LoopStart)
                    {
                        startSample = Math.Clamp(startSample, LoopStart, Math.Max(LoopStart, LoopEnd - 1));
                    }
                    else
                    {
                        startSample = Math.Clamp(startSample, 0, Math.Max(0, TotalSamples - 1));
                    }

                    await _playbackService.SeekAsync(TimeSpan.FromSeconds(startSample / (double)SampleRate));
                }

                IsPreviewPlaying = true;
                _playheadTimer.Start();
                Status = "Playing";
            }
            catch (Exception ex)
            {
                Status = $"Play failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task StopPreview()
        {
            try
            {
                _playheadTimer.Stop();
                await _playbackService.StopAsync();
            }
            finally
            {
                IsPreviewPlaying = false;
                Status = "Ready";
            }
        }

        [RelayCommand]
        private async Task StopPreviewAndReset()
        {
            await StopPreview();

            if (TotalSamples <= 0)
            {
                PlayheadSample = 0;
                return;
            }

            if (LoopPreviewEnabled && LoopEnd > LoopStart)
            {
                PlayheadSample = Math.Clamp(LoopStart, 0, Math.Max(0, TotalSamples - 1));
            }
            else
            {
                PlayheadSample = 0;
            }
        }

        [RelayCommand]
        private async Task TogglePreviewPlayPause()
        {
            if (IsPreviewPlaying)
            {
                await _playbackService.PauseAsync();
                _playheadTimer.Stop();
                IsPreviewPlaying = false;
                Status = "Paused";
            }
            else
            {
                // If we have a loaded reader (paused), resume without re-decoding.
                if (_playbackService.Duration > TimeSpan.Zero)
                {
                    await _playbackService.ResumeAsync();
                    _playheadTimer.Start();
                    IsPreviewPlaying = true;
                    Status = "Playing";
                }
                else
                {
                    await PlayPreview();
                }
            }
        }

        public void BeginScrub()
        {
            if (IsPreviewPlaying)
            {
                _resumeAfterScrub = true;
                _ = _playbackService.PauseAsync();
                _playheadTimer.Stop();
                IsPreviewPlaying = false;
                Status = "Scrubbing";
            }
            else
            {
                _resumeAfterScrub = false;
            }
        }

        public async Task EndScrubAsync()
        {
            if (_resumeAfterScrub)
            {
                _resumeAfterScrub = false;
                await TogglePreviewPlayPause();
            }
            else
            {
                if (Status == "Scrubbing")
                {
                    Status = "Paused";
                }
            }
        }

        public async Task SeekPreviewToSampleAsync(int sample)
        {
            if (TotalSamples <= 0 || SampleRate <= 0)
            {
                PlayheadSample = 0;
                return;
            }

            var clamped = Math.Clamp(sample, 0, Math.Max(0, TotalSamples - 1));
            PlayheadSample = clamped;

            // If we have an active playback reader (playing or paused), seek immediately.
            if (_playbackService.Duration > TimeSpan.Zero)
            {
                await _playbackService.SeekAsync(TimeSpan.FromSeconds(clamped / (double)SampleRate));
            }
        }

        [RelayCommand]
        private void ToggleLoopPlayback()
        {
            LoopPreviewEnabled = !LoopPreviewEnabled;
            Status = LoopPreviewEnabled ? "Loop preview on" : "Loop preview off";
        }

        [RelayCommand]
        private void SetLoopStartAtCursor()
        {
            LoopStart = PlayheadSample;
        }

        [RelayCommand]
        private void SetLoopEndAtCursor()
        {
            LoopEnd = PlayheadSample;
        }

        private void UpdatePlayheadFromPlayback()
        {
            if (!IsPreviewPlaying)
            {
                return;
            }

            if (SampleRate <= 0)
            {
                return;
            }

            var pos = _playbackService.Position;
            var sample = (int)Math.Round(pos.TotalSeconds * SampleRate);
            if (sample < 0) sample = 0;
            if (TotalSamples > 0 && sample > TotalSamples - 1) sample = TotalSamples - 1;
            PlayheadSample = sample;

            if (!_playbackService.IsPlaying)
            {
                // Playback ended/stopped.
                _playheadTimer.Stop();
                IsPreviewPlaying = false;
            }
        }

        [RelayCommand]
        private void ZoomIn()
        {
            SamplesPerPixel = Math.Max(0.25, SamplesPerPixel * 0.8);
        }

        [RelayCommand]
        private void ZoomOut()
        {
            SamplesPerPixel = Math.Min(16384, SamplesPerPixel * 1.25);
        }

        [RelayCommand]
        private void CenterOnLoop()
        {
            if (LoopEnd <= LoopStart || TotalSamples <= 0)
            {
                return;
            }

            var span = Math.Max(LoopEnd - LoopStart, 1);
            SamplesPerPixel = Math.Clamp(span / 400.0, 0.25, 4096);
            var pre = (int)Math.Round(50 * SamplesPerPixel);
            ViewStartSample = Math.Clamp(LoopStart - pre, 0, Math.Max(0, TotalSamples - 1));
            PlayheadSample = LoopStart;
        }

        [RelayCommand]
        private void ClearLoopPoints()
        {
            LoopStart = 0;
            LoopEnd = Math.Max(LoopStart + 1, TotalSamples);
            HasChanges = true;
            Status = "Loop points cleared.";
        }

        [RelayCommand]
        private async Task AnalyzeAudio()
        {
            AnalysisStatus = "Analyzing...";
            try
            {
                var ffmpegPath = ResolveFfmpegPath();
                if (string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    AnalysisStatus = "ffmpeg not found";
                    return;
                }

                // If it's a rooted path, require it to exist. If it's just "ffmpeg.exe", allow PATH lookup.
                if (Path.IsPathRooted(ffmpegPath) && !File.Exists(ffmpegPath))
                {
                    AnalysisStatus = "ffmpeg not found";
                    return;
                }

                string inputPath = _path;
                string? tempWav = null;
                try
                {
                    if (_isScd)
                    {
                        tempWav = DecodeScdToTempWav(out _);
                        if (string.IsNullOrWhiteSpace(tempWav) || !File.Exists(tempWav))
                        {
                            AnalysisStatus = "Could not decode SCD for analysis";
                            return;
                        }

                        inputPath = tempWav;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-hide_banner -nostats -i \"{inputPath}\" -filter:a ebur128=framelog=quiet -f null -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null)
                    {
                        AnalysisStatus = "Could not start ffmpeg";
                        return;
                    }

                    var stdout = await process.StandardOutput.ReadToEndAsync();
                    var stderr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    var text = stdout + "\n" + stderr;
                    var parsed = ParseEbur128(text);
                    if (parsed != null)
                    {
                        AnalysisResult = parsed;
                        AnalysisStatus = "Analysis complete";
                    }
                    else
                    {
                        AnalysisStatus = "Could not parse EBU-R128";
                    }
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(tempWav))
                    {
                        try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                AnalysisStatus = $"Analysis failed: {ex.Message}";
            }
        }

        private async Task<bool> RewriteScdViaReencodeAsync(bool normalize, double targetLufs, bool patchVolume, float volumeFloat)
        {
            var tempWav = Path.Combine(Path.GetTempPath(), $"loop_edit_{Guid.NewGuid():N}.wav");
            var tempWavNormalized = Path.Combine(Path.GetTempPath(), $"loop_edit_norm_{Guid.NewGuid():N}.wav");
            try
            {
                var vgmstreamPath = ResolveVgmstreamPath();
                if (!File.Exists(vgmstreamPath))
                {
                    Status = "vgmstream-cli.exe not found.";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = vgmstreamPath,
                    Arguments = $"-i -o \"{tempWav}\" \"{_path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Status = "Could not start vgmstream.";
                        return false;
                    }

                    await process.StandardOutput.ReadToEndAsync();
                    await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0 || !File.Exists(tempWav))
                    {
                        Status = "vgmstream failed to decode SCD.";
                        return false;
                    }
                }

                var wavForEncode = tempWav;
                if (normalize)
                {
                    Status = "Normalizing audio...";
                    var ok = await Services.ScdQuickNormalizeService.NormalizeWavToNewFileAsync(tempWav, tempWavNormalized, targetLufs);
                    if (ok)
                    {
                        wavForEncode = tempWavNormalized;
                    }
                    else
                    {
                        Status = "Normalization failed (continuing).";
                    }
                }

                // Ensure the encoded SCD uses the editor's loop points.
                WavLoopTagWriter.Write(wavForEncode, LoopStart, LoopEnd);

                var encoder = new ScdEncoderService();
                var resultPath = await encoder.EncodeAsync(_path, wavForEncode, quality: 10, fullLoop: false);
                File.Copy(resultPath, _path, overwrite: true);

                if (patchVolume)
                {
                    try
                    {
                        ScdVolumePatcher.PatchVolume(_path, volumeFloat);
                    }
                    catch
                    {
                        // Best-effort only; don't fail save.
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Status = $"Rebuild failed: {ex.Message}";
                return false;
            }
            finally
            {
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
                try { if (File.Exists(tempWavNormalized)) File.Delete(tempWavNormalized); } catch { }
            }
        }

        private Task<bool> NormalizeWavInPlaceAsync(string wavPath, double targetLufsValue)
            => Services.ScdQuickNormalizeService.NormalizeWavInPlaceAsync(wavPath, targetLufsValue);

        private void UpdateTimes()
        {
            LoopStartTime = FormatTime(LoopStart, SampleRate);
            LoopEndTime = FormatTime(LoopEnd, SampleRate);
            DurationDisplay = FormatTime(TotalSamples, SampleRate);
            TotalSamplesDisplay = $"{TotalSamples:N0} samples";
        }

        private void UpdateWaveformWidth()
        {
            if (TotalSamples <= 0)
            {
                WaveformPixelWidth = 1200;
                return;
            }

            // Base width on current zoom level
            // When zoomed out, waveform fits in viewport; when zoomed in, it extends for scrolling
            var spp = Math.Clamp(SamplesPerPixel, 0.25, 16384);
            var target = Math.Ceiling(TotalSamples / spp);
            
            // Use calculated width - MinWidth in XAML ensures it fills viewport when smaller
            WaveformPixelWidth = target;
        }

        private void TryPopulateMetadataFromDecoded()
        {
            var vgmstreamPath = ResolveVgmstreamPath();
            if (!File.Exists(vgmstreamPath))
            {
                return;
            }

            var tempWav = Path.Combine(Path.GetTempPath(), $"loop_meta_{Guid.NewGuid():N}.wav");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = vgmstreamPath,
                    Arguments = $"-i -o \"{tempWav}\" \"{_path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return;
                }

                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && File.Exists(tempWav))
                {
                    var (_, meta) = WavLoopTagReader.Read(tempWav);
                    if (meta != null)
                    {
                        SampleRate = meta.SampleRate;
                        Channels = meta.Channels;
                        TotalSamples = meta.TotalSamples;
                        LoopEnd = Math.Max(LoopEnd, Math.Max(LoopStart + 1, TotalSamples));
                    }
                }
            }
            catch
            {
                // best effort only
            }
            finally
            {
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
            }
        }

        private string? DecodeScdToTempWav(out LoopPoints? decodedLoop)
        {
            decodedLoop = null;
            var vgmstreamPath = ResolveVgmstreamPath();
            if (!File.Exists(vgmstreamPath))
            {
                return null;
            }

            var tempWav = Path.Combine(Path.GetTempPath(), $"loop_wave_{Guid.NewGuid():N}.wav");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = vgmstreamPath,
                    Arguments = $"-i -o \"{tempWav}\" \"{_path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return null;
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && File.Exists(tempWav))
                {
                    decodedLoop = ParseVgmstreamLoopInfo(stdout + "\n" + stderr);
                    return tempWav;
                }
            }
            catch
            {
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
            }

            return null;
        }

        private static string FormatTime(int samples, int sampleRate)
        {
            if (sampleRate <= 0)
            {
                return "--:--";
            }

            var seconds = samples / (double)sampleRate;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.ToString(ts.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss\.fff", CultureInfo.InvariantCulture);
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

        private static string ResolveVgmstreamPath()
        {
            var baseDir = AppContext.BaseDirectory;
            var local = Path.Combine(baseDir, "vgmstream-cli.exe");
            if (File.Exists(local))
            {
                return local;
            }

            var repoLevel = Path.Combine(baseDir, "..", "..", "..", "..", "vgmstream", "vgmstream-cli.exe");
            if (File.Exists(repoLevel))
            {
                return Path.GetFullPath(repoLevel);
            }

            return "vgmstream-cli.exe";
        }

        private static string? ResolveFfmpegPath()
        {
            var baseDir = AppContext.BaseDirectory;

            var env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            {
                return env;
            }

            var local = Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe");
            if (File.Exists(local))
            {
                return local;
            }

            // Dev run: output is ...\SCDToolkit.Desktop\bin\Debug\netX\ so go up 4 levels to the repo root.
            var repoLevel = Path.Combine(baseDir, "..", "..", "..", "..", "ffmpeg", "bin", "ffmpeg.exe");
            var repoFull = Path.GetFullPath(repoLevel);
            if (File.Exists(repoFull))
            {
                return repoFull;
            }

            return "ffmpeg.exe";
        }

        private static string? ParseLufs(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string? integrated = null;
            string? truePeak = null;
            string? lra = null;

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("I:", StringComparison.OrdinalIgnoreCase))
                {
                    integrated = trimmed;
                }
                else if (trimmed.StartsWith("T:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("TP:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("True peak", StringComparison.OrdinalIgnoreCase))
                {
                    truePeak = trimmed;
                }
                else if (trimmed.StartsWith("LRA:", StringComparison.OrdinalIgnoreCase))
                {
                    lra = trimmed;
                }
            }

            if (integrated == null && truePeak == null && lra == null)
            {
                return null;
            }

            return $"Integrated: {integrated ?? "n/a"}\nTrue Peak: {truePeak ?? "n/a"}\nLoudness Range: {lra ?? "n/a"}";
        }
        private static string? ParseEbur128(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Typical ffmpeg ebur128 summary contains lines like:
            //   I:         -20.3 LUFS
            //   LRA:        22.5 LU
            //   True peak:   -1.2 dBFS
            var iMatch = Regex.Match(text, @"\bI:\s*(-?\d+(?:\.\d+)?)\s*LUFS\b", RegexOptions.IgnoreCase);
            var lraMatch = Regex.Match(text, @"\bLRA:\s*(\d+(?:\.\d+)?)\s*LU\b", RegexOptions.IgnoreCase);
            var tpMatch = Regex.Match(text, @"\bTrue\s*peak:\s*(-?\d+(?:\.\d+)?)\s*dBFS\b", RegexOptions.IgnoreCase);

            double? integrated = null;
            double? lra = null;
            double? truePeak = null;

            if (iMatch.Success && double.TryParse(iMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var iVal))
            {
                integrated = iVal;
            }
            if (lraMatch.Success && double.TryParse(lraMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lraVal))
            {
                lra = lraVal;
            }
            if (tpMatch.Success && double.TryParse(tpMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tpVal))
            {
                truePeak = tpVal;
            }

            if (integrated == null && lra == null && truePeak == null)
            {
                return null;
            }

            var lines = new List<string>();
            if (integrated != null)
            {
                lines.Add($"Integrated: {integrated.Value:0.0} LUFS");
                var target = -14.0;
                var gain = target - integrated.Value;
                lines.Add($"Suggested gain to {target:0.0} LUFS: {gain:+0.0;-0.0;0.0} dB");
            }
            if (lra != null)
            {
                lines.Add($"LRA: {lra.Value:0.0} LU");
            }
            if (truePeak != null)
            {
                lines.Add($"True peak: {truePeak.Value:0.0} dBFS");
            }

            return string.Join("\n", lines);
        }
    }
}
#pragma warning restore MVVMTK0034
