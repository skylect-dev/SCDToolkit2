using System.Collections.Generic;

namespace SCDToolkit.Desktop.Models
{
    public sealed class WaveformData
    {
        public WaveformData(int totalSamples, int channels, float[][] samples, List<PeakLevel> peakLevels)
        {
            TotalSamples = totalSamples;
            Channels = channels;
            Samples = samples;
            PeakLevels = peakLevels;
        }

        public int TotalSamples { get; }
        public int Channels { get; }
        public float[][] Samples { get; }
        public List<PeakLevel> PeakLevels { get; }
    }

    public sealed class PeakLevel
    {
        public PeakLevel(int blockSize, float[][] min, float[][] max)
        {
            BlockSize = blockSize;
            Min = min;
            Max = max;
        }

        public int BlockSize { get; }
        public float[][] Min { get; }
        public float[][] Max { get; }
    }
}
