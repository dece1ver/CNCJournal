using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    class CollectionConverteter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ICollection<string> { Count: > 0 } values ? string.Join(Environment.NewLine, values) : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s))
            {
                return s.Split(Environment.NewLine).ToList();
            }
            return new List<string>();
        }
    }
}
