using libeLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    class ShortDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                return dateTime == DateTime.MinValue ? string.Empty : dateTime.ToString(Constants.ShortDateFormat);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            DateTime.TryParseExact(value.ToString(), Constants.ShortDateFormat, null, DateTimeStyles.None, out var dateTime);
            return dateTime;
        }
    }
}