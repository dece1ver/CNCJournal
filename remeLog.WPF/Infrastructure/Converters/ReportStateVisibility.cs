using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using static remeLog.Models.CombinedParts;

namespace remeLog.Infrastructure.Converters
{
    internal class ReportStateVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ReportState reportState)
            {
                return reportState switch
                {
                    ReportState.Exist => parameter.Equals("Exist") ? Visibility.Visible : Visibility.Collapsed,
                    ReportState.Partial => parameter.Equals("Partial") ? Visibility.Visible : Visibility.Collapsed,
                    ReportState.NotExist => parameter.Equals("NotExist") ? Visibility.Visible : Visibility.Collapsed,
                    _ => (object)Visibility.Collapsed,
                };
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
