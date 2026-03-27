using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace QCTasks.Converters;

/// <summary>Фон бейджа статуса</summary>
public class StatusToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush Accepted = new(Color.FromRgb(0xE8, 0xF5, 0xE9));
    private static readonly SolidColorBrush Rejected = new(Color.FromRgb(0xFF, 0xEB, 0xEE));
    private static readonly SolidColorBrush Unknown = new(Color.FromRgb(0xF5, 0xF5, 0xF5));

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