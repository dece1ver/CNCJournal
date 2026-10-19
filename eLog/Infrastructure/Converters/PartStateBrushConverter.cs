using eLog.Models;
using libeLog;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;

namespace eLog.Infrastructure.Converters
{
    /// <summary>Цвет маркера состояния карточки детали (корпоративные сигналы Areopag).</summary>
    public class PartStateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Part part)
                return Constants.Colors.Gray;

            // Открытый простой требует завершения — предупреждение.
            if (!part.DownTimesIsClosed)
                return new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x2E));

            // Деталь в работе (наладка или изготовление) — акцент.
            if (part.IsStarted)
                return Constants.Colors.AreopagBlue;

            // Завершена и синхронизирована — подтверждение.
            if (part.IsSynced)
                return Constants.Colors.AreopagGreen;

            return Constants.Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
