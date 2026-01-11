using System.Threading.Tasks;
using SCDToolkit.Core.Abstractions;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Services
{
    // Placeholder decode service; replace with ffmpeg/vgmstream-backed implementation.
    public class StubAudioDecodeService : IAudioDecodeService
    {
        public Task<AudioPreview> DecodePreviewAsync(string path)
        {
            // Flat line preview placeholder
            var peaks = new float[256];
            var metadata = new AudioMetadata(48000, 2, 712126);
            return Task.FromResult(new AudioPreview(peaks, metadata));
        }
    }
}
