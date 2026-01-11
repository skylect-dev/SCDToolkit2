namespace SCDToolkit.Core.Models
{
    public class AudioMetadata
    {
        public AudioMetadata(int sampleRate, int channels, int totalSamples)
        {
            SampleRate = sampleRate;
            Channels = channels;
            TotalSamples = totalSamples;
        }

        public int SampleRate { get; }
        public int Channels { get; }
        public int TotalSamples { get; }
    }
}
