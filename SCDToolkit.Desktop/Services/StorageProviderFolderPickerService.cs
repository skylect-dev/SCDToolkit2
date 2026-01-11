using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SCDToolkit.Desktop.Services
{
    public class StorageProviderFolderPickerService : IFolderPickerService
    {
        private readonly TopLevel _topLevel;

        public StorageProviderFolderPickerService(TopLevel topLevel)
        {
            _topLevel = topLevel;
        }

        public async Task<string?> PickFolderAsync()
        {
            var provider = _topLevel.StorageProvider;
            if (provider == null)
            {
                return null;
            }

            var results = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = null,
                Title = "Select folder to scan"
            });

            return results is { Count: > 0 } ? results[0].Path?.LocalPath : null;
        }
    }
}
