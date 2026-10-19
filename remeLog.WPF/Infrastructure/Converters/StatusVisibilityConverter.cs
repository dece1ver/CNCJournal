using remeLog.Infrastructure.Types;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    internal class StatusVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch ((CheckStatus)value)
            {
                case CheckStatus.Ok:
                    if (Application.Current.TryFindResource("StatusOkIcon") is Viewbox statusOk)
                    {
                        return statusOk;
                    }
                    break;
                case CheckStatus.Warning:
                    if (Application.Current.TryFindResource("StatusWarningIcon") is Viewbox statusWarning)
                    {
                        return statusWarning;
                    }
                    break;
                case CheckStatus.Error:
                    if (Application.Current.TryFindResource("StatusErrorIcon") is Viewbox statusErr)
                    {
                        return statusErr;
                    }
                    break;
                case CheckStatus.Sync:
                    if (Application.Current.TryFindResource("SyncIcon") is Viewbox sync)
                    {
                        return sync;
                    }
                    break;
            }
            return new object();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}