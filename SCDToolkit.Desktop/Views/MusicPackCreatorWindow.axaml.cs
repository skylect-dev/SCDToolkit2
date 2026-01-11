using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SCDToolkit.Desktop.ViewModels;

namespace SCDToolkit.Desktop.Views;

public partial class MusicPackCreatorWindow : Window
{
    private Point? _dragStart;

    public MusicPackCreatorWindow()
    {
        InitializeComponent();

        // DragDrop routed events aren't bindable via XAML in this Avalonia version.
        AddHandler(DragDrop.DragOverEvent, TrackRow_DragOver, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DropEvent, TrackRow_Drop, RoutingStrategies.Bubble);

        // Ensure file pickers are hosted by this window (prevents focusing main window).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MusicPackCreatorViewModel vm)
            {
                vm.AttachStorageProvider(StorageProvider);
            }
        };

        Opened += (_, _) =>
        {
            if (DataContext is MusicPackCreatorViewModel vm)
            {
                vm.AttachStorageProvider(StorageProvider);
            }
        };
    }

    public IEnumerable<SourceFileOptionViewModel> SourceOptions
        => (IEnumerable<SourceFileOptionViewModel>?)((DataContext as MusicPackCreatorViewModel)?.SourceOptions)
            ?? Array.Empty<SourceFileOptionViewModel>();

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LibraryItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private async void LibraryItem_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null)
            return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed is false)
            return;

        var pos = e.GetPosition(this);
        var delta = pos - _dragStart.Value;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
            return;

        _dragStart = null;

        if (DataContext is not MusicPackCreatorViewModel vm)
            return;
        if (vm.SelectedLibrarySource is null)
            return;

        var data = new DataObject();
        data.Set(DataFormats.Text, vm.SelectedLibrarySource.Path);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    private void TrackRow_DragOver(object? sender, DragEventArgs e)
    {
        var (vm, track) = TryGetDropTarget(e);
        e.DragEffects = (vm != null && track != null && e.Data.Contains(DataFormats.Text))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TrackRow_Drop(object? sender, DragEventArgs e)
    {
        var (vm, track) = TryGetDropTarget(e);
        if (vm is null || track is null)
            return;

        var path = e.Data.GetText();
        if (string.IsNullOrWhiteSpace(path))
            return;

        vm.AssignTrack(track, path);
        e.Handled = true;
    }

    private (MusicPackCreatorViewModel? vm, MusicPackTrackViewModel? track) TryGetDropTarget(DragEventArgs e)
    {
        if (DataContext is not MusicPackCreatorViewModel vm)
            return (null, null);

        // Find the nearest control in the visual tree whose DataContext is a track.
        var c = e.Source as Control;
        while (c != null && c.DataContext is not MusicPackTrackViewModel)
        {
            c = c.Parent as Control;
        }

        return (vm, c?.DataContext as MusicPackTrackViewModel);
    }
}
