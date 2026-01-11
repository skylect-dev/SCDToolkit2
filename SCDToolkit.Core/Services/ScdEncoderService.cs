using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SCDToolkit.Core.Abstractions;
using SCDToolkit.Core.Utils;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Services
{
    public sealed class ScdEncoderService : IScdEncoderService
    {
        private readonly string _toolsRoot;

        public ScdEncoderService(string? toolsRoot = null)
        {
            // Default to a tools folder alongside the app binaries; fall back to the KHPCSoundTools_Source tools folder.
            var candidate = toolsRoot ?? Path.Combine(AppContext.BaseDirectory, "tools");
            if (!Directory.Exists(candidate))
            {
                candidate = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "KHPCSoundTools_Source", "tools");
            }
            _toolsRoot = candidate;
        }

        public Task<string> EncodeAsync(string inputScdPath, string wavPath, int quality = 10, bool fullLoop = false)
        {
            if (quality is < 0 or > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 0 and 10.");
            }

            return Task.Run(() => EncodeInternal(inputScdPath, wavPath, quality, fullLoop));
        }

        private string EncodeInternal(string inputScdPath, string wavPath, int quality, bool fullLoop)
        {
            if (!File.Exists(inputScdPath))
            {
                throw new FileNotFoundException("Source SCD not found", inputScdPath);
            }

            if (!File.Exists(wavPath))
            {
                throw new FileNotFoundException("Input WAV not found", wavPath);
            }

            EnsureToolsPresent();
            var outputDir = Path.GetDirectoryName(wavPath) ?? (Path.GetDirectoryName(AppContext.BaseDirectory) ?? AppContext.BaseDirectory);
            Directory.CreateDirectory(outputDir);

            var oldScd = File.ReadAllBytes(inputScdPath);
            uint tablesOffset = ScdBinaryHelpers.Read(oldScd, 16, 0x0e);
            uint headersEntries = ScdBinaryHelpers.Read(oldScd, 16, (int)tablesOffset + 0x04);
            uint headersOffset = ScdBinaryHelpers.Read(oldScd, 32, (int)tablesOffset + 0x0c);
            int fileSize = (int)ScdBinaryHelpers.Read(oldScd, 32, (int)headersOffset);

            var scdEntries = new List<byte[]>();
            int[] entryOffsets = new int[headersEntries + 1];
            entryOffsets[0] = fileSize;
            uint codec = GetCodec(headersEntries, headersOffset, oldScd);

            for (int i = 0; i < headersEntries; i++)
            {
                uint entryBegin = ScdBinaryHelpers.Read(oldScd, 32, (int)headersOffset + i * 0x04);
                uint entryEnd = i == headersEntries - 1
                    ? (uint)oldScd.Length
                    : ScdBinaryHelpers.Read(oldScd, 32, (int)headersOffset + (i + 1) * 0x04);
                uint entrySize = entryEnd - entryBegin;

                byte[] entry = new byte[entrySize];
                Array.Copy(oldScd, entryBegin, entry, 0, entrySize);

                byte[] newEntry;
                if (ScdBinaryHelpers.Read(entry, 32, 0x0c) != 0xFFFFFFFF)
                {
                    if (codec == 0x6)
                    {
                        newEntry = EncodeVorbisEntry(entry, wavPath, quality, fullLoop);
                    }
                    else
                    {
                        newEntry = EncodeMsAdpcmEntry(entry, wavPath);
                    }
                }
                else
                {
                    newEntry = entry;
                }

                scdEntries.Add(newEntry);
                fileSize += newEntry.Length;
                entryOffsets[i + 1] = fileSize;
            }

            var finalScd = new byte[fileSize];
            Array.Copy(oldScd, finalScd, entryOffsets[0]);
            for (int i = 0; i < headersEntries; i++)
            {
                ScdBinaryHelpers.Write(finalScd, entryOffsets[i], 32, (int)headersOffset + i * 0x04);
                Array.Copy(scdEntries[i], 0, finalScd, entryOffsets[i], scdEntries[i].Length);
            }

            ScdBinaryHelpers.Write(finalScd, fileSize, 32, 0x10);
            string outputPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputScdPath) + ".scd");
            File.WriteAllBytes(outputPath, finalScd);
            return outputPath;
        }

        private byte[] EncodeVorbisEntry(byte[] entry, string wavPath, int quality, bool fullLoop)
        {
            byte[] wav = File.ReadAllBytes(wavPath);

            // Prefer proper WAV loop metadata (smpl) and fall back to Audacity/other tags.
            // This matches how the loop editor writes loops and prevents losing loops on re-encode.
            var (loop, meta) = WavLoopTagReader.Read(wavPath);
            int loopStartSamples = loop?.StartSample ?? -1;
            int loopEndExclusive = loop?.EndSample ?? -1;

            // Normalize end semantics:
            // - Loop editor uses an exclusive end (can equal TotalSamples)
            // - Some sources (smpl) may store an inclusive end (often TotalSamples-1)
            if (meta is { TotalSamples: > 0 } && loopEndExclusive > 0)
            {
                if (loopEndExclusive == meta.TotalSamples - 1)
                {
                    loopEndExclusive = meta.TotalSamples;
                }

                loopEndExclusive = Math.Clamp(loopEndExclusive, 1, meta.TotalSamples);
                loopStartSamples = Math.Clamp(loopStartSamples, 0, loopEndExclusive - 1);
            }

            if (fullLoop)
            {
                // Derive full loop from WAV data chunk when tags are absent.
                byte[] fmtPattern = Encoding.ASCII.GetBytes("fmt ");
                byte[] datPattern = Encoding.ASCII.GetBytes("data");
                int typePos = ScdBinaryHelpers.SearchBytePattern(0, wav, fmtPattern);
                int dataPos = ScdBinaryHelpers.SearchBytePattern(0, wav, datPattern);
                if (typePos != -1 && dataPos != -1)
                {
                    short type = BitConverter.ToInt16(wav, typePos + 20);
                    int dataSize = BitConverter.ToInt32(wav, dataPos + 4);
                    if (dataSize < wav.Length)
                    {
                        loopStartSamples = 0;
                        loopEndExclusive = dataSize / type;
                    }
                }
            }

            string oggPath = WavToOgg(wavPath, loopStartSamples, loopEndExclusive, quality);
            try
            {
                return OggToScd(wav, entry, oggPath, loopStartSamples, loopEndExclusive);
            }
            finally
            {
                SafeDelete(oggPath);
            }
        }

        private byte[] EncodeMsAdpcmEntry(byte[] entry, string wavPath)
        {
            string msAdpcmPath = WavToMsAdpcm(wavPath);
            try
            {
                byte[] wav = File.ReadAllBytes(wavPath);
                return MsAdpcmToScd(wav, entry, msAdpcmPath);
            }
            finally
            {
                SafeDelete(msAdpcmPath);
            }
        }

        private string WavToOgg(string inputWav, int loopStartSamples, int totalSamples, int quality)
        {
            var oggOut = Path.Combine(Path.GetDirectoryName(inputWav) ?? (Path.GetDirectoryName(AppContext.BaseDirectory) ?? AppContext.BaseDirectory), Path.GetFileNameWithoutExtension(inputWav) + ".ogg");
            string oggenc = Path.Combine(_toolsRoot, "oggenc", "oggenc.exe");

            var args = loopStartSamples == -1 && totalSamples == -1
                ? $"\"{inputWav}\" -s 0 -q \"{quality}\""
                : $"\"{inputWav}\" -s 0 -q \"{quality}\" -c LoopStart=\"{loopStartSamples}\" -c LoopEnd=\"{totalSamples - 1}\"";

            RunProcess(oggenc, args);
            return oggOut;
        }

        private string WavToMsAdpcm(string inputWav)
        {
            var outputWav = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? AppContext.BaseDirectory, "adpcm" + Path.GetFileNameWithoutExtension(inputWav) + ".wav");
            string encoder = Path.Combine(_toolsRoot, "adpcmencode3", "adpcmencode3.exe");
            RunProcess(encoder, $"\"{inputWav}\" \"{outputWav}\"");
            return outputWav;
        }

        private byte[] OggToScd(byte[] wav, byte[] entry, string oggPath, int loopStartSamples, int totalSamples)
        {
            byte[] ogg = File.ReadAllBytes(oggPath);
            uint metaOffset = 0;
            uint extraDataOffset = metaOffset + 0x20;

            static ulong ReadOggGranulePosition(byte[] oggBytes, int pageOffset)
            {
                // Ogg page header layout:
                // 0x00  "OggS"
                // 0x04  version
                // 0x05  header type
                // 0x06  granule position (int64 LE)
                if (pageOffset < 0 || pageOffset + 0x0E > oggBytes.Length)
                {
                    return 0;
                }

                return BitConverter.ToUInt64(oggBytes, pageOffset + 0x06);
            }

            // Find Vorbis header size
            int vorbisHeaderSize = 0;
            byte[] pattern = { 0x05, 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 };
            vorbisHeaderSize = ScdBinaryHelpers.SearchBytePattern(vorbisHeaderSize, ogg, pattern);
            pattern = new byte[] { 0x4F, 0x67, 0x67, 0x53 };
            while (true)
            {
                vorbisHeaderSize = ScdBinaryHelpers.SearchBytePattern(vorbisHeaderSize, ogg, pattern);
                if (ScdBinaryHelpers.Read(ogg, 8, vorbisHeaderSize + 0x05) != 1)
                {
                    break;
                }
                vorbisHeaderSize = vorbisHeaderSize + 4;
            }

            // Find OGG page offsets
            List<int> pageOffsets = new();
            int offset = vorbisHeaderSize;
            pattern = new byte[] { 0x4F, 0x67, 0x67, 0x53 };
            while (true)
            {
                offset = ScdBinaryHelpers.SearchBytePattern(offset, ogg, pattern);
                if (offset == -1)
                {
                    break;
                }
                pageOffsets.Add(offset);
                offset = offset + 4;
            }

            // Stream size
            int streamSize = ogg.Length - vorbisHeaderSize;
            ScdBinaryHelpers.Write(entry, streamSize, 32, (int)metaOffset);

            // Loop offsets
            int loopStart = 0;
            int loopEnd = 0;
            if (loopStartSamples != -1 && totalSamples != -1)
            {
                // Map loopStart/loopEnd from PCM samples to byte offsets into the OGG *audio* stream.
                // These offsets are relative to the start of the audio data (after the Vorbis header pages).
                // KH2 appears to rely on these byte offsets for looping.
                for (int i = 0; i < pageOffsets.Count; i++)
                {
                    offset = pageOffsets[i];
                    if (ReadOggGranulePosition(ogg, offset) >= (ulong)loopStartSamples)
                    {
                        loopStart = pageOffsets[i] - vorbisHeaderSize;
                        break;
                    }
                }

                // Determine if the requested end sample is essentially the end of the stream.
                // If so, keep the historical behavior of using streamSize.
                var lastGranule = pageOffsets.Count > 0
                    ? ReadOggGranulePosition(ogg, pageOffsets[^1])
                    : 0UL;

                if ((ulong)totalSamples >= lastGranule && lastGranule > 0)
                {
                    loopEnd = streamSize;
                }
                else
                {
                    loopEnd = streamSize;
                    for (int i = 0; i < pageOffsets.Count; i++)
                    {
                        offset = pageOffsets[i];
                        if (ReadOggGranulePosition(ogg, offset) >= (ulong)totalSamples)
                        {
                            loopEnd = pageOffsets[i] - vorbisHeaderSize;
                            break;
                        }
                    }
                }

                loopStart = Math.Clamp(loopStart, 0, streamSize);
                loopEnd = Math.Clamp(loopEnd, 0, streamSize);
                if (loopEnd <= loopStart)
                {
                    // If we can't derive a sane loop end, fall back to full-stream.
                    loopEnd = streamSize;
                }
            }

            ScdBinaryHelpers.Write(entry, loopStart, 32, (int)metaOffset + 0x10);
            ScdBinaryHelpers.Write(entry, loopEnd, 32, (int)metaOffset + 0x14);

            uint channels = ScdBinaryHelpers.Read(ogg, 8, 0x27);
            ScdBinaryHelpers.Write(entry, (int)channels, 8, (int)metaOffset + 0x04);
            uint sampleRate = ScdBinaryHelpers.Read(ogg, 32, 0x28);
            ScdBinaryHelpers.Write(entry, (int)sampleRate, 32, (int)metaOffset + 0x08);

            uint auxChunkCount = ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x1c);
            uint auxChunkSize = 0;
            if (auxChunkCount > 0)
            {
                auxChunkSize = ScdBinaryHelpers.Read(entry, 32, (int)extraDataOffset + 0x04);
                extraDataOffset += auxChunkSize;
                uint markEntries = ScdBinaryHelpers.Read(entry, 32, (int)metaOffset + 0x30);
                ScdBinaryHelpers.Write(entry, loopStartSamples, 32, (int)metaOffset + 0x28);
                ScdBinaryHelpers.Write(entry, totalSamples, 32, (int)metaOffset + 0x2C);
                if (markEntries == 1)
                {
                    int mark = ScdBinaryHelpers.SearchTag("MARK1", wav);
                    ScdBinaryHelpers.Write(entry, mark != -1 ? mark : loopStartSamples, 32, (int)metaOffset + 0x34);
                }
                else
                {
                    for (int i = 0; i < markEntries; i++)
                    {
                        int mark = ScdBinaryHelpers.SearchTag("MARK" + (i + 1), wav);
                        ScdBinaryHelpers.Write(entry, mark != -1 ? mark : 0, 32, (int)metaOffset + 0x34 + i * 0x04);
                    }
                }
            }

            ScdBinaryHelpers.Write(entry, vorbisHeaderSize, 32, (int)extraDataOffset + 0x14);
            // Preserve the existing encode byte from the template SCD.
            // KH2 game files commonly use encodeType=0x2002 and encodeByte=0x7F.

            offset = vorbisHeaderSize;
            List<int> seekOffsets = new();
            ulong previousGranule = ReadOggGranulePosition(ogg, offset);
            seekOffsets.Add(0);
            if (offset != pageOffsets[^1])
            {
                for (int i = 1; i < pageOffsets.Count; i++)
                {
                    offset = pageOffsets[i];
                    if (i == pageOffsets.Count - 1)
                    {
                        break;
                    }
                    ulong currentGranule = ReadOggGranulePosition(ogg, offset);
                    if (currentGranule - previousGranule >= 2048)
                    {
                        seekOffsets.Add(offset - vorbisHeaderSize);
                        previousGranule = currentGranule;
                    }
                }
                if (seekOffsets.Count == pageOffsets.Count - 1)
                {
                    seekOffsets.Add(offset - vorbisHeaderSize);
                }
            }

            byte[] seekTable = new byte[seekOffsets.Count * 4];
            for (int i = 0; i < seekOffsets.Count; i++)
            {
                ScdBinaryHelpers.Write(seekTable, seekOffsets[i], 32, i * 4);
            }

            ScdBinaryHelpers.Write(entry, seekTable.Length, 32, (int)extraDataOffset + 0x10);
            ScdBinaryHelpers.Write(entry, 0x20 + vorbisHeaderSize + (int)auxChunkSize + seekTable.Length, 32, (int)metaOffset + 0x18);

            // SCD layout expects:
            // - Seek table header (0x20 bytes)
            // - Seek table
            // - XOR-encoded Vorbis header pages (vorbisHeaderSize bytes)
            // - OGG audio pages (ogg.Length - vorbisHeaderSize bytes)
            int newSize = (int)(extraDataOffset + 0x20 + seekTable.Length + ogg.Length);
            while (newSize % 16 != 0)
            {
                newSize++;
            }

            byte[] newEntry = new byte[newSize];
            Array.Copy(entry, newEntry, extraDataOffset + 0x20);
            Array.Copy(seekTable, 0, newEntry, extraDataOffset + 0x20, seekTable.Length);

            // Copy the Vorbis header pages (optionally XOR-obfuscated) and then the audio pages.
            int oggWriteOffset = (int)extraDataOffset + 0x20 + seekTable.Length;
            int headerSize = Math.Clamp(vorbisHeaderSize, 0, ogg.Length);
            int audioSize = ogg.Length - headerSize;

            // Read the template encode byte (seek table header byte at +0x02).
            byte encodeByte = entry[(int)extraDataOffset + 0x02];

            // Header portion: copy, applying XOR if encodeByte is non-zero.
            for (int i = 0; i < headerSize; i++)
            {
                byte v = ogg[i];
                if (encodeByte != 0)
                {
                    v ^= encodeByte;
                }
                newEntry[oggWriteOffset + i] = v;
            }

            // Audio portion: copy as-is.
            Array.Copy(ogg, headerSize, newEntry, oggWriteOffset + headerSize, audioSize);
            return newEntry;
        }

        private byte[] MsAdpcmToScd(byte[] wav, byte[] entry, string msAdpcmPath)
        {
            byte[] msadpcm = File.ReadAllBytes(msAdpcmPath);
            uint metaOffset = 0;
            uint extraDataOffset = metaOffset + 0x20;
            uint channels = ScdBinaryHelpers.Read(msadpcm, 8, 0x16);
            uint sampleRate = ScdBinaryHelpers.Read(msadpcm, 32, 0x18);
            ScdBinaryHelpers.Write(entry, (int)channels, 8, (int)metaOffset + 0x04);
            ScdBinaryHelpers.Write(entry, (int)sampleRate, 32, (int)metaOffset + 0x08);

            byte[] pattern = { 0x64, 0x61, 0x74, 0x61 };
            int dataOffset = ScdBinaryHelpers.SearchBytePattern(0, msadpcm, pattern) + 8;
            ScdBinaryHelpers.Write(entry, msadpcm.Length - dataOffset, 32, (int)metaOffset);

            int fileSize = (int)(extraDataOffset + 0x32 + msadpcm.Length - dataOffset);
            while (fileSize % 16 != 0)
            {
                fileSize++;
            }

            byte[] newEntry = new byte[fileSize];
            Array.Copy(entry, newEntry, extraDataOffset);
            Array.Copy(msadpcm, 0x14, newEntry, extraDataOffset, 0x32);
            Array.Copy(msadpcm, dataOffset, newEntry, extraDataOffset + 0x32, msadpcm.Length - dataOffset);
            return newEntry;
        }

        private void RunProcess(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Process {Path.GetFileName(fileName)} failed with exit code {process.ExitCode}.");
            }
        }

        private static uint GetCodec(uint headersEntries, uint headersOffset, byte[] data)
        {
            uint entryCodec = 0xFFFFFFFF;
            for (int i = 0; i < headersEntries; i++)
            {
                uint entryBegin = ScdBinaryHelpers.Read(data, 32, (int)headersOffset + i * 0x04);
                entryCodec = ScdBinaryHelpers.Read(data, 32, (int)entryBegin + 0x0c);
                if (entryCodec != 0xFFFFFFFF)
                {
                    break;
                }
            }
            return entryCodec;
        }

        private void EnsureToolsPresent()
        {
            string oggenc = Path.Combine(_toolsRoot, "oggenc", "oggenc.exe");
            string adpcm = Path.Combine(_toolsRoot, "adpcmencode3", "adpcmencode3.exe");
            if (!File.Exists(oggenc))
            {
                throw new FileNotFoundException("Please place oggenc.exe in tools/oggenc", oggenc);
            }
            if (!File.Exists(adpcm))
            {
                throw new FileNotFoundException("Please place adpcmencode3.exe in tools/adpcmencode3", adpcm);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
