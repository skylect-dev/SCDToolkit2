using System.Collections.Generic;
using System.Threading.Tasks;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Abstractions
{
    public interface ILibraryService
    {
        Task<IReadOnlyList<LibraryItem>> ScanAsync(IEnumerable<string> folderPaths, bool includeSubdirectories = true);
    }
}
