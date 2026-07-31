using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace remeLog.Infrastructure.Converters
{
    public class IntNullableConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int?)
            {
                return value;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return int.TryParse((value.ToString() ?? string.Empty), out int i) ? i : DependencyProperty.UnsetValue;
        }
    }
}
