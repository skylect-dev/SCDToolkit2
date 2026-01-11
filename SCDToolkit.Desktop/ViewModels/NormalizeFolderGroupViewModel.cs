using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class NormalizeFolderGroupViewModel : ObservableObject
    {
        [ObservableProperty]
        private string folderPath = string.Empty;

        [ObservableProperty]
        private string displayName = string.Empty;

        [ObservableProperty]
        private bool isExpanded = true;

        public ObservableCollection<SelectableScdFile> Items { get; } = new();

        public int ItemCount => Items.Count;

        // Checked when all items are selected; setting selects/deselects all.
        public bool IsAllSelected
        {
            get => Items.Count > 0 && Items.All(i => i.IsSelected);
            set
            {
                foreach (var item in Items)
                {
                    item.IsSelected = value;
                }
                OnPropertyChanged();
            }
        }

        public NormalizeFolderGroupViewModel()
        {
            Items.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(ItemCount));
                OnPropertyChanged(nameof(IsAllSelected));
            };
        }

        public void AttachSelectionTracking()
        {
            foreach (var item in Items)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
                item.PropertyChanged += OnItemPropertyChanged;
            }

            OnPropertyChanged(nameof(IsAllSelected));
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableScdFile.IsSelected))
            {
                OnPropertyChanged(nameof(IsAllSelected));
            }
        }

        public override string ToString() => DisplayName;
    }
}
