namespace SCDToolkit.Core.Models
{
    public class AudioPreview
    {
        public AudioPreview(float[] peaks, AudioMetadata metadata)
        {
            Peaks = peaks;
            Metadata = metadata;
        }

        public float[] Peaks { get; }
        public AudioMetadata Metadata { get; }
    }
}
