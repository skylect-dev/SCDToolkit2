using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class FolderGroupViewModel : ObservableObject
    {
        [ObservableProperty]
        private string folderPath = string.Empty;

        [ObservableProperty]
        private string displayName = string.Empty;

        [ObservableProperty]
        private bool isExpanded = true;

        [ObservableProperty]
        private ObservableCollection<LibraryItemViewModel> items = new();

        public int ItemCount => Items.Count;
    }
}
