using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace remeLog.Infrastructure.Converters
{
    public class BoolToOnlineBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(Color.FromRgb(158, 158, 158));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
