using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SCDToolkit.Desktop.Models;

namespace SCDToolkit.Desktop.Controls
{
    public sealed class WaveformView : Control
    {
        public event EventHandler<int>? UserPlayheadChanged;
        public event EventHandler<int>? UserPlayheadScrubStarted;
        public event EventHandler<int>? UserPlayheadScrubEnded;
        public static readonly StyledProperty<WaveformData?> WaveformProperty =
            AvaloniaProperty.Register<WaveformView, WaveformData?>(nameof(Waveform));

        public static readonly StyledProperty<double> SamplesPerPixelProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(SamplesPerPixel), 200d, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<int> ViewStartSampleProperty =
            AvaloniaProperty.Register<WaveformView, int>(nameof(ViewStartSample), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<int> LoopStartProperty =
            AvaloniaProperty.Register<WaveformView, int>(nameof(LoopStart), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<int> LoopEndProperty =
            AvaloniaProperty.Register<WaveformView, int>(nameof(LoopEnd), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<int> PlayheadSampleProperty =
            AvaloniaProperty.Register<WaveformView, int>(nameof(PlayheadSample), 0);

        public static readonly StyledProperty<IBrush?> BackgroundProperty =
            AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(Background));

        public static readonly StyledProperty<int> SampleRateProperty =
            AvaloniaProperty.Register<WaveformView, int>(nameof(SampleRate), 0);

        public static readonly StyledProperty<double> ViewportWidthProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(ViewportWidth), 0d);

        public static readonly StyledProperty<double> HorizontalOffsetProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(HorizontalOffset), 0d);

        public WaveformData? Waveform
        {
            get => GetValue(WaveformProperty);
            set => SetValue(WaveformProperty, value);
        }

        public double SamplesPerPixel
        {
            get => GetValue(SamplesPerPixelProperty);
            set => SetValue(SamplesPerPixelProperty, value);
        }

        public int ViewStartSample
        {
            get => GetValue(ViewStartSampleProperty);
            set => SetValue(ViewStartSampleProperty, value);
        }

        public int LoopStart
        {
            get => GetValue(LoopStartProperty);
            set => SetValue(LoopStartProperty, value);
        }

        public int LoopEnd
        {
            get => GetValue(LoopEndProperty);
            set => SetValue(LoopEndProperty, value);
        }

        public int PlayheadSample
        {
            get => GetValue(PlayheadSampleProperty);
            set => SetValue(PlayheadSampleProperty, value);
        }

        public IBrush? Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public int SampleRate
        {
            get => GetValue(SampleRateProperty);
            set => SetValue(SampleRateProperty, value);
        }

        public double ViewportWidth
        {
            get => GetValue(ViewportWidthProperty);
            set => SetValue(ViewportWidthProperty, value);
        }

        public double HorizontalOffset
        {
            get => GetValue(HorizontalOffsetProperty);
            set => SetValue(HorizontalOffsetProperty, value);
        }

        private const double MinSamplesPerPixel = 0.01; // deep zoom-in
        private const double MaxSamplesPerPixel = 16384;
        private bool _draggingStart;
        private bool _draggingEnd;
        private bool _scrubbing;
        private Point _lastPoint;

        static WaveformView()
        {
            FocusableProperty.OverrideDefaultValue(typeof(WaveformView), true);
        }

        public WaveformView()
        {
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Arrow);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var wf = Waveform;
            if (wf == null || wf.TotalSamples <= 0)
            {
                return;
            }

            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            // Background fill
            if (Background is { } bg)
            {
                context.FillRectangle(bg, new Rect(0, 0, width, height));
            }

            var spp = Math.Clamp(SamplesPerPixel, MinSamplesPerPixel, MaxSamplesPerPixel);

            // Virtualize drawing: only render the visible portion of the (potentially huge) waveform.
            var viewportWidth = ViewportWidth > 1 ? ViewportWidth : width;
            var visibleStartX = Math.Clamp(HorizontalOffset, 0, Math.Max(0, width - viewportWidth));
            var visibleEndX = Math.Min(width, visibleStartX + viewportWidth);

            DrawRuler(context, viewportWidth, height, spp, visibleStartX);
            DrawWaveform(context, wf, viewportWidth, height, spp, visibleStartX);
            DrawLoopRegion(context, viewportWidth, height, spp, visibleStartX);
            DrawPlayhead(context, viewportWidth, height, spp, visibleStartX);
        }

        private void DrawWaveform(DrawingContext context, WaveformData wf, double viewportWidth, double height, double spp, double visibleStartX)
        {
            var mid = height / 2.0;
            var amp = height / 2.0;
            var startX = (int)Math.Floor(visibleStartX);
            var endX = (int)Math.Ceiling(visibleStartX + viewportWidth);

            // High zoom-in: render a single line (faster + matches reference look).
            if (spp <= 1.0)
            {
                DrawWaveformLine(context, wf, height, spp, startX, endX);
                return;
            }

            var pen = new Pen(new SolidColorBrush(Color.FromUInt32(0xFF76B4FF)), 1.2);
            var fillBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.FromUInt32(0x403B82F6), 0),
                    new GradientStop(Color.FromUInt32(0x204B5C80), 1)
                }
            };

            var zeroPen = new Pen(new SolidColorBrush(Color.FromUInt32(0x30FFFFFF)), 1);
            context.DrawLine(zeroPen, new Point(startX, mid), new Point(endX, mid));

            var level = PickLevel(wf, spp);
            var channels = wf.Channels;
            var samples = wf.Samples;

            for (int x = startX; x < endX; x++)
            {
                var startSample = (int)Math.Floor(x * spp);
                if (startSample >= wf.TotalSamples)
                {
                    break;
                }

                var endSample = Math.Min(wf.TotalSamples, (int)Math.Floor((x + 1) * spp));
                if (endSample <= startSample)
                {
                    endSample = startSample + 1;
                }

                float min = 1f;
                float max = -1f;

                if (spp <= 8 || level == null)
                {
                    // Use raw samples for high zoom
                    var end = Math.Min(endSample, wf.TotalSamples);
                    for (int s = startSample; s < end; s++)
                    {
                        for (int c = 0; c < channels; c++)
                        {
                            var v = samples[c][s];
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                    }
                }
                else
                {
                    // Use aggregated peaks
                    var block = level.BlockSize;
                    var idxStart = startSample / block;
                    var idxEnd = (endSample - 1) / block;
                    for (int i = idxStart; i <= idxEnd && i < level.Min[0].Length; i++)
                    {
                        for (int c = 0; c < channels; c++)
                        {
                            var vMin = level.Min[c][i];
                            var vMax = level.Max[c][i];
                            if (vMin < min) min = vMin;
                            if (vMax > max) max = vMax;
                        }
                    }
                }

                var xPos = x + 0.5;
                var y1 = mid - max * amp;
                var y2 = mid - min * amp;
                context.DrawLine(pen, new Point(xPos, y1), new Point(xPos, y2));
            }

            // Subtle fill to show overall range
            context.FillRectangle(fillBrush, new Rect(startX, 0, Math.Max(0, endX - startX), height));
        }

        private void DrawWaveformLine(DrawingContext context, WaveformData wf, double height, double spp, int startX, int endX)
        {
            var mid = height / 2.0;
            var amp = height / 2.0;
            var pen = new Pen(new SolidColorBrush(Color.FromUInt32(0xFFE8EAEE)), 1.0);

            // Build a polyline of the visible samples.
            // For multi-channel, average channels for display.
            var channels = wf.Channels;
            var samples = wf.Samples;

            var geometry = new StreamGeometry();
            using (var gc = geometry.Open())
            {
                bool started = false;
                for (int x = startX; x < endX; x++)
                {
                    var sampleIndex = (int)Math.Floor(x * spp);
                    if (sampleIndex < 0) continue;
                    if (sampleIndex >= wf.TotalSamples) break;

                    float v = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        v += samples[c][sampleIndex];
                    }
                    v /= Math.Max(1, channels);

                    var y = mid - v * amp;
                    var p = new Point(x + 0.5, y);
                    if (!started)
                    {
                        gc.BeginFigure(p, false);
                        started = true;
                    }
                    else
                    {
                        gc.LineTo(p);
                    }
                }
            }

            context.DrawGeometry(null, pen, geometry);
        }

        private void DrawLoopRegion(DrawingContext context, double viewportWidth, double height, double spp, double visibleStartX)
        {
            if (LoopEnd <= LoopStart)
            {
                return;
            }

            var visibleEndX = visibleStartX + viewportWidth;
            var loopStartX = LoopStart / spp;
            var loopEndX = LoopEnd / spp;

            var visibleStart = Math.Max(loopStartX, visibleStartX);
            var visibleEnd = Math.Min(loopEndX, visibleEndX);
            if (visibleEnd <= visibleStart)
            {
                return;
            }

            var region = new Rect(visibleStart, 0, visibleEnd - visibleStart, height);
            context.FillRectangle(new SolidColorBrush(Color.FromUInt32(0x3032c56d)), region);

            var handleBrushStart = new SolidColorBrush(Color.FromUInt32(0xFF32c56d));
            var handleBrushEnd = new SolidColorBrush(Color.FromUInt32(0xFFd95b5b));
            var handleWidth = 6;

            var labelTypeface = new Typeface("Segoe UI");
            if (loopStartX >= visibleStartX - 20 && loopStartX <= visibleEndX + 20)
            {
                context.FillRectangle(handleBrushStart, new Rect(loopStartX - handleWidth / 2.0, 0, handleWidth, height));
            }
            if (loopEndX >= visibleStartX - 20 && loopEndX <= visibleEndX + 20)
            {
                context.FillRectangle(handleBrushEnd, new Rect(loopEndX - handleWidth / 2.0, 0, handleWidth, height));
            }

            // Labels
            var startText = new FormattedText("Loop Start", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, labelTypeface, 11, handleBrushStart);
            var endText = new FormattedText("Loop End", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, labelTypeface, 11, handleBrushEnd);
            if (loopStartX >= visibleStartX - 200 && loopStartX <= visibleEndX + 200)
            {
                context.DrawText(startText, new Point(loopStartX + 8, 8));
            }
            if (loopEndX >= visibleStartX - 200 && loopEndX <= visibleEndX + 200)
            {
                context.DrawText(endText, new Point(loopEndX + 8, 8));
            }
        }

        private void DrawPlayhead(DrawingContext context, double viewportWidth, double height, double spp, double visibleStartX)
        {
            var s = PlayheadSample;

            var visibleEndX = visibleStartX + viewportWidth;
            var x = s / spp;
            if (x < visibleStartX || x > visibleEndX)
            {
                return;
            }

            var pen = new Pen(new SolidColorBrush(Color.FromUInt32(0xFFe5e7eb)), 1);
            context.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        private void DrawRuler(DrawingContext context, double viewportWidth, double height, double spp, double visibleStartX)
        {
            if (SampleRate <= 0)
            {
                return;
            }

            var visibleEndX = visibleStartX + viewportWidth;
            var secondsPerPixel = spp / SampleRate;
            var approxStep = (viewportWidth / 8.0) * secondsPerPixel; // aim for ~8 ticks

            // Choose a "nice" step size so labels stay readable at any zoom level.
            var steps = new[]
            {
                0.1, 0.25, 0.5,
                1, 2, 5,
                10, 15, 30,
                60, 120, 300
            };
            var step = steps.FirstOrDefault(s => s >= approxStep);
            if (step <= 0) step = steps[^1];

            var startSeconds = (visibleStartX * spp) / SampleRate;
            var firstTick = Math.Floor(startSeconds / step) * step;
            var tickPen = new Pen(new SolidColorBrush(Color.FromUInt32(0x50FFFFFF)), 1);
            var textBrush = new SolidColorBrush(Color.FromUInt32(0xB0FFFFFF));
            var typeface = new Typeface("Segoe UI");

            for (double t = firstTick; ; t += step)
            {
                var x = (t * SampleRate) / spp;
                if (x > visibleEndX + 40) break;
                if (x < visibleStartX - 40) continue;

                context.DrawLine(tickPen, new Point(x, 0), new Point(x, 12));

                var label = FormatTime(t, step);
                var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, textBrush);
                context.DrawText(ft, new Point(x + 4, 0));
            }
        }

        private static string FormatTime(double seconds, double stepSeconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);

            // When zoomed in enough to show sub-second ticks, include milliseconds.
            if (stepSeconds < 1)
            {
                return ts.ToString(ts.TotalHours >= 1 ? @"hh\:mm\:ss\.fff" : @"m\:ss\.fff", CultureInfo.InvariantCulture);
            }

            return ts.ToString(ts.TotalHours >= 1 ? @"hh\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
        }

        private PeakLevel? PickLevel(WaveformData wf, double spp)
        {
            if (wf.PeakLevels.Count == 0)
            {
                return null;
            }

            var target = Math.Max(1, spp);
            PeakLevel? best = null;
            double bestDiff = double.MaxValue;
            foreach (var level in wf.PeakLevels)
            {
                var diff = Math.Abs(level.BlockSize - target);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = level;
                }
            }
            return best;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var wf = Waveform;
            if (wf == null) return;

            var p = e.GetCurrentPoint(this);
            _lastPoint = p.Position;
            Focus();

            var spp = Math.Clamp(SamplesPerPixel, MinSamplesPerPixel, MaxSamplesPerPixel);
            var startX = LoopStart / spp;
            var endX = LoopEnd / spp;
            var hitMargin = 8;

            if (Math.Abs(p.Position.X - startX) <= hitMargin)
            {
                _draggingStart = true;
                e.Pointer.Capture(this);
                return;
            }
            if (Math.Abs(p.Position.X - endX) <= hitMargin)
            {
                _draggingEnd = true;
                e.Pointer.Capture(this);
                return;
            }

            // Click set playhead
            var sample = (int)Math.Round(p.Position.X * spp);
            sample = Math.Clamp(sample, 0, wf.TotalSamples - 1);
            SetCurrentValue(PlayheadSampleProperty, sample);

            // Begin scrubbing so click+drag seeks continuously.
            if (p.Properties.IsLeftButtonPressed)
            {
                _scrubbing = true;
                e.Pointer.Capture(this);
                UserPlayheadScrubStarted?.Invoke(this, sample);
                UserPlayheadChanged?.Invoke(this, sample);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var wf = Waveform;
            if (wf == null) return;

            var p = e.GetCurrentPoint(this);
            var pos = p.Position;
            var spp = Math.Clamp(SamplesPerPixel, MinSamplesPerPixel, MaxSamplesPerPixel);

            if (_draggingStart)
            {
                var sample = (int)Math.Round(pos.X * spp);
                sample = Math.Clamp(sample, 0, Math.Max(0, LoopEnd - 1));
                SetCurrentValue(LoopStartProperty, sample);
                e.Handled = true;
                return;
            }

            if (_draggingEnd)
            {
                var sample = (int)Math.Round(pos.X * spp);
                sample = Math.Clamp(sample, Math.Max(LoopStart + 1, 1), wf.TotalSamples - 1);
                SetCurrentValue(LoopEndProperty, sample);
                e.Handled = true;
                return;
            }

            if (_scrubbing && p.Properties.IsLeftButtonPressed)
            {
                var sample = (int)Math.Round(pos.X * spp);
                sample = Math.Clamp(sample, 0, wf.TotalSamples - 1);
                SetCurrentValue(PlayheadSampleProperty, sample);
                UserPlayheadChanged?.Invoke(this, sample);
                e.Handled = true;
                return;
            }

            _lastPoint = pos;

            // Hover cursor change
            var startX = LoopStart / spp;
            var endX = LoopEnd / spp;
            var hitMargin = 8;
            if (Math.Abs(pos.X - startX) <= hitMargin || Math.Abs(pos.X - endX) <= hitMargin)
            {
                Cursor = new Cursor(StandardCursorType.SizeWestEast);
            }
            else
            {
                Cursor = new Cursor(StandardCursorType.Arrow);
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _draggingStart = _draggingEnd = false;
            if (_scrubbing)
            {
                var wf = Waveform;
                if (wf != null)
                {
                    var spp = Math.Clamp(SamplesPerPixel, MinSamplesPerPixel, MaxSamplesPerPixel);
                    var p = e.GetCurrentPoint(this);
                    var sample = (int)Math.Round(p.Position.X * spp);
                    sample = Math.Clamp(sample, 0, wf.TotalSamples - 1);
                    UserPlayheadScrubEnded?.Invoke(this, sample);
                }
            }

            _scrubbing = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WaveformProperty ||
                change.Property == SamplesPerPixelProperty ||
                change.Property == ViewStartSampleProperty ||
                change.Property == LoopStartProperty ||
                change.Property == LoopEndProperty ||
                change.Property == PlayheadSampleProperty ||
                change.Property == BackgroundProperty ||
                change.Property == ViewportWidthProperty ||
                change.Property == HorizontalOffsetProperty)
            {
                InvalidateVisual();
            }
        }
    }
}
