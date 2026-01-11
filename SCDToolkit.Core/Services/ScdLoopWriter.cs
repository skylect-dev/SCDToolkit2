using System;
using System.IO;
using SCDToolkit.Core.Utils;

namespace SCDToolkit.Core.Services
{
    /// <summary>
    /// Writes loop points directly into an existing SCD entry when possible.
    /// </summary>
    public static class ScdLoopWriter
    {
        public static bool TryWriteLoopPoints(string scdPath, int loopStart, int loopEnd, out string? error)
        {
            error = null;

            if (loopStart < 0 || loopEnd <= loopStart)
            {
                error = "Loop end must be greater than start.";
                return false;
            }

            if (!File.Exists(scdPath))
            {
                error = "SCD file not found.";
                return false;
            }

            try
            {
                var data = File.ReadAllBytes(scdPath);
                if (!TryLocateFirstEntry(data, out var entryBegin, out var entryEnd, out var codec, out var headersOffset, out var headersEntries))
                {
                    error = "Unable to locate SCD entry.";
                    return false;
                }

                if (codec != 0x6)
                {
                    error = "Only Vorbis SCD entries are supported for loop editing.";
                    return false;
                }

                var entrySize = entryEnd - entryBegin;
                var entry = new byte[entrySize];
                Array.Copy(data, entryBegin, entry, 0, entrySize);

                var auxChunkCount = ScdBinaryHelpers.Read(entry, 32, 0x1c);
                if (auxChunkCount == 0)
                {
                    error = "Entry is missing aux chunk with sample-accurate loop data.";
                    return false;
                }

                ScdBinaryHelpers.Write(entry, loopStart, 32, 0x28);
                ScdBinaryHelpers.Write(entry, loopEnd, 32, 0x2c);

                // Best-effort: if mark entries exist, keep them aligned to the new loop start/end
                var markEntries = ScdBinaryHelpers.Read(entry, 32, 0x30);
                for (var i = 0; i < markEntries; i++)
                {
                    var pos = 0x34 + i * 0x04;
                    if (pos + 4 <= entry.Length)
                    {
                        var markValue = i == 0 ? loopStart : 0;
                        ScdBinaryHelpers.Write(entry, markValue, 32, pos);
                    }
                }

                Array.Copy(entry, 0, data, entryBegin, entrySize);
                File.WriteAllBytes(scdPath, data);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryLocateFirstEntry(byte[] data, out int entryBegin, out int entryEnd, out uint codec, out uint headersOffset, out uint headersEntries)
        {
            entryBegin = 0;
            entryEnd = 0;
            codec = 0xFFFFFFFF;
            headersOffset = 0;
            headersEntries = 0;

            if (data.Length < 0x20)
            {
                return false;
            }

            var tablesOffset = ScdBinaryHelpers.Read(data, 16, 0x0e);
            headersEntries = ScdBinaryHelpers.Read(data, 16, (int)tablesOffset + 0x04);
            headersOffset = ScdBinaryHelpers.Read(data, 32, (int)tablesOffset + 0x0c);

            for (var i = 0; i < headersEntries; i++)
            {
                entryBegin = (int)ScdBinaryHelpers.Read(data, 32, (int)headersOffset + i * 0x04);
                codec = ScdBinaryHelpers.Read(data, 32, entryBegin + 0x0c);
                if (codec != 0xFFFFFFFF)
                {
                    entryEnd = i == headersEntries - 1
                        ? data.Length
                        : (int)ScdBinaryHelpers.Read(data, 32, (int)headersOffset + (i + 1) * 0x04);
                    return true;
                }
            }

            return false;
        }
    }
}
