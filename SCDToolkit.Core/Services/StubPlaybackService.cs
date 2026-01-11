using System.Threading.Tasks;
using SCDToolkit.Core.Abstractions;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Services
{
    // Placeholder playback; replace with vgmstream/NAudio integration.
    public class StubPlaybackService : IPlaybackService
    {
        public double Volume { get; set; } = 0.7;
        public System.TimeSpan Position => System.TimeSpan.Zero;
        public System.TimeSpan Duration => System.TimeSpan.Zero;
        public bool IsPlaying => false;
        public bool LoopEnabled { get; set; } = true;

        public Task PlayAsync(LibraryItem item, LoopPoints loopPoints)
        {
            return Task.CompletedTask;
        }

        public Task PauseAsync()
        {
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public Task SeekAsync(System.TimeSpan position)
        {
            return Task.CompletedTask;
        }
    }
}
