using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SCDToolkit.Core.Models;

namespace SCDToolkit.Desktop.ViewModels
{
    public partial class LibraryItemViewModel : ObservableObject
    {
        public LibraryItemViewModel(string displayName, string details)
        {
            DisplayName = displayName;
            Details = details;
        }

        [ObservableProperty]
        private string displayName;

        [ObservableProperty]
        private string details;

        [ObservableProperty]
        private string? path;

        public bool IsWav => !string.IsNullOrWhiteSpace(Path) && Path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        public bool IsScd => !string.IsNullOrWhiteSpace(Path) && Path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase);

        // Context menu enabling rules:
        // - If already SCD, converting to SCD doesn't make sense.
        // - If already WAV, converting to WAV doesn't make sense.
        // - Other formats allow both conversions.
        public bool CanConvertToScdAny => !IsScd;
        public bool CanConvertToWavAny => !IsWav;

        [ObservableProperty]
        private int loopStart;

        [ObservableProperty]
        private int loopEnd;

        // Used by the folder TreeView style binding. Leaf nodes don't actually expand,
        // but the property must exist to avoid binding errors.
        [ObservableProperty]
        private bool isExpanded;

        public string LoopInfo
        {
            get
            {
                if (LoopStart > 0 && LoopEnd > LoopStart)
                {
                    return $"Loop: {LoopStart:N0} → {LoopEnd:N0}";
                }
                return "No loop";
            }
        }

        public LibraryItem ToModel()
        {
            var item = new LibraryItem(Path ?? string.Empty, DisplayName, Details)
            {
                LoopPoints = new LoopPoints(LoopStart, LoopEnd)
            };
            return item;
        }
    }
}
