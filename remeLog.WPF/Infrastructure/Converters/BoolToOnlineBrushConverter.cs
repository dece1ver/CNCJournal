using System;
using System.Globalization;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    public class BoolToOnlineBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? ThemeBrushes.Ok : ThemeBrushes.Disabled;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
