using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    /// <summary>
    /// Видимость колонки PartsInfoWindow по набору видимых ID текущего профиля
    /// (встроенная роль или пользовательский профиль столбцов — см.
    /// PartsInfoWindowViewModel.VisibleColumnIds). Parameter — стабильный ID
    /// колонки (тот же, что и inf:ColumnId.Id на этой же колонке).
    /// </summary>
    public class ColumnVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HashSet<string> visibleColumnIds && parameter is string columnId)
            {
                return visibleColumnIds.Contains(columnId)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
