using remeLog.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace remeLog.Infrastructure.Converters
{
    /// <summary>
    /// Цвет статуса станка: изготовление/наладка/простой/нет данных (протухший heartbeat).
    /// Цвета взяты из remeLog.Web/wwwroot/app.css (--signal-ok/--signal-warn/--signal-alert/--ink-faint),
    /// чтобы WPF-окно и веб-дашборд красили один и тот же статус одинаково.
    /// </summary>
    public class MachineActivityStatusBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Machining = new(Color.FromRgb(0x00, 0xAD, 0x68));
        private static readonly SolidColorBrush Setup = new(Color.FromRgb(0xB8, 0x86, 0x2E));
        private static readonly SolidColorBrush Idle = new(Color.FromRgb(0xE6, 0x3C, 0x2F));
        private static readonly SolidColorBrush NoData = new(Color.FromRgb(0x75, 0x7C, 0x95));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not MachineActivity activity || activity.IsStale) return NoData;

            return activity.Status switch
            {
                MachineActivity.MachiningStatus => Machining,
                MachineActivity.SetupStatus => Setup,
                _ => Idle,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
