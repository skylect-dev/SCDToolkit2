using System.Threading.Tasks;

namespace SCDToolkit.Desktop.Services
{
    internal class NoopFolderPickerService : IFolderPickerService
    {
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
