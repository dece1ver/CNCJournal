using libeLog;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace QCTasks.Converters;

/// <summary>Фон бейджа статуса</summary>
public class StatusToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush Accepted = Constants.Colors.AreopagGreen;
    private static readonly SolidColorBrush Rejected = Constants.Colors.AreopagRed;
    private static readonly SolidColorBrush Unknown = Constants.Colors.Gray;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "Принято" => Accepted,
            "Отклонено" => Rejected,
            _ => Unknown
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}