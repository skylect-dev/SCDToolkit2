using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SCDToolkit.Desktop.Converters
{
    public class BoolToAngleConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? 90d : 0d;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return d >= 90;
            }
            return false;
        }
    }
}
