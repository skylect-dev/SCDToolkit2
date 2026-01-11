using CommunityToolkit.Mvvm.ComponentModel;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class RandoCategoryViewModel : ObservableObject
    {
        [ObservableProperty]
        private string folderPath = string.Empty;

        [ObservableProperty]
        private string displayName = string.Empty;

        public override string ToString() => DisplayName;
    }
}
