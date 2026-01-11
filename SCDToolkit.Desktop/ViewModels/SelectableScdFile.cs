using CommunityToolkit.Mvvm.ComponentModel;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class SelectableScdFile : ObservableObject
    {
        [ObservableProperty]
        private bool isSelected;

        [ObservableProperty]
        private string displayName = string.Empty;

        [ObservableProperty]
        private string filePath = string.Empty;
    }
}
