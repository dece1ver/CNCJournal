using remeLog.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    internal class MachineInspectionCalendarCellIconMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 0 && values[0] is MachineInspectionCalendarCell cell)
            {
                var key = cell.IsChecked ? "StatusOkIcon" : "StatusErrorIcon";
                return Application.Current.TryFindResource(key) ?? DependencyProperty.UnsetValue;
            }
            return DependencyProperty.UnsetValue;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
