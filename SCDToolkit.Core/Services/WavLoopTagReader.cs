using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SCDToolkit.Core.Models;
using SCDToolkit.Core.Utils;

namespace SCDToolkit.Core.Services
{
    public static class WavLoopTagReader
    {
        public static (LoopPoints? loop, AudioMetadata? meta) Read(string path)
        {
            var data = File.ReadAllBytes(path);

            // Basic WAV header fields
            if (data.Length < 44)
            {
                return (null, null);
            }

            var sampleRate = BitConverter.ToInt32(data, 0x18);
            var channels = BitConverter.ToInt16(data, 0x16);
            int totalSamples = -1;

            // Derive total samples from data chunk
            var dataOffset = FindChunk(data, "data");
            var fmtOffset = FindChunk(data, "fmt ");
            if (dataOffset >= 0 && fmtOffset >= 0)
            {
                var bitsPerSample = BitConverter.ToInt16(data, fmtOffset + 14);
                var byteRate = BitConverter.ToInt32(data, fmtOffset + 8);
                var bytes = BitConverter.ToInt32(data, dataOffset + 4);
                if (bitsPerSample > 0 && channels > 0)
                {
                    totalSamples = (int)((bytes * 8) / (bitsPerSample * channels));
                }
                else if (byteRate > 0 && sampleRate > 0)
                {
                    var durationSec = bytes / (double)byteRate;
                    totalSamples = (int)(durationSec * sampleRate);
                }
            }

            // Try standard smpl chunk first (industry standard for WAV loops)
            LoopPoints? loop = ReadSmplChunk(data);

            // Fall back to Audacity metadata tags (LoopStart/LoopEnd in INFO or custom chunks)
            if (loop == null)
            {
                loop = ReadAudacityLoopTags(data);
            }

            // Fall back to custom LoopStart/LoopEnd tags (other editors)
            if (loop == null)
            {
                int loopStart = ScdBinaryHelpers.SearchTag("LoopStart", data);
                int loopEnd = ScdBinaryHelpers.SearchTag("LoopEnd", data);
                if (loopStart >= 0 && loopEnd >= 0)
                {
                    loop = new LoopPoints(loopStart, loopEnd);
                }
            }

            AudioMetadata? meta = null;
            if (sampleRate > 0 && channels > 0 && totalSamples > 0)
            {
                meta = new AudioMetadata(sampleRate, channels, totalSamples);
            }

            return (loop, meta);
        }

        private static int FindChunk(byte[] data, string fourCc)
        {
            var pattern = System.Text.Encoding.ASCII.GetBytes(fourCc);
            return ScdBinaryHelpers.SearchBytePattern(0, data, pattern);
        }

        private static LoopPoints? ReadSmplChunk(byte[] data)
        {
            var smplOffset = FindChunk(data, "smpl");
            if (smplOffset < 0 || smplOffset + 36 > data.Length)
            {
                return null;
            }

            // smpl chunk: 4 bytes 'smpl', 4 bytes size, 7*4 bytes header, then loop data
            const int smplHeaderSize = 36; // bytes after the chunk id/size
            var numLoops = BitConverter.ToUInt32(data, smplOffset + 28);
            if (numLoops == 0 || smplOffset + 8 + smplHeaderSize > data.Length)
            {
                return null;
            }

            // First loop: 6*4 bytes, we want dwStart (offset +8) and dwEnd (offset +12)
            var firstLoopOffset = smplOffset + 8 + smplHeaderSize; // skip id + size + header
            if (firstLoopOffset + 16 > data.Length)
            {
                return null;
            }

            var loopStart = (int)BitConverter.ToUInt32(data, firstLoopOffset + 8);
            var loopEnd = (int)BitConverter.ToUInt32(data, firstLoopOffset + 12);

            if (loopStart >= 0 && loopEnd > loopStart)
            {
                return new LoopPoints(loopStart, loopEnd);
            }

            return null;
        }

        private static LoopPoints? ReadAudacityLoopTags(byte[] data)
        {
            try
            {
                // Search for LIST-INFO chunk and extract LoopStart/LoopEnd metadata
                var listOffset = FindChunk(data, "LIST");
                if (listOffset < 0 || listOffset + 8 > data.Length)
                {
                    return null;
                }

                var chunkSize = BitConverter.ToInt32(data, listOffset + 4);
                var chunkEnd = listOffset + 8 + chunkSize;
                if (chunkEnd > data.Length)
                {
                    chunkEnd = data.Length;
                }

                // Extract text from INFO chunk (look for "LoopStart" and "LoopEnd" as key=value pairs)
                var infoBytes = new byte[chunkEnd - listOffset - 8];
                Array.Copy(data, listOffset + 8, infoBytes, 0, infoBytes.Length);

                // Try multiple encodings since metadata might be stored differently
                string? infoText = null;
                foreach (var enc in new[] { Encoding.ASCII, Encoding.UTF8, Encoding.GetEncoding("ISO-8859-1") })
                {
                    try
                    {
                        var text = enc.GetString(infoBytes);
                        if (text.Contains("LoopStart") || text.Contains("loopStart"))
                        {
                            infoText = text;
                            break;
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(infoText))
                {
                    return null;
                }

                // Extract numeric values from tags: LoopStart=387971 and LoopEnd=712126
                var loopStartMatch = Regex.Match(infoText, @"LoopStart[=:\s]+(\d+)", RegexOptions.IgnoreCase);
                var loopEndMatch = Regex.Match(infoText, @"LoopEnd[=:\s]+(\d+)", RegexOptions.IgnoreCase);

                if (loopStartMatch.Success && loopEndMatch.Success)
                {
                    int loopStart = int.Parse(loopStartMatch.Groups[1].Value);
                    int loopEnd = int.Parse(loopEndMatch.Groups[1].Value);

                    if (loopStart >= 0 && loopEnd > loopStart)
                    {
                        return new LoopPoints(loopStart, loopEnd);
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
