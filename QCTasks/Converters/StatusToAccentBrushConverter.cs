using libeLog;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace QCTasks.Converters;

/// <summary>Цвет левого акцента карточки по статусу</summary>
public class StatusToAccentBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InWork = Constants.Colors.AreopagBlue;
    private static readonly SolidColorBrush Accepted = Constants.Colors.AreopagGreen;
    private static readonly SolidColorBrush Rejected = Constants.Colors.AreopagRed;
    private static readonly SolidColorBrush Unknown = Constants.Colors.AreopagBlue60;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "В работе" => InWork,
            "Принято" => Accepted,
            "Отклонено" => Rejected,
            _ => Unknown
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
