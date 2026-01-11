using System;
using System.IO;
using SCDToolkit.Core.Utils;

namespace SCDToolkit.Core.Services
{
    /// <summary>
    /// Writes loop metadata to WAV files using a standard SMPL chunk.
    /// </summary>
    public static class WavLoopTagWriter
    {
        public static void Write(string path, int loopStartSample, int loopEndSample)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("WAV file not found", path);
            }

            if (loopStartSample < 0 || loopEndSample <= loopStartSample)
            {
                throw new ArgumentException("Loop points must be non-negative and end must be after start.");
            }

            var data = File.ReadAllBytes(path);
            if (data.Length < 44)
            {
                throw new InvalidOperationException("Invalid WAV file.");
            }

            var sampleRate = BitConverter.ToInt32(data, 0x18);
            if (sampleRate <= 0)
            {
                throw new InvalidOperationException("Sample rate not found in WAV header.");
            }

            var smplOffset = FindChunk(data, "smpl");
            byte[] updated = smplOffset >= 0
                ? UpdateExistingSmpl(data, smplOffset, sampleRate, loopStartSample, loopEndSample)
                : AppendSmpl(data, sampleRate, loopStartSample, loopEndSample);

            File.WriteAllBytes(path, updated);
        }

        private static byte[] UpdateExistingSmpl(byte[] data, int smplOffset, int sampleRate, int loopStartSample, int loopEndSample)
        {
            var buffer = (byte[])data.Clone();

            const int smplHeaderSize = 36;
            const int loopStructSize = 24;

            var existingChunkSize = BitConverter.ToInt32(buffer, smplOffset + 4);
            var existingChunkEnd = smplOffset + 8 + existingChunkSize;
            var requiredSize = smplHeaderSize + loopStructSize;

            // If the existing chunk is too small, append a fresh one instead of risking corruption.
            if (existingChunkEnd < smplOffset + 8 + requiredSize)
            {
                return AppendSmpl(data, sampleRate, loopStartSample, loopEndSample);
            }

            // Standard SMPL header fields
            var samplePeriod = (uint)Math.Max(1, 1_000_000_000.0 / sampleRate);
            ScdBinaryHelpers.Write(buffer, (int)samplePeriod, 32, smplOffset + 8);
            ScdBinaryHelpers.Write(buffer, 1, 32, smplOffset + 28); // numSampleLoops
            ScdBinaryHelpers.Write(buffer, 0, 32, smplOffset + 32); // samplerData

            var firstLoopOffset = smplOffset + 44; // cuePointId (0), type (0), start/end follow
            ScdBinaryHelpers.Write(buffer, 0, 32, firstLoopOffset + 0);   // cuePointID
            ScdBinaryHelpers.Write(buffer, 0, 32, firstLoopOffset + 4);   // loop type: forward
            ScdBinaryHelpers.Write(buffer, loopStartSample, 32, firstLoopOffset + 8);
            ScdBinaryHelpers.Write(buffer, loopEndSample, 32, firstLoopOffset + 12);
            ScdBinaryHelpers.Write(buffer, 0, 32, firstLoopOffset + 16);  // fraction
            ScdBinaryHelpers.Write(buffer, 0, 32, firstLoopOffset + 20);  // play count (0 = infinite)

            // Update chunk size (header without the 8-byte id+size)
            var chunkSize = Math.Max(existingChunkSize, requiredSize);
            ScdBinaryHelpers.Write(buffer, chunkSize, 32, smplOffset + 4);

            return buffer;
        }

        private static byte[] AppendSmpl(byte[] data, int sampleRate, int loopStartSample, int loopEndSample)
        {
            const int smplHeaderSize = 36;
            const int loopStructSize = 24;
            var samplePeriod = (uint)Math.Max(1, 1_000_000_000.0 / sampleRate);

            var chunkSize = smplHeaderSize + loopStructSize; // does not include id+size fields
            var smplChunk = new byte[chunkSize + 8];

            // "smpl" + chunk size
            smplChunk[0] = (byte)'s';
            smplChunk[1] = (byte)'m';
            smplChunk[2] = (byte)'p';
            smplChunk[3] = (byte)'l';
            var chunkSizeBytes = BitConverter.GetBytes(chunkSize);
            Array.Copy(chunkSizeBytes, 0, smplChunk, 4, 4);

            // Header
            ScdBinaryHelpers.Write(smplChunk, (int)samplePeriod, 32, 8);   // samplePeriod (nanoseconds per sample)
            ScdBinaryHelpers.Write(smplChunk, 60, 32, 12);                 // midi unity note (middle C)
            ScdBinaryHelpers.Write(smplChunk, 1, 32, 28);                  // numSampleLoops
            ScdBinaryHelpers.Write(smplChunk, 0, 32, 32);                  // samplerData

            // First loop
            var firstLoopOffset = 44; // relative to start of chunk
            ScdBinaryHelpers.Write(smplChunk, 0, 32, firstLoopOffset + 0);   // cuePointID
            ScdBinaryHelpers.Write(smplChunk, 0, 32, firstLoopOffset + 4);   // loop type forward
            ScdBinaryHelpers.Write(smplChunk, loopStartSample, 32, firstLoopOffset + 8);
            ScdBinaryHelpers.Write(smplChunk, loopEndSample, 32, firstLoopOffset + 12);
            ScdBinaryHelpers.Write(smplChunk, 0, 32, firstLoopOffset + 16);  // fraction
            ScdBinaryHelpers.Write(smplChunk, 0, 32, firstLoopOffset + 20);  // play count

            // Combine original data + new chunk and fix RIFF size
            var combined = new byte[data.Length + smplChunk.Length];
            Array.Copy(data, combined, data.Length);
            Array.Copy(smplChunk, 0, combined, data.Length, smplChunk.Length);

            var riffSize = combined.Length - 8;
            var riffSizeBytes = BitConverter.GetBytes(riffSize);
            Array.Copy(riffSizeBytes, 0, combined, 4, 4);

            return combined;
        }

        private static int FindChunk(byte[] data, string fourCc)
        {
            var pattern = System.Text.Encoding.ASCII.GetBytes(fourCc);
            return ScdBinaryHelpers.SearchBytePattern(0, data, pattern);
        }
    }
}
