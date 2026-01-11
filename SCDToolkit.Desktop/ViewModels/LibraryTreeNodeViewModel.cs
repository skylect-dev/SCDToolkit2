using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class LibraryTreeNodeViewModel : ObservableObject
    {
        [ObservableProperty]
        private string folderPath = string.Empty;

        [ObservableProperty]
        private string displayName = string.Empty;

        [ObservableProperty]
        private bool isExpanded = true;

        [ObservableProperty]
        private ObservableCollection<object> children = new();

        [ObservableProperty]
        private int itemCount;
    }
}
