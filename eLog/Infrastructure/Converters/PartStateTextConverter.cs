using eLog.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

namespace eLog.Infrastructure.Converters
{
    /// <summary>Подпись маркера состояния карточки детали.</summary>
    public class PartStateTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Part part)
                return string.Empty;

            if (!part.DownTimesIsClosed)
                return "простой";

            if (part.SetupIsNotFinished && part.IsStarted)
                return "наладка";

            if (part.InProduction)
                return "изготовление";

            if (part.IsSynced)
                return "готово";

            return "ожидает";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
