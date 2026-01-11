using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SCDToolkit.Core.Abstractions;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Services
{
    // Placeholder library scanner; replace with real filesystem scan and SCD parsing.
    public class StubLibraryService : ILibraryService
    {
        public Task<IReadOnlyList<LibraryItem>> ScanAsync(IEnumerable<string> folderPaths, bool includeSubdirectories = true)
        {
            var items = folderPaths.Select((path, i) =>
                new LibraryItem(path, $"Stub Track {i + 1}", "LoopStart=387971, LoopEnd=712126")
                {
                    LoopPoints = new LoopPoints(387971, 712126),
                    Metadata = new AudioMetadata(48000, 2, 712126)
                }).ToList();

            return Task.FromResult<IReadOnlyList<LibraryItem>>(items);
        }
    }
}
