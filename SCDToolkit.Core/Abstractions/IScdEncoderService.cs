using System.Threading.Tasks;

namespace SCDToolkit.Core.Abstractions
{
    public interface IScdEncoderService
    {
        /// <summary>
        /// Encode/replace entries in an SCD using a WAV with loop tags. Returns path of written SCD.
        /// </summary>
        Task<string> EncodeAsync(string inputScdPath, string wavPath, int quality = 10, bool fullLoop = false);
    }
}
