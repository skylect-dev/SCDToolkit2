using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Interactivity;
using SCDToolkit.Desktop.ViewModels;
using System;
using SCDToolkit.Desktop.Controls;
using System.Diagnostics;

namespace SCDToolkit.Desktop.Views
{
    public partial class LoopEditorWindow : Window
    {
        private ScrollViewer? _waveformScroller;
        private WaveformView? _waveformView;
        private readonly Stopwatch _seekThrottle = Stopwatch.StartNew();

        public LoopEditorWindow()
        {
            InitializeComponent();
            this.Opened += OnOpened;
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            _waveformScroller = this.FindControl<ScrollViewer>("WaveformScroller");
            _waveformView = this.FindControl<WaveformView>("WaveformView");
            if (_waveformScroller != null && DataContext is LoopEditorViewModel vm)
            {
                SyncWaveformViewport();
                _waveformScroller.PropertyChanged += WaveformScrollerOnPropertyChanged;
            }

            if (_waveformView != null)
            {
                _waveformView.PointerWheelChanged += WaveformViewOnPointerWheelChanged;
                _waveformView.UserPlayheadChanged += WaveformViewOnUserPlayheadChanged;
                _waveformView.UserPlayheadScrubStarted += WaveformViewOnUserPlayheadScrubStarted;
                _waveformView.UserPlayheadScrubEnded += WaveformViewOnUserPlayheadScrubEnded;
            }
        }

        private void WaveformViewOnUserPlayheadScrubStarted(object? sender, int sample)
        {
            if (DataContext is not LoopEditorViewModel vm)
            {
                return;
            }

            vm.BeginScrub();
        }

        private async void WaveformViewOnUserPlayheadScrubEnded(object? sender, int sample)
        {
            if (DataContext is not LoopEditorViewModel vm)
            {
                return;
            }

            await vm.SeekPreviewToSampleAsync(sample);
            await vm.EndScrubAsync();
        }

        private async void WaveformViewOnUserPlayheadChanged(object? sender, int sample)
        {
            if (DataContext is not LoopEditorViewModel vm)
            {
                return;
            }

            // Throttle seeks to avoid overwhelming audio thread while scrubbing.
            if (_seekThrottle.ElapsedMilliseconds < 25)
            {
                return;
            }
            _seekThrottle.Restart();

            await vm.SeekPreviewToSampleAsync(sample);
        }

        private void WaveformScrollerOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.BoundsProperty || e.Property == ScrollViewer.OffsetProperty)
            {
                SyncWaveformViewport();
            }
        }

        private void SyncWaveformViewport()
        {
            if (_waveformScroller != null && DataContext is LoopEditorViewModel vm)
            {
                vm.ViewportWidth = _waveformScroller.Bounds.Width;
            }

            if (_waveformScroller != null && _waveformView != null)
            {
                _waveformView.ViewportWidth = _waveformScroller.Bounds.Width;
                _waveformView.HorizontalOffset = _waveformScroller.Offset.X;
            }
        }

        private void WaveformViewOnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (_waveformScroller == null || DataContext is not LoopEditorViewModel vm)
            {
                return;
            }

            // Use viewport-relative mouse X so anchoring is stable.
            var cursorX = e.GetPosition(_waveformScroller).X;
            if (cursorX < 0) cursorX = 0;
            if (vm.ViewportWidth > 1 && cursorX > vm.ViewportWidth) cursorX = vm.ViewportWidth;

            var oldSpp = vm.SamplesPerPixel;
            var factor = e.Delta.Y > 0 ? 0.9 : 1.1;
            var requested = oldSpp * factor;

            // Compute anchor sample under cursor before zoom.
            var anchorSample = (_waveformScroller.Offset.X + cursorX) * oldSpp;

            vm.SamplesPerPixel = requested; // clamped in VM using ViewportWidth
            var newSpp = vm.SamplesPerPixel;

            // Keep the same sample under the cursor by adjusting the scroll offset.
            var expectedWidth = vm.TotalSamples > 0 ? (vm.TotalSamples / newSpp) : 0;
            if (expectedWidth < vm.ViewportWidth) expectedWidth = vm.ViewportWidth;
            var maxOffset = Math.Max(0, expectedWidth - vm.ViewportWidth);

            var newOffsetX = (anchorSample / newSpp) - cursorX;
            if (newOffsetX < 0) newOffsetX = 0;
            if (newOffsetX > maxOffset) newOffsetX = maxOffset;

            // Post to allow layout/extent to update with new width.
            Dispatcher.UIThread.Post(() =>
            {
                if (_waveformScroller == null) return;
                _waveformScroller.Offset = new Vector(newOffsetX, _waveformScroller.Offset.Y);
                SyncWaveformViewport();
            }, DispatcherPriority.Background);

            e.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not LoopEditorViewModel vm)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.S:
                    vm.SetLoopStartAtCursorCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.E:
                    vm.SetLoopEndAtCursorCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.C:
                    vm.ClearLoopPointsCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.L:
                    vm.ToggleLoopPlaybackCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Space:
                    vm.TogglePreviewPlayPauseCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}
