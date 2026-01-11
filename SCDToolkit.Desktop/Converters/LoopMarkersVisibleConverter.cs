using System;
using System.Collections.Generic;
using Avalonia.Data.Converters;

namespace SCDToolkit.Desktop.Converters
{
    /// <summary>
    /// Returns true when loop end is greater than loop start, used to show/hide loop markers.
    /// </summary>
    public sealed class LoopMarkersVisibleConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Count < 2)
            {
                return false;
            }

            var start = ToInt(values[0]);
            var end = ToInt(values[1]);
            return end > start && start >= 0;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static int ToInt(object? value)
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                float f => (int)f,
                string s when int.TryParse(s, out var v) => v,
                _ => 0
            };
        }
    }
}
