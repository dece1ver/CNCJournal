using remeLog.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    internal class MachineInspectionCalendarCellConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MachineInspectionCalendarDayRow row && parameter is string machine)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell.Machine == machine) return cell;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
