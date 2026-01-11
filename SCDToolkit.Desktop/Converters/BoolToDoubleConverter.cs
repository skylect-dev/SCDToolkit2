using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SCDToolkit.Desktop.Converters
{
    public sealed class BoolToDoubleConverter : IValueConverter
    {
        // ConverterParameter supports "trueValue|falseValue" (e.g. "1|0" or "5000|0").
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var b = value is true;

            var trueValue = 1d;
            var falseValue = 0d;

            if (parameter is string s)
            {
                var parts = s.Split('|');
                if (parts.Length == 2)
                {
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                        trueValue = t;
                    if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                        falseValue = f;
                }
            }

            return b ? trueValue : falseValue;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return d > 0;
            }
            return false;
        }
    }
}
