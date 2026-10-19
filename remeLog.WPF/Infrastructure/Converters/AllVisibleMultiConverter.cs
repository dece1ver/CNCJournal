using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    /// <summary>
    /// Visible, только если ВСЕ переданные значения (bool или Visibility) — "истинны".
    /// Используется, когда видимость колонки одновременно зависит от нескольких
    /// независимых условий (например, штатной логики колонки и текущего профиля
    /// столбцов — см. MachineColumn/AiCheckColumn в PartsInfoWindow.xaml).
    /// </summary>
    public class AllVisibleMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            foreach (var value in values)
            {
                var visible = value switch
                {
                    Visibility v => v == Visibility.Visible,
                    bool b => b,
                    _ => false
                };
                if (!visible) return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
