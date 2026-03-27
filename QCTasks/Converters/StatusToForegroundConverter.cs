using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace QCTasks.Converters;

/// <summary>Цвет текста статуса</summary>
public class StatusToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush Accepted = new(Color.FromRgb(0x1B, 0x5E, 0x20));
    private static readonly SolidColorBrush Rejected = new(Color.FromRgb(0xB7, 0x1C, 0x1C));
    private static readonly SolidColorBrush Unknown = new(Color.FromRgb(0x75, 0x75, 0x75));

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
