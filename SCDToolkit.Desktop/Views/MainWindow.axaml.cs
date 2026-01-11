using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SCDToolkit.Desktop.ViewModels;

namespace SCDToolkit.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private Slider? _positionSlider;
        private bool _dragging;

        private Point _libraryPressPoint;
        private bool _libraryPointerDown;
        private LibraryItemViewModel? _libraryPressedItem;

        public MainWindow()
        {
            InitializeComponent();
            AttachSliderEvents();
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

            Opened += (_, _) => (DataContext as MainViewModel)?.StartKh2HookWarmup();
            Closed += (_, _) => (DataContext as MainViewModel)?.StopKh2HookWarmup();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void AttachSliderEvents()
        {
            _positionSlider = this.FindControl<Slider>("PositionSlider");
            if (_positionSlider == null) return;

            _positionSlider.AddHandler(Thumb.DragStartedEvent, OnSliderDragStarted, RoutingStrategies.Tunnel);
            _positionSlider.AddHandler(Thumb.DragCompletedEvent, OnSliderDragCompleted, RoutingStrategies.Tunnel);
            _positionSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            _positionSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            _positionSlider.AddHandler(PointerCaptureLostEvent, OnSliderPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_dragging) return;

            _dragging = true;
            (DataContext as MainViewModel)?.BeginSeek();
        }

        private async void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_dragging) return;

            _dragging = false;
            await FinishSeekAsync(sender as Slider);
        }

        private async void OnSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!_dragging) return;

            _dragging = false;
            await FinishSeekAsync(sender as Slider);
        }

        private void OnSliderDragStarted(object? sender, VectorEventArgs e)
        {
            if (_dragging) return;

            _dragging = true;
            (DataContext as MainViewModel)?.BeginSeek();
        }

        private async void OnSliderDragCompleted(object? sender, VectorEventArgs e)
        {
            if (!_dragging) return;

            _dragging = false;
            await FinishSeekAsync(sender as Slider);
        }

        private async Task FinishSeekAsync(Slider? slider)
        {
            if (DataContext is not MainViewModel vm) return;

            var target = slider?.Value ?? _positionSlider?.Value ?? 0;
            await vm.EndSeekAsync(target);
        }

        private async void OnLibraryDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            // Get the LibraryItemViewModel from the tapped element
            LibraryItemViewModel? item = null;
            
            if (sender is ListBox listBox)
            {
                item = listBox.SelectedItem as LibraryItemViewModel;
            }

            if (item != null)
            {
                vm.SelectedItem = item;
                if (vm.PlayCommand.CanExecute(null))
                {
                    await vm.PlayCommand.ExecuteAsync(null);
                }
            }
        }

        private void OnLibraryTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is not TreeView tv) return;

            if (tv.SelectedItem is LibraryItemViewModel item)
            {
                vm.SelectedItem = item;
            }
        }

        private async void OnLibraryTreeDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is not TreeView tv) return;

            if (tv.SelectedItem is LibraryItemViewModel item)
            {
                vm.SelectedItem = item;
                if (vm.PlayCommand.CanExecute(null))
                {
                    await vm.PlayCommand.ExecuteAsync(null);
                }
            }
        }

        private async void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            // Don't steal keys while typing in text inputs.
            if (e.Source is TextBox)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.W:
                    if (vm.ConvertToWavCommand.CanExecute(null))
                    {
                        e.Handled = true;
                        await vm.ConvertToWavCommand.ExecuteAsync(null);
                    }
                    break;

                case Key.S:
                    if (vm.ConvertToScdCommand.CanExecute(null))
                    {
                        e.Handled = true;
                        await vm.ConvertToScdCommand.ExecuteAsync(null);
                    }
                    break;

                case Key.L:
                    if (vm.OpenLoopEditorCommand.CanExecute(null))
                    {
                        e.Handled = true;
                        await vm.OpenLoopEditorCommand.ExecuteAsync(null);
                    }
                    break;

                case Key.N:
                    e.Handled = true;
                    if (vm.SelectedItem != null && vm.QuickNormalizeItemCommand.CanExecute(vm.SelectedItem))
                    {
                        await vm.QuickNormalizeItemCommand.ExecuteAsync(vm.SelectedItem);
                    }
                    else if (vm.OpenNormalizeCommand.CanExecute(null))
                    {
                        vm.OpenNormalizeCommand.Execute(null);
                    }
                    break;

                case Key.Delete:
                    if (vm.DeleteSelectedCommand.CanExecute(null))
                    {
                        e.Handled = true;
                        await vm.DeleteSelectedCommand.ExecuteAsync(null);
                    }
                    break;
            }
        }

        private void OnGroupedListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is not ListBox source) return;
            if (!source.Classes.Contains("GroupedItemsList")) return;

            // Keep SelectedItem pointing at the most recently selected item.
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                if (e.AddedItems[e.AddedItems.Count - 1] is LibraryItemViewModel last)
                {
                    vm.SelectedItem = last;
                }
            }

            // Recompute global selection across all grouped lists.
            var selected = this.GetVisualDescendants()
                .OfType<ListBox>()
                .Where(lb => lb.Classes.Contains("GroupedItemsList"))
                .SelectMany(lb => (lb.SelectedItems ?? Array.Empty<object>()).OfType<LibraryItemViewModel>())
                .Distinct()
                .ToList();

            vm.SelectedItems = new System.Collections.ObjectModel.ObservableCollection<LibraryItemViewModel>(selected);
        }

        private void OnFlatListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is not ListBox listBox) return;

            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                if (e.AddedItems[e.AddedItems.Count - 1] is LibraryItemViewModel last)
                {
                    vm.SelectedItem = last;
                }
            }

            var selected = (listBox.SelectedItems ?? Array.Empty<object>()).OfType<LibraryItemViewModel>().Distinct().ToList();
            vm.SelectedItems = new System.Collections.ObjectModel.ObservableCollection<LibraryItemViewModel>(selected);
        }

        private void OnLibraryRowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
            {
                return;
            }

            _libraryPointerDown = true;
            _libraryPressPoint = e.GetPosition(this);

            // Row DataContext should be the item.
            if (sender is Control c && c.DataContext is LibraryItemViewModel item)
            {
                _libraryPressedItem = item;
            }
            else
            {
                _libraryPressedItem = null;
            }
        }

        private async void OnLibraryRowPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_libraryPointerDown) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
            {
                _libraryPointerDown = false;
                _libraryPressedItem = null;
                return;
            }

            var pos = e.GetPosition(this);
            var delta = pos - _libraryPressPoint;
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
            {
                return;
            }

            _libraryPointerDown = false;

            if (DataContext is not MainViewModel vm) return;

            IEnumerable<LibraryItemViewModel> items = vm.SelectedItems.Count > 0
                ? vm.SelectedItems
                : (_libraryPressedItem != null ? new[] { _libraryPressedItem } : Enumerable.Empty<LibraryItemViewModel>());
            var paths = items
                .Select(i => i.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p) && p.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetFullPath(p!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0) return;

            var data = new DataObject();
            data.Set(DataFormats.FileNames, paths);

            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
            catch
            {
                // Ignore drag failures.
            }
        }

        private void OnRandoFolderAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is not Control control) return;

            // Ensure we don't double-hook if the template reuses controls.
            control.RemoveHandler(DragDrop.DragOverEvent, OnRandoFolderDragOver);
            control.RemoveHandler(DragDrop.DropEvent, OnRandoFolderDrop);

            control.AddHandler(DragDrop.DragOverEvent, OnRandoFolderDragOver);
            control.AddHandler(DragDrop.DropEvent, OnRandoFolderDrop);
        }

        private void OnRandoFolderDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.None;

            if (sender is not Control c) return;
            if (c.DataContext is not RandoFolderGroupViewModel folder) return;
            if (string.IsNullOrWhiteSpace(folder.FolderPath) || !Directory.Exists(folder.FolderPath)) return;

            if (e.Data.Contains(DataFormats.FileNames))
            {
                var files = e.Data.GetFileNames()?.ToArray() ?? Array.Empty<string>();
                if (files.Any(f => f.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)))
                {
                    e.DragEffects = DragDropEffects.Copy;
                }
            }

            e.Handled = true;
        }

        private void OnRandoFolderDrop(object? sender, DragEventArgs e)
        {
            if (sender is not Control c) return;
            if (c.DataContext is not RandoFolderGroupViewModel folder) return;
            if (string.IsNullOrWhiteSpace(folder.FolderPath)) return;

            var destFolder = folder.FolderPath;
            if (!Directory.Exists(destFolder))
            {
                try { Directory.CreateDirectory(destFolder); } catch { return; }
            }

            var files = e.Data.GetFileNames()?.ToArray() ?? Array.Empty<string>();
            var copiedCount = 0;
            foreach (var src in files)
            {
                try
                {
                    if (!src.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(src)) continue;

                    var dest = Path.Combine(destFolder, Path.GetFileName(src));
                    File.Copy(src, dest, overwrite: true);

                    var fileName = Path.GetFileName(src);
                    if (!folder.Files.Any(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        folder.Files.Add(fileName);
                    }

                    copiedCount++;
                }
                catch
                {
                    // Best-effort.
                }
            }

            if (DataContext is MainViewModel vm && copiedCount > 0)
            {
                vm.KhRandoStatus = $"KH Rando: Dropped {copiedCount} file(s) → {folder.DisplayName}";
            }

            e.Handled = true;
        }

    }
}
