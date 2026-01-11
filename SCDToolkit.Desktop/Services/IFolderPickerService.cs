using System.Threading.Tasks;

namespace SCDToolkit.Desktop.Services
{
    public interface IFolderPickerService
    {
        Task<string?> PickFolderAsync();
    }
}
