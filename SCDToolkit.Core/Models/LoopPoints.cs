namespace SCDToolkit.Core.Models
{
    public class LoopPoints
    {
        public LoopPoints(int startSample, int endSample)
        {
            StartSample = startSample;
            EndSample = endSample;
        }

        public int StartSample { get; }
        public int EndSample { get; }
    }
}
