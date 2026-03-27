using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace QCTasks.Converters;

/// <summary>Цвет левого акцента карточки по статусу</summary>
public class StatusToAccentBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Accepted = new(Color.FromRgb(0x43, 0xA0, 0x47));
    private static readonly SolidColorBrush Rejected = new(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly SolidColorBrush Unknown = new(Color.FromRgb(0x9E, 0x9E, 0x9E));

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
