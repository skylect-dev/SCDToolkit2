using System.Threading.Tasks;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Abstractions
{
    public interface IPlaybackService
    {
        Task PlayAsync(LibraryItem item, LoopPoints loopPoints);
        Task PauseAsync();
        Task StopAsync();
        Task SeekAsync(System.TimeSpan position);
        double Volume { get; set; }
        System.TimeSpan Position { get; }
        System.TimeSpan Duration { get; }
        bool IsPlaying { get; }
        bool LoopEnabled { get; set; }
    }
}
