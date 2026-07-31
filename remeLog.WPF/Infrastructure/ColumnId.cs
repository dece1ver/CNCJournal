using System.Windows;
using System.Windows.Controls;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Стабильный ID колонки DataGrid — замена DisplayIndex для идентификации
    /// колонки в коде (контекстные меню, обработчики кликов). DataGridColumn не
    /// FrameworkElement и не имеет своего Tag, поэтому используется attached
    /// property. Значение проставляется в PartsInfoWindow.xaml через
    /// inf:ColumnId.Id="..." и должно совпадать с ключом в PartColumnMeta.Map —
    /// единственная точка синхронизации; порядок колонок в
    /// DataGrid.Columns/DisplayIndex значения не имеет.
    /// </summary>
    public static class ColumnId
    {
        public static readonly DependencyProperty IdProperty =
            DependencyProperty.RegisterAttached(
                "Id", typeof(string), typeof(ColumnId), new PropertyMetadata(null));

        public static void SetId(DataGridColumn column, string? value) =>
            column.SetValue(IdProperty, value);

        public static string? GetId(DataGridColumn column) =>
            (string?)column.GetValue(IdProperty);
    }
}
