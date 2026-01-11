using System;
using System.IO;
using System.Linq;
using SCDToolkit.Core.Models;
using SCDToolkit.Core.Utils;

namespace SCDToolkit.Core.Services
{
    public class ScdParser
    {
        public (LoopPoints? loop, AudioMetadata? meta) ReadScdInfo(string path)
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 0x20)
            {
                return (null, null);
            }

            uint tablesOffset = ScdBinaryHelpers.Read(data, 16, 0x0e);
            uint headersEntries = ScdBinaryHelpers.Read(data, 16, (int)tablesOffset + 0x04);
            uint headersOffset = ScdBinaryHelpers.Read(data, 32, (int)tablesOffset + 0x0c);
            uint codec = GetCodec(headersEntries, headersOffset, data);

            var entry = GetFirstRealEntry(headersEntries, headersOffset, data);
            if (entry == null)
            {
                return (null, null);
            }

            LoopPoints? loop = null;
            AudioMetadata? meta = null;

            if (codec == 0x6) // ogg
            {
                loop = ReadOggLoop(entry);
                meta = ReadOggMetadata(entry);
            }
            else if (codec == 0x0c) // msadpcm
            {
                loop = ReadMsAdpcmLoop(entry);
            }

            return (loop, meta);
        }

        public LoopPoints? ReadLoopPoints(string path)
        {
            var (loop, _) = ReadScdInfo(path);
            return loop;
        }

        private static byte[]? GetFirstRealEntry(uint headersEntries, uint headersOffset, byte[] data)
        {
            for (int i = 0; i < headersEntries; i++)
            {
                uint entryBegin = ScdBinaryHelpers.Read(data, 32, (int)headersOffset + i * 0x04);
                uint entryCodec = ScdBinaryHelpers.Read(data, 32, (int)entryBegin + 0x0c);
                if (entryCodec != 0xFFFFFFFF)
                {
                    uint entryEnd = (i == headersEntries - 1)
                        ? (uint)data.Length
                        : ScdBinaryHelpers.Read(data, 32, (int)headersOffset + (i + 1) * 0x04);
                    uint entrySize = entryEnd - entryBegin;
                    byte[] entry = new byte[entrySize];
                    Array.Copy(data, entryBegin, entry, 0, entrySize);
                    return entry;
                }
            }
            return null;
        }

        private static LoopPoints? ReadOggLoop(byte[] entry)
        {
            uint metaOffset = 0;
            
            // The values at 0x10 and 0x14 are BYTE OFFSETS into the OGG data for seeking
            // The ACTUAL sample-accurate loop points should be in the aux chunk at 0x28 and 0x2c
            // However, many SCD files don't have aux chunks, so we need to rely on vgmstream
            // to convert byte offsets to sample counts by parsing the OGG pages
            int loopStart = -1;
            int loopEnd = -1;

            uint auxChunkCount = ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x1c);
            if (auxChunkCount > 0)
            {
                // Use aux chunk for sample-accurate loop points
                loopStart = (int)ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x28);
                loopEnd = (int)ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x2c);
            }
            else
            {
                // No aux chunk - read byte offsets
                // NOTE: These are byte positions, NOT samples
                // The playback service should rely on vgmstream to get accurate sample counts
                loopStart = (int)ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x10);
                loopEnd = (int)ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x14);
            }

            if (loopStart >= 0 && loopEnd > 0 && loopEnd > loopStart)
            {
                return new LoopPoints(loopStart, loopEnd);
            }
            return null;
        }

        private static LoopPoints? ReadMsAdpcmLoop(byte[] entry)
        {
            // MSADPCM entries do not carry loop markers in this format in the provided tools.
            return null;
        }

        private static AudioMetadata? ReadOggMetadata(byte[] entry)
        {
            try
            {
                uint metaOffset = 0;
                // Channels is at 0x04 (8-bit), Sample Rate is at 0x08 (32-bit)
                int channels = (int)ScdBinaryHelpers.Read(entry, 8, (int)metaOffset + 0x04);
                int sampleRate = (int)ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x08);
                
                // Get total samples from aux chunk if available
                int totalSamples = 0;
                uint auxChunkCount = ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x1c);
                if (auxChunkCount > 0)
                {
                    totalSamples = (int)ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x2c);
                }

                if (sampleRate > 0 && channels > 0)
                {
                    return new AudioMetadata(sampleRate, channels, totalSamples);
                }
            }
            catch { }
            
            return null;
        }

        private static uint GetCodec(uint headersEntries, uint headersOffset, byte[] data)
        {
            uint entryBegin;
            uint entryCodec = 0xFFFFFFFF;
            for (int i = 0; i < headersEntries; i++)
            {
                entryBegin = ScdBinaryHelpers.Read(data, 32, (int)headersOffset + i * 0x04);
                entryCodec = ScdBinaryHelpers.Read(data, 32, (int)entryBegin + 0x0c);
                if (entryCodec != 0xFFFFFFFF)
                {
                    break;
                }
            }
            return entryCodec;
        }
    }
}
