using libeLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace remeLog.Infrastructure.Converters
{
    public class TimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeOnly time)
            {
                return time == TimeOnly.MinValue ? string.Empty : time.ToString(Constants.HHmmssFormat);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (TimeOnly.TryParseExact(value.ToString(), Constants.HHmmFormat, null, DateTimeStyles.None, out var HHmmTime))
            {
                return HHmmTime;
            }
            else if (TimeOnly.TryParseExact(value.ToString(), Constants.HHmmssFormat, null, DateTimeStyles.None, out var HHmmssTime))
            {
                return HHmmssTime;
            }
            return DependencyProperty.UnsetValue;
        }
    }
}
