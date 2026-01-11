using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SCDToolkit.Core.Abstractions;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Core.Services
{
    /// <summary>
    /// Basic filesystem scanner: finds audio/SCD files under provided folders.
    /// </summary>
    public class FileSystemLibraryService : ILibraryService
    {
        private static readonly string[] Extensions = { ".wav", ".ogg", ".scd", ".mp3", ".flac" };

        public Task<IReadOnlyList<LibraryItem>> ScanAsync(IEnumerable<string> folderPaths, bool includeSubdirectories = true)
        {
            var items = new List<LibraryItem>();
            var option = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var scdParser = new ScdParser();

            foreach (var folder in folderPaths.Distinct())
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                var files = Directory.EnumerateFiles(folder, "*", option)
                    .Where(f => Extensions.Any(ext => f.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase)));

                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    var display = Path.GetFileName(file);
                    var details = $"{info.Extension.ToUpperInvariant().Trim('.')} • {(info.Length / (1024 * 1024.0)):0.0} MB";
                    var item = new LibraryItem(file, display, details);

                    if (info.Extension.Equals(".wav", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var (loop, meta) = WavLoopTagReader.Read(file);
                        item.LoopPoints = loop;
                        item.Metadata = meta;
                    }
                    else if (info.Extension.Equals(".scd", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var (loop, meta) = scdParser.ReadScdInfo(file);
                        item.LoopPoints = loop;
                        item.Metadata = meta;
                    }

                    items.Add(item);
                }
            }

            return Task.FromResult<IReadOnlyList<LibraryItem>>(items.OrderBy(i => i.DisplayName).ToList());
        }
    }
}
