using System.Threading.Tasks;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Abstractions
{
    public interface IAudioDecodeService
    {
        Task<AudioPreview> DecodePreviewAsync(string path);
    }
}
