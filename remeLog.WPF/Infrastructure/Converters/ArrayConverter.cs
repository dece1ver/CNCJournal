using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    internal class ArrayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string[] { Length: > 0 } values ? string.Join("; ", values) : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {

            var arr = value.ToString()?.Split(';')
                .Select(q => q.Trim())
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .ToArray() ?? Array.Empty<string>();
            var res = new string[arr.Length];
            arr.CopyTo(res, arr.Length);
            return res;
        }
    }
}