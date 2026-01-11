using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SCDToolkit.Desktop.Services
{
    public class AvaloniaFolderPickerService : IFolderPickerService
    {
        private readonly Window _window;

        public AvaloniaFolderPickerService(Window window)
        {
            _window = window;
        }

        public async Task<string?> PickFolderAsync()
        {
            if (_window?.StorageProvider == null)
            {
                return null;
            }

            var result = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false
            });

            return result?.FirstOrDefault()?.TryGetLocalPath();
        }
    }
}
