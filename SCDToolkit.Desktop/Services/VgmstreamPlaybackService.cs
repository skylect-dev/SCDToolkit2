using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NAudio.Wave;
using SCDToolkit.Core.Abstractions;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Desktop.Services
{
    public sealed class VgmstreamPlaybackService : IPlaybackService, IDisposable
    {
        private readonly string _vgmstreamPath;
        private IWavePlayer? _output;
        private WaveStream? _reader;
        private AudioFileReader? _sourceReader;
        private string? _tempDecodedPath;
        private LoopPoints? _vgmstreamLoopInfo; // Loop info extracted from vgmstream output
        private readonly object _gate = new();
        private double _volume = 0.7;
        private bool _isPlaying;
        private bool _loopEnabled = true;
        private LoopPoints? _currentLoopPoints;

        public bool LoopEnabled
        {
            get => _loopEnabled;
            set
            {
                if (_loopEnabled != value)
                {
                    _loopEnabled = value;
                    UpdateLoopState();
                }
            }
        }

        public string? GetDecodedPath()
        {
            lock (_gate)
            {
                return _tempDecodedPath;
            }
        }

        /// <summary>
        /// Gets the loop points extracted from vgmstream's output (for SCD files).
        /// Returns null if no loop info was found or if the file is not an SCD.
        /// </summary>
        public LoopPoints? GetVgmstreamLoopInfo()
        {
            lock (_gate)
            {
                return _vgmstreamLoopInfo;
            }
        }

        private void UpdateLoopState()
        {
            lock (_gate)
            {
                if (_reader is LoopingWaveStream loopStream)
                {
                    loopStream.LoopEnabled = _loopEnabled;
                }
            }
        }

        public VgmstreamPlaybackService(string? vgmstreamPath = null)
        {
            _vgmstreamPath = ResolveVgmstreamPath(vgmstreamPath);
        }

        public Task PlayAsync(LibraryItem item, LoopPoints loopPoints)
        {
            return Task.Run(() => PlayInternal(item, loopPoints));
        }

        public Task PauseAsync()
        {
            lock (_gate)
            {
                _output?.Pause();
                _isPlaying = false;
            }
            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            lock (_gate)
            {
                if (_output != null && _reader != null)
                {
                    _output.Play();
                    _isPlaying = true;
                }
            }
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            lock (_gate)
            {
                DisposePlayback();
            }
            return Task.CompletedTask;
        }

        public Task SeekAsync(TimeSpan position)
        {
            lock (_gate)
            {
                if (_reader != null && position >= TimeSpan.Zero && position <= Duration)
                {
                    _reader.CurrentTime = position;
                }
            }
            return Task.CompletedTask;
        }

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 1);
                if (_sourceReader != null)
                {
                    _sourceReader.Volume = (float)_volume;
                }
            }
        }

        public TimeSpan Position
        {
            get
            {
                lock (_gate)
                {
                    return _reader?.CurrentTime ?? TimeSpan.Zero;
                }
            }
        }

        public TimeSpan Duration
        {
            get
            {
                lock (_gate)
                {
                    return _reader?.TotalTime ?? TimeSpan.Zero;
                }
            }
        }

        public bool IsPlaying
        {
            get
            {
                lock (_gate)
                {
                    return _isPlaying;
                }
            }
        }

        private void PlayInternal(LibraryItem item, LoopPoints loopPoints)
        {
            lock (_gate)
            {
                DisposePlayback();

                string sourcePath = item.Path;
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException("File not found", sourcePath);
                }

                bool isScd = string.Equals(Path.GetExtension(sourcePath), ".scd", StringComparison.OrdinalIgnoreCase);
                string playbackPath = isScd ? DecodeWithVgmstream(sourcePath) : sourcePath;

                _reader = CreateReader(playbackPath, loopPoints);
                _output = new WaveOutEvent();
                _output.Init(_reader);
                _output.Play();
                _isPlaying = true;
                _output.PlaybackStopped += (_, _) => _isPlaying = false;
            }
        }

        private WaveStream CreateReader(string path, LoopPoints loopPoints)
        {
            _sourceReader = new AudioFileReader(path)
            {
                Volume = (float)_volume
            };

            _currentLoopPoints = loopPoints;

            // If loopPoints were passed and valid, use them
            if (loopPoints != null && loopPoints.EndSample > loopPoints.StartSample && loopPoints.EndSample > 0)
            {
                var loopStart = loopPoints.StartSample;
                var loopEnd = loopPoints.EndSample;
                var loopStream = new LoopingWaveStream(_sourceReader, loopStart, loopEnd);
                loopStream.LoopEnabled = _loopEnabled;
                return loopStream;
            }

            // If this is a decoded WAV from vgmstream or has loop tags, check for loops
            if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                var (loop, _) = SCDToolkit.Core.Services.WavLoopTagReader.Read(path);
                if (loop != null && loop.EndSample > loop.StartSample)
                {
                    _currentLoopPoints = loop;
                    var loopStream = new LoopingWaveStream(_sourceReader, loop.StartSample, loop.EndSample);
                    loopStream.LoopEnabled = _loopEnabled;
                    return loopStream;
                }
            }

            // No loop
            return _sourceReader;
        }

        private string DecodeWithVgmstream(string scdPath)
        {
            if (!File.Exists(_vgmstreamPath))
            {
                throw new FileNotFoundException("vgmstream-cli.exe not found", _vgmstreamPath);
            }

            string tempPath = Path.Combine(Path.GetTempPath(), $"scd_decode_{Guid.NewGuid():N}.wav");
            var psi = new ProcessStartInfo
            {
                FileName = _vgmstreamPath,
                Arguments = $"-i -o \"{tempPath}\" \"{scdPath}\"", // -i = no looping
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string output = string.Empty;
            using (var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start vgmstream-cli"))
            {
                output = process.StandardOutput.ReadToEnd();
                output += process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode != 0 || !File.Exists(tempPath))
                {
                    throw new InvalidOperationException("vgmstream-cli failed to decode the file.");
                }
            }

            // Parse vgmstream output to extract accurate loop points
            _vgmstreamLoopInfo = ParseVgmstreamLoopInfo(output);

            _tempDecodedPath = tempPath;
            return tempPath;
        }

        private static LoopPoints? ParseVgmstreamLoopInfo(string vgmstreamOutput)
        {
            // Parse output like:
            // loop start: 2734287 samples (0:56.964 seconds)
            // loop end: 7459454 samples (2:35.405 seconds)
            var loopStartMatch = Regex.Match(vgmstreamOutput, @"loop start:\s*(\d+)\s*samples");
            var loopEndMatch = Regex.Match(vgmstreamOutput, @"loop end:\s*(\d+)\s*samples");

            if (loopStartMatch.Success && loopEndMatch.Success)
            {
                int loopStart = int.Parse(loopStartMatch.Groups[1].Value);
                int loopEnd = int.Parse(loopEndMatch.Groups[1].Value);
                return new LoopPoints(loopStart, loopEnd);
            }

            return null;
        }

        private static string ResolveVgmstreamPath(string? overridePath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath;
            }

            // Prefer local tools next to the app; fall back to repo-level vgmstream folder.
            var baseDir = AppContext.BaseDirectory;
            var local = Path.Combine(baseDir, "vgmstream-cli.exe");
            if (File.Exists(local))
            {
                return local;
            }

            var fallback = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "vgmstream", "vgmstream-cli.exe"));
            return fallback;
        }

        private void DisposePlayback()
        {
            try
            {
                _output?.Stop();
            }
            catch
            {
                // ignore
            }

            _output?.Dispose();
            _reader?.Dispose();
            _sourceReader?.Dispose();
            _output = null;
            _reader = null;
            _sourceReader = null;
            _isPlaying = false;

            if (!string.IsNullOrWhiteSpace(_tempDecodedPath) && File.Exists(_tempDecodedPath))
            {
                try
                {
                    File.Delete(_tempDecodedPath);
                }
                catch
                {
                    // best effort
                }
            }

            _tempDecodedPath = null;
            _vgmstreamLoopInfo = null;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                DisposePlayback();
            }
        }

        private sealed class LoopingWaveStream : WaveStream
        {
            private readonly WaveStream _source;
            private readonly long _loopStartBytes;
            private readonly long _loopEndBytes;
            private readonly bool _hasLoop;
            public bool LoopEnabled { get; set; } = true;

            public LoopingWaveStream(WaveStream source, int loopStartSamples, int loopEndSamples)
            {
                _source = source;
                int blockAlign = source.WaveFormat.BlockAlign;
                _loopStartBytes = (long)loopStartSamples * blockAlign;
                _loopEndBytes = (long)loopEndSamples * blockAlign;
                if (_loopEndBytes <= 0 || _loopEndBytes > source.Length)
                {
                    _loopEndBytes = source.Length;
                }
                _hasLoop = _loopEndBytes > _loopStartBytes && _loopEndBytes <= source.Length;
            }

            public override WaveFormat WaveFormat => _source.WaveFormat;

            public override long Length => _source.Length;

            public override long Position
            {
                get => _source.Position;
                set => _source.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (!_hasLoop || !LoopEnabled)
                {
                    return _source.Read(buffer, offset, count);
                }

                int totalRead = 0;
                while (totalRead < count)
                {
                    if (_source.Position >= _loopEndBytes)
                    {
                        _source.Position = _loopStartBytes;
                    }

                    int required = count - totalRead;
                    int read = _source.Read(buffer, offset + totalRead, required);
                    if (read == 0)
                    {
                        if (LoopEnabled)
                        {
                            _source.Position = _loopStartBytes;
                            continue;
                        }
                        break;
                    }
                    totalRead += read;
                }

                return totalRead;
            }
        }
    }
}
