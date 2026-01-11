using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class RandoFolderGroupViewModel : ObservableObject
    {
        [ObservableProperty]
        private string folderPath = string.Empty;

        [ObservableProperty]
        private string displayName = string.Empty;

        [ObservableProperty]
        private bool isExpanded = true;

        [ObservableProperty]
        private bool isSelected;

        public ObservableCollection<string> Files { get; } = new();

        public int ItemCount => Files.Count;

        public RandoFolderGroupViewModel()
        {
            Files.CollectionChanged += OnFilesChanged;
        }

        private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ItemCount));
        }

        public override string ToString() => DisplayName;
    }
}
