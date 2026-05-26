using System;
using System.Globalization;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    public class BoolToOnlineConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? "Онлайн" : "Оффлайн";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
