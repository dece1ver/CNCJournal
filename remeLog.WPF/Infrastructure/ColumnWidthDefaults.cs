using System.Collections.Generic;
using System.Windows.Controls;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Дефолтные ширины столбцов PartsInfoWindow (ColumnId → px), взятые из
    /// разметки (Width= на DataGridColumn-ресурсах в PartsInfoWindow.xaml).
    /// Захватываются один раз при первом открытии PartsInfoWindow — источник
    /// истины для дефолта, используются как fallback, когда у активного
    /// пользовательского профиля нет своего переопределения (ColumnProfile.ColumnWidths).
    /// </summary>
    public static class ColumnWidthDefaults
    {
        private static Dictionary<string, double>? _Values;
        public static IReadOnlyDictionary<string, double> Values => _Values ??= new();

        public static void CaptureOnce(IEnumerable<string> columnIds, IReadOnlyList<DataGridColumn> columns)
        {
            if (_Values != null) return;
            var values = new Dictionary<string, double>();
            var i = 0;
            foreach (var id in columnIds)
                values[id] = columns[i++].Width.Value;
            _Values = values;
        }

        public static double GetDefault(string columnId) =>
            Values.TryGetValue(columnId, out var width) ? width : 100;
    }
}
