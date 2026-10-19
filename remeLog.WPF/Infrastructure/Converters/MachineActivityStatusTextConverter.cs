using remeLog.Models;
using System;
using System.Globalization;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    /// <summary>Текст статуса станка с учётом протухшего heartbeat ("Нет данных").</summary>
    public class MachineActivityStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is MachineActivity activity
                ? (activity.IsStale ? "Нет данных" : activity.Status)
                : "Нет данных";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
