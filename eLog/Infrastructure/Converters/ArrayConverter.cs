using eLog.Infrastructure;
using eLog.Infrastructure.Extensions;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace eLog.Infrastructure.Converters
{
    internal class ArrayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string[] { Length: > 0 } values ? string.Join("; ", values.Skip(1)) : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var first = new[] { Text.WithoutOrderItem };
            var arr = value.ToString()?.Split(';')
                .Select(q => q.Trim())
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .ToArray() ?? Array.Empty<string>();
            var res = new string[first.Length + arr.Length];
            first.CopyTo(res, 0);
            arr.CopyTo(res, first.Length);
            return res;
        }
    }
}