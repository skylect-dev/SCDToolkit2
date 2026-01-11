using System;
using System.Collections.Generic;
using Avalonia.Data.Converters;

namespace SCDToolkit.Desktop.Converters
{
    public sealed class LoopMarkerOffsetConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Count < 3)
            {
                return 0d;
            }

            double loopSeconds = ToDouble(values[0]);
            double durationSeconds = ToDouble(values[1]);
            double width = ToDouble(values[2]);

            if (durationSeconds <= 0 || width <= 0 || loopSeconds < 0)
            {
                return 0d;
            }

            var ratio = Math.Clamp(loopSeconds / durationSeconds, 0d, 1d);
            return ratio * width;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static double ToDouble(object? value)
        {
            return value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                string s when double.TryParse(s, out var v) => v,
                _ => 0d
            };
        }
    }
}
