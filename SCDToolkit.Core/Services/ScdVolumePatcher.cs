using System;
using System.IO;
using SCDToolkit.Core.Utils;

namespace SCDToolkit.Core.Services
{
    public static class ScdVolumePatcher
    {
        public static void PatchVolume(string scdPath, float volumeMultiplier, bool verbose = false)
        {
            if (!File.Exists(scdPath))
            {
                throw new FileNotFoundException("SCD file not found", scdPath);
            }

            var data = File.ReadAllBytes(scdPath);
            
            // Read offset table pointer at 0x50
            if (data.Length < 0x54)
            {
                throw new InvalidDataException("SCD file too small");
            }

            uint tableOffset = ScdBinaryHelpers.Read(data, 32, 0x50);
                        if (verbose)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] tableOffset from 0x50: 0x{tableOffset:X}");
                        }
            
            if (tableOffset == 0 || tableOffset >= data.Length)
            {
                throw new InvalidDataException($"Invalid volume offset table pointer: 0x{tableOffset:X} (file size: 0x{data.Length:X})");
            }

            // The table at tableOffset contains offsets into the SCD.
            // For each offset, the 8th byte pointed to by that offset is a float controlling in-game volume.
            // Patch every referenced entry (best-effort, stop on invalid terminator/out-of-bounds).
            var patched = 0;
            for (int i = 0; i < 1024; i++)
            {
                var entryPtrPos = (int)tableOffset + i * 4;
                if (entryPtrPos + 4 > data.Length)
                {
                    if (verbose) System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Entry {i}: out of bounds");
                    break;
                }

                uint entryOffset = ScdBinaryHelpers.Read(data, 32, entryPtrPos);

                if (verbose)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Entry {i} at 0x{entryPtrPos:X}: entryOffset=0x{entryOffset:X}");
                }

                if (entryOffset == 0)
                {
                    if (verbose) System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Entry {i}: zero terminator");
                    break;
                }

                if (entryOffset >= data.Length)
                {
                    if (verbose) System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Entry {i}: offset out of bounds");
                    break;
                }

                uint volumeOffset = entryOffset + 8;
                if (volumeOffset + 4 > data.Length)
                {
                    if (verbose) System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Entry {i}: volume offset out of bounds");
                    break;
                }

                // Read old volume for logging
                var oldVolume = BitConverter.ToSingle(data, (int)volumeOffset);

                // Write new volume (floats are little-endian in SCD)
                var volumeBytes = BitConverter.GetBytes(volumeMultiplier);
                Array.Copy(volumeBytes, 0, data, (int)volumeOffset, 4);

                if (verbose)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Entry {i}: Patched volume at 0x{volumeOffset:X} from {oldVolume:F3} to {volumeMultiplier:F3}");
                }

                patched++;
            }

            if (patched == 0)
            {
                // Fallback: Some SCDs store the volume at a fixed absolute offset (0x128)
                const int absoluteVolumeOffset = 0x128;
                if (absoluteVolumeOffset + 4 <= data.Length)
                {
                    var oldVolume = BitConverter.ToSingle(data, absoluteVolumeOffset);
                    var volumeBytes = BitConverter.GetBytes(volumeMultiplier);
                    Array.Copy(volumeBytes, 0, data, absoluteVolumeOffset, 4);
                    patched = 1;

                    if (verbose)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Fallback: patched absolute volume at 0x{absoluteVolumeOffset:X} from {oldVolume:F3} to {volumeMultiplier:F3}");
                    }
                }
                else
                {
                    throw new InvalidDataException($"No volume entries patched (tableOffset was 0x{tableOffset:X}, checked {Math.Min(1024, (data.Length - (int)tableOffset) / 4)} entries)");
                }
            }

            if (verbose)
            {
                System.Diagnostics.Debug.WriteLine($"[ScdVolumePatcher] Successfully patched {patched} volume entries");
            }

            File.WriteAllBytes(scdPath, data);
        }
    }
}
