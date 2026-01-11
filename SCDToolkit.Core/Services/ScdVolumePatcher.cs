using System;
using System.IO;
using SCDToolkit.Core.Utils;

namespace SCDToolkit.Core.Services
{
    public static class ScdVolumePatcher
    {
        public static void PatchVolume(string scdPath, float volumeMultiplier)
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
            if (tableOffset == 0 || tableOffset >= data.Length)
            {
                throw new InvalidDataException("Invalid volume offset table pointer");
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
                    break;
                }

                uint entryOffset = BitConverter.ToUInt32(data, entryPtrPos);
                if (entryOffset == 0)
                {
                    break;
                }

                if (entryOffset >= data.Length)
                {
                    break;
                }

                uint volumeOffset = entryOffset + 8;
                if (volumeOffset + 4 > data.Length)
                {
                    break;
                }

                byte[] volumeBytes = BitConverter.GetBytes(volumeMultiplier);
                Array.Copy(volumeBytes, 0, data, volumeOffset, 4);
                patched++;
            }

            if (patched == 0)
            {
                throw new InvalidDataException("No volume entries patched");
            }

            File.WriteAllBytes(scdPath, data);
        }
    }
}
