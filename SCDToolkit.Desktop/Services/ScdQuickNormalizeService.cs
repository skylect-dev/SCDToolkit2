using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SCDToolkit.Core.Models;
using SCDToolkit.Core.Services;

namespace SCDToolkit.Desktop.Services
{
    public static class ScdQuickNormalizeService
    {
        public static async Task<bool> NormalizeWavInPlaceAsync(string wavPath, double targetLufsValue)
        {
            if (string.IsNullOrWhiteSpace(wavPath)) throw new ArgumentException("Path is required", nameof(wavPath));
            if (!File.Exists(wavPath)) throw new FileNotFoundException("WAV not found", wavPath);

            var tempOut = Path.Combine(Path.GetTempPath(), $"wav_norm_{Guid.NewGuid():N}.wav");
            try
            {
                if (!await NormalizeWavToNewFileAsync(wavPath, tempOut, targetLufsValue))
                {
                    return false;
                }

                File.Copy(tempOut, wavPath, overwrite: true);
                return true;
            }
            finally
            {
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            }
        }

        public static async Task NormalizeScdInPlaceAsync(
            string scdPath,
            bool normalize,
            double targetLufs,
            bool patchVolume,
            float volumeFloat)
        {
            if (string.IsNullOrWhiteSpace(scdPath)) throw new ArgumentException("Path is required", nameof(scdPath));
            if (!File.Exists(scdPath)) throw new FileNotFoundException("SCD not found", scdPath);
            if (!scdPath.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Quick normalize currently supports .scd files only.");

            var tempWav = Path.Combine(Path.GetTempPath(), $"scd_norm_{Guid.NewGuid():N}.wav");
            var tempWavNormalized = Path.Combine(Path.GetTempPath(), $"scd_norm_out_{Guid.NewGuid():N}.wav");

            try
            {
                var (loop, _) = new ScdParser().ReadScdInfo(scdPath);
                var loopStart = loop?.StartSample ?? 0;
                var loopEnd = loop?.EndSample ?? 0;

                var vgmstreamPath = ResolveVgmstreamPath();
                if (string.IsNullOrWhiteSpace(vgmstreamPath) || !File.Exists(vgmstreamPath))
                    throw new FileNotFoundException("vgmstream-cli.exe not found", vgmstreamPath);

                // Decode to WAV
                var psi = new ProcessStartInfo
                {
                    FileName = vgmstreamPath,
                    Arguments = $"-i -o \"{tempWav}\" \"{scdPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) throw new InvalidOperationException("Could not start vgmstream.");
                    await process.StandardOutput.ReadToEndAsync();
                    await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0 || !File.Exists(tempWav))
                        throw new InvalidOperationException("vgmstream failed to decode SCD.");
                }

                var wavForEncode = tempWav;
                if (normalize)
                {
                    var ok = await NormalizeWavToNewFileAsync(tempWav, tempWavNormalized, targetLufs);
                    if (ok) wavForEncode = tempWavNormalized;
                    else throw new InvalidOperationException("FFmpeg LUFS normalization failed (ffmpeg missing or EBU-R128 parse failed). Set FFMPEG_PATH or bundle ffmpeg.");
                }

                // Preserve loop points via smpl
                if (loopEnd > loopStart)
                {
                    WavLoopTagWriter.Write(wavForEncode, loopStart, loopEnd);
                }

                var encoder = new ScdEncoderService();
                var resultPath = await encoder.EncodeAsync(scdPath, wavForEncode, quality: 10, fullLoop: false);
                File.Copy(resultPath, scdPath, overwrite: true);

                if (patchVolume)
                {
                    try
                    {
                        ScdVolumePatcher.PatchVolume(scdPath, volumeFloat);
                    }
                    catch
                    {
                        // Best-effort.
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
                try { if (File.Exists(tempWavNormalized)) File.Delete(tempWavNormalized); } catch { }
            }
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

        private static string ResolveFfmpegPath()
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

            var repoLevel = Path.Combine(baseDir, "..", "..", "..", "..", "ffmpeg", "bin", "ffmpeg.exe");
            var repoFull = Path.GetFullPath(repoLevel);
            if (File.Exists(repoFull))
            {
                return repoFull;
            }

            return "ffmpeg.exe";
        }

        internal static async Task<bool> NormalizeWavToNewFileAsync(string inputWav, string outputWav, double targetLufsValue)
        {
            var ffmpegPath = ResolveFfmpegPath();
            if (string.IsNullOrWhiteSpace(ffmpegPath) || (!File.Exists(ffmpegPath) && !string.Equals(ffmpegPath, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var integrated = await GetIntegratedLufsAsync(ffmpegPath, inputWav);
            if (integrated == null)
            {
                return false;
            }

            var gainDb = targetLufsValue - integrated.Value;
            gainDb = Math.Clamp(gainDb, -30, 30);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -hide_banner -nostats -i \"{inputWav}\" -vn -map 0:a:0 -filter:a volume={gainDb.ToString("0.###", CultureInfo.InvariantCulture)}dB -c:a pcm_s16le \"{outputWav}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 && File.Exists(outputWav);
        }

        private static async Task<double?> GetIntegratedLufsAsync(string ffmpegPath, string wavPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-hide_banner -nostats -i \"{wavPath}\" -filter:a ebur128=framelog=quiet -f null -",
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

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var text = stdout + "\n" + stderr;
            // Parse: "I:         -20.3 LUFS"
            var match = Regex.Match(text, @"\bI:\s*(-?\d+(?:\.\d+)?)\s*LUFS\b", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }

            return null;
        }
    }
}
