using CommunityToolkit.Mvvm.ComponentModel;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class FileTypeFilterOptionViewModel : ObservableObject
    {
        [ObservableProperty]
        private string extension = string.Empty; // e.g. ".scd"

        [ObservableProperty]
        private string displayName = string.Empty; // e.g. "SCD"

        [ObservableProperty]
        private int count;

        [ObservableProperty]
        private bool isChecked = true;

        public override string ToString() => DisplayName;
    }
}
