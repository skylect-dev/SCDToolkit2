using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SCDToolkit.Desktop.Services
{
    public readonly record struct ScdNormalizationOptions(
        bool Normalize,
        double TargetLufs,
        bool PatchVolume,
        float VolumeFloat)
    {
        public string ProfileKey => $"normalize={Normalize};targetLufs={TargetLufs:0.###};patchVolume={PatchVolume};volumeFloat={VolumeFloat:0.###}";
    }

    public readonly record struct ScdNormalizationProgress(
        int Total,
        int Completed,
        int Skipped,
        int Errors,
        string CurrentFile,
        string Message);

    public readonly record struct ScdNormalizationResult(
        int Total,
        int Completed,
        int Skipped,
        int Errors,
        IReadOnlyList<string> ErrorMessages);

    public static class ScdNormalizationManager
    {
        private static readonly object Gate = new();

        public static void RecordAppliedProfile(string path, string profileKey, bool userTuned = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (string.IsNullOrWhiteSpace(profileKey)) profileKey = "unknown";

            var full = Canon(path);
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists) return;

                lock (Gate)
                {
                    var config = ConfigLoader.Load();
                    var cache = GetCache(config);
                    cache[full] = NewEntry(info, profileKey, userTuned);
                    ConfigLoader.Save(config);
                }
            }
            catch
            {
                // Best-effort.
            }
        }

        private static Dictionary<string, NormalizationCacheEntry> GetCache(AppConfig config)
        {
            config.NormalizationCache ??= new Dictionary<string, NormalizationCacheEntry>(StringComparer.OrdinalIgnoreCase);
            return config.NormalizationCache;
        }

        private static NormalizationCacheEntry NewEntry(FileInfo info, string profileKey, bool userTuned)
        {
            return new NormalizationCacheEntry
            {
                FileLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                FileLength = info.Length,
                ProfileKey = profileKey,
                IsUserTuned = userTuned,
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        private static string Canon(string path) => Path.GetFullPath(path);

        public static bool IsUserTuned(string path)
        {
            var full = Canon(path);
            lock (Gate)
            {
                var config = ConfigLoader.Load();
                var cache = GetCache(config);
                return cache.TryGetValue(full, out var entry) && entry.IsUserTuned;
            }
        }

        public static void MarkUserTuned(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Canon(path);

            try
            {
                var info = new FileInfo(full);
                if (!info.Exists) return;

                lock (Gate)
                {
                    var config = ConfigLoader.Load();
                    var cache = GetCache(config);
                    cache[full] = NewEntry(info, profileKey: "user_tuned", userTuned: true);
                    ConfigLoader.Save(config);
                }
            }
            catch
            {
                // Best-effort.
            }
        }

        private static bool ShouldSkipInternal(string fullPath, ScdNormalizationOptions options, out string reason)
        {
            reason = string.Empty;

            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    reason = "missing";
                    return true;
                }

                var config = ConfigLoader.Load();
                var cache = GetCache(config);
                if (!cache.TryGetValue(fullPath, out var entry))
                {
                    return false;
                }

                if (entry.IsUserTuned)
                {
                    reason = "user-tuned";
                    return true;
                }

                var sameFile = entry.FileLastWriteUtcTicks == info.LastWriteTimeUtc.Ticks && entry.FileLength == info.Length;
                var sameProfile = string.Equals(entry.ProfileKey ?? string.Empty, options.ProfileKey, StringComparison.OrdinalIgnoreCase);

                if (sameFile && sameProfile)
                {
                    reason = "already-normalized";
                    return true;
                }

                return false;
            }
            catch
            {
                // If we can't decide, don't skip.
                return false;
            }
        }

        public static async Task<(bool Success, bool Skipped, string Message)> NormalizeOneAsync(
            string scdPath,
            ScdNormalizationOptions options,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scdPath)) return (false, true, "empty path");
            var full = Canon(scdPath);

            if (!full.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                return (false, true, "not scd");
            }

            if (!force)
            {
                lock (Gate)
                {
                    if (ShouldSkipInternal(full, options, out var reason))
                    {
                        return (true, true, reason);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            await ScdQuickNormalizeService.NormalizeScdInPlaceAsync(
                full,
                normalize: options.Normalize,
                targetLufs: options.TargetLufs,
                patchVolume: options.PatchVolume,
                volumeFloat: options.VolumeFloat);

            // Update cache after successful write.
            try
            {
                var info = new FileInfo(full);
                if (info.Exists)
                {
                    lock (Gate)
                    {
                        var config = ConfigLoader.Load();
                        var cache = GetCache(config);
                        cache[full] = NewEntry(info, options.ProfileKey, userTuned: false);
                        ConfigLoader.Save(config);
                    }
                }
            }
            catch
            {
                // Best-effort.
            }

            return (true, false, "normalized");
        }

        public static async Task<ScdNormalizationResult> NormalizeManyAsync(
            IEnumerable<string> scdPaths,
            ScdNormalizationOptions options,
            int maxDegreeOfParallelism,
            Action<ScdNormalizationProgress>? progress = null,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            var all = scdPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Canon)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = all.Count;
            if (total == 0)
            {
                return new ScdNormalizationResult(0, 0, 0, 0, Array.Empty<string>());
            }

            maxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism);

            var errors = new ConcurrentBag<string>();
            var completed = 0;
            var skipped = 0;
            var errorCount = 0;

            using var gate = new SemaphoreSlim(maxDegreeOfParallelism);

            var tasks = all.Select(async path =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    progress?.Invoke(new ScdNormalizationProgress(total, completed, skipped, errorCount, path, "starting"));

                    if (!force)
                    {
                        lock (Gate)
                        {
                            if (ShouldSkipInternal(path, options, out var reason))
                            {
                                Interlocked.Increment(ref skipped);
                                progress?.Invoke(new ScdNormalizationProgress(total, completed, skipped, errorCount, path, $"skipped:{reason}"));
                                return;
                            }
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    await ScdQuickNormalizeService.NormalizeScdInPlaceAsync(
                        path,
                        normalize: options.Normalize,
                        targetLufs: options.TargetLufs,
                        patchVolume: options.PatchVolume,
                        volumeFloat: options.VolumeFloat);

                    try
                    {
                        var info = new FileInfo(path);
                        if (info.Exists)
                        {
                            lock (Gate)
                            {
                                var config = ConfigLoader.Load();
                                var cache = GetCache(config);
                                cache[path] = NewEntry(info, options.ProfileKey, userTuned: false);
                                ConfigLoader.Save(config);
                            }
                        }
                    }
                    catch
                    {
                        // Best-effort
                    }

                    Interlocked.Increment(ref completed);
                    progress?.Invoke(new ScdNormalizationProgress(total, completed, skipped, errorCount, path, "done"));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                    progress?.Invoke(new ScdNormalizationProgress(total, completed, skipped, errorCount, path, "error"));
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            return new ScdNormalizationResult(total, completed, skipped, errorCount, errors.ToArray());
        }
    }
}
