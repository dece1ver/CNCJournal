using System;
using System.Globalization;
using System.Windows;

namespace remeLog.Infrastructure.Converters
{
    public class DoubleNullableConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return double.TryParse((value.ToString() ?? string.Empty).Replace(",", "."), out double d) ? d : DependencyProperty.UnsetValue;
        }
    }
}
