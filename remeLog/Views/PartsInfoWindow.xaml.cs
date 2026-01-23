using DocumentFormat.OpenXml.Spreadsheet;
using libeLog.Infrastructure;
using remeLog.Infrastructure;
using remeLog.Models;
using remeLog.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Database = remeLog.Infrastructure.Database;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для PartsInfoWindow.xaml
    /// </summary>
    public partial class PartsInfoWindow : Window
    {
        enum DataType
        {
            None, Numeric, TimeSpan
        }

        public PartsInfoWindow(CombinedParts parts)
        {
            InitializeComponent();
            DataContext = new PartsInfoWindowViewModel(parts);
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(sender is ComboBox cb)
            {
                cb.Text = "Фильтр по станку";
            }
        }

        private void ValidationTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBlock { Parent: Grid grid }) return;
            foreach (UIElement gridChild in grid.Children)
            {
                if (gridChild is AdornedElementPlaceholder { AdornedElement: TextBlock textBlock } 
                && System.Windows.Controls.Validation.GetErrors(textBlock) is ICollection<ValidationError> { Count: > 0 } errors)
                {
                    MessageBox.Show(errors.First().ErrorContent.ToString(), "Некорректный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void DataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                // Получаем элемент, на который было произведено нажатие
                DependencyObject depObj = (DependencyObject)e.OriginalSource;
                // Находим ячейку, к которой относится событие
                DataGridCell cell = FindVisualParent<DataGridCell>(depObj);
                if (cell != null)
                {
                    // Получаем столбец ячейки
                    DataGridColumn column = cell.Column;
                    // Получаем значение ячейки
                    object value = cell.DataContext;
                    // Обработка события
                    // value содержит значение ячейки
                    // column содержит столбец, к которому относится ячейка
                    if (DataContext is PartsInfoWindowViewModel d && value is Part p)
                    {
                        switch (column.DisplayIndex)
                        {
                            case 2:
                                d.ShiftFilter = d.ShiftFilter.FilterText == p.Shift ? new Shift(Infrastructure.Types.ShiftType.All) : new Shift(p.Shift);
                                break;
                            case 3:
                                d.OperatorFilter = d.OperatorFilter == p.Operator ? "" : p.Operator;
                                break;
                            case 4:
                                d.PartNameFilter = d.PartNameFilter == p.PartName ? "" : p.PartName;
                                break;
                            case 5:
                                d.OrderFilter = d.OrderFilter == p.Order ? "" : p.Order;
                                break;
                            case 9:
                                d.SetupFilter = d.SetupFilter == p.Setup ? null : p.Setup;
                                break;
                            case 44:
                                switch (p.EngineerComment)
                                {
                                    case "":
                                        p.EngineerComment = "Исправлено";
                                        break;
                                    case "Исправлено":
                                        p.EngineerComment = "Исправлено ранее";
                                        break;
                                    case "Исправлено ранее":
                                        p.EngineerComment = "Корректировка не требуется";
                                        break;
                                    case "Корректировка не требуется":
                                        p.EngineerComment = "Ожидание полного изготовления";
                                        break;
                                    case "Ожидание полного изготовления":
                                        p.EngineerComment = "Ожидание решения технолога";
                                        break;
                                    case "Ожидание решения технолога":
                                        p.EngineerComment = "Ожидание решения производства";
                                        break;
                                    case "Ожидание решения производства":
                                        p.EngineerComment = "Временные нормативы";
                                        break;
                                    case "Временные нормативы":
                                        p.EngineerComment = "Изменение технологии";
                                        break;
                                    case "Изменение технологии":
                                        p.EngineerComment = "";
                                        break;
                                }
                                break;
                        }
                    }
                }
            }
            else if (e.RightButton == MouseButtonState.Pressed)
            {
                DependencyObject depObj = (DependencyObject)e.OriginalSource;

                TextBox editingTextBox = FindVisualParent<TextBox>(depObj);
                if (editingTextBox != null)
                {
                    editingTextBox.Focus();
                    e.Handled = true;
                    var extendedTextBoxMenu = (ContextMenu)FindResource("EditingTextBoxContextMenu");

                    extendedTextBoxMenu.PlacementTarget = editingTextBox;
                    extendedTextBoxMenu.IsOpen = true;
                    return;
                }

                DataGridCell cell = FindVisualParent<DataGridCell>(depObj);
                if (cell != null)
                {
                    DataGridColumn column = cell.Column;
                    object value = cell.DataContext;
                    if (DataContext is PartsInfoWindowViewModel d && value is Part p)
                    {
                        switch (column.DisplayIndex)
                        {
                            case 3 or 5:
                                e.Handled = true;
                                cell.Focus();
                                var multiFilterValueContextMenu = (ContextMenu)FindResource("MultiFilterValueContextMenu");
                                multiFilterValueContextMenu.PlacementTarget = cell;
                                multiFilterValueContextMenu.IsOpen = true;
                                break;
                            case 10 or 11 or 12:
                                e.Handled = true;
                                cell.Focus();
                                var timeContextMenu = (ContextMenu)FindResource("TimeContextMenu");
                                timeContextMenu.PlacementTarget = cell;
                                timeContextMenu.IsOpen = true;
                                break;
                            case 41:
                                e.Handled = true;
                                cell.Focus();
                                var masterCommentContextMenu = (ContextMenu)FindResource("MasterCommentCellContextMenu");
                                masterCommentContextMenu.PlacementTarget = cell;
                                masterCommentContextMenu.IsOpen = true;
                                break;
                            case 42 or 43 when p.IsSerial:
                                e.Handled = true;
                                cell.Focus();
                                var serialPartFixedSetupContextMenu = (ContextMenu)FindResource("SerialPartFixedNormativesContextMenu");
                                serialPartFixedSetupContextMenu.PlacementTarget = cell;
                                serialPartFixedSetupContextMenu.IsOpen = true;
                                break;
                            case 44:
                                e.Handled = true;
                                cell.Focus();
                                var engeneerCommentContextMenu = (ContextMenu)FindResource("EngeneerCommentCellContextMenu");
                                engeneerCommentContextMenu.PlacementTarget = cell;
                                engeneerCommentContextMenu.IsOpen = true;
                                break;
                        }
                    }
                }
            }
        }

        private void DataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (sender is DataGrid dataGrid && DataContext is PartsInfoWindowViewModel d)
            {
                var selectedCells = dataGrid.SelectedCells;
                if (selectedCells.Count <= 1)
                {
                    d.Status = string.Empty;
                    return;
                }
                string percent = "";
                double sum = 0;
                TimeSpan timeSpan = TimeSpan.Zero;
                int cnt = 0;
                int cntWithioutZeroes = 0;
                foreach (DataGridCellInfo cell in selectedCells.Where(c => c.IsValid))
                {
                    if (cell.Column.DisplayIndex is 8 or 9 or 10)
                    {
                        d.Status = string.Empty;
                        return;
                    }
                    var content = cell.Column.GetCellContent(cell.Item);
                    if (content is TextBlock textBlock)
                    {
                        var value = textBlock.Text;
                        if (value.EndsWith("%"))
                        {
                            percent = "%";
                            value = value.Replace("%", "");
                        }
                        if (double.TryParse(value, out double num))
                        {
                            sum += num;
                            if (num > 0) cntWithioutZeroes++;
                            cnt++;
                        }
                        else if (TimeSpan.TryParse(textBlock.Text, out TimeSpan span))
                        {
                            timeSpan += span;
                            if (timeSpan.Ticks > 0) cntWithioutZeroes++;
                            cnt++;
                        }
                    }
                }
                if (sum > 0 && cnt > 0 && timeSpan == TimeSpan.Zero)
                {
                    d.Status = $"Среднее: {sum / cnt:0.#}{percent} ({sum / cntWithioutZeroes:0.#}{percent})     Количество: {cnt:0.#}     Сумма: {sum}{percent}";
                }
                else if (timeSpan.Ticks > 0 && cnt > 0 && sum == 0)
                {
                    d.Status = $"Среднее: {TimeSpan.FromTicks(timeSpan.Ticks / cnt):hh\\:mm\\:ss} ({TimeSpan.FromTicks(timeSpan.Ticks / cntWithioutZeroes):hh\\:mm\\:ss})     Количество: {cnt}     Сумма: {timeSpan:hh\\:mm\\:ss}";
                }
                else
                {
                    d.Status = string.Empty;
                }
            }
        }

        private async void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is PartsInfoWindowViewModel d)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && sender is DataGrid dataGrid)
                {
                    switch (e.Key)
                    {
                        case Key.I:
                            var infoCell = dataGrid.SelectedCells.FirstOrDefault();
                            var colIndex = infoCell.Column.DisplayIndex;
                            var infoCellContent = infoCell.Column.GetCellContent(infoCell.Item);
                            var info = infoCellContent is TextBlock tb ? tb.Text : "null";
                            info = $"Выбрано ячеек: {dataGrid.SelectedCells.Count}\n\n" +
                                $"Информация о первой выделенной:\n" +
                                $"Индекс столбца: {colIndex}\n" +
                                $"Тип: {infoCellContent}\n" +
                                $"Содержимое: {info}\n\n" +
                                $"Деталь: {d.SelectedPart?.PartName}";
                            MessageBox.Show(info);
                            e.Handled = true;
                            break;

                        case Key.F:
                            // не работает - надо разобраться
                            break; // временно закрыл
                                   //var baseCell = dataGrid.SelectedCells.FirstOrDefault();
                                   //if (!baseCell.IsValid) return;
                                   //var content = baseCell.Column.GetCellContent(baseCell.Item);
                                   //if (content is TextBlock textBlock)
                                   //{
                                   //    var value = textBlock.Text;
                                   //    foreach (var cell in dataGrid.SelectedCells.Skip(1))
                                   //    {
                                   //        var cellContent = cell.Column.GetCellContent(cell.Item);
                                   //        if (cellContent is TextBlock textBlockToUpdate)
                                   //        {
                                   //            textBlockToUpdate.Text = value;
                                   //        }
                                   //    }
                                   //}
                                   //e.Handled = true;
                                   //break;

                        case Key.V:
                            var pasteCell = dataGrid.SelectedCells.FirstOrDefault();
                            if (pasteCell.Column.DisplayIndex is 21 or 34 or 39 or 42 or 44)
                            {
                                OnPaste();
                            }

                            break;
                        case Key.W:
                            if (d.SelectedPart is Part)
                            {
                                d.SearchInWinnumCommand.Execute(d.SelectedPart);
                                e.Handled = true;
                            }                        
                            break;
                        case Key.Delete:
                            if (d.SelectedPart is Part dp)
                            {
                                d.DeletePartCommand.Execute(dp);
                                e.Handled = true;
                            }
                            break;
                        
                    }
                }
            }
        }

        private void PartsInfoWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // если обрабатывать, то окно кнопки в окне нажимаются не с первого раза
            return;
            if (DataContext is PartsInfoWindowViewModel d)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H || e.Key is Key.Help or Key.F1)
                {
                    var helpWindow = new PartsInfoHelpWindow();
                    helpWindow.ShowDialog();
                }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
                {
                    d.ExportToExcelCommand.Execute(null);

                }
            }
        }

        private void OnVariantClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;

            string newText = item.Header?.ToString() ?? "";

            if (Keyboard.FocusedElement is not DataGridCell cell) return;

            var row = DataGridRow.GetRowContainingElement(cell);
            var itemData = row?.Item;
            if (itemData == null) return;

            string? propertyName = null;

            if (cell.Column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
            {
                propertyName = binding.Path?.Path;
            }
            else
            {
                FrameworkElement? boundElement = FindBoundFrameworkElement<TextBox>(cell) as FrameworkElement;
                boundElement ??= FindBoundFrameworkElement<TextBlock>(cell) as FrameworkElement;

                if (boundElement != null)
                {
                    if (BindingOperations.GetBinding(boundElement, GetDependencyProperty(boundElement)) is Binding innerBinding)
                    {
                        propertyName = innerBinding.Path?.Path;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                var prop = itemData.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(itemData, newText);
                }
            }
        }

        private void OnInsertValueClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            string value = item.Tag?.ToString() ?? item.Header?.ToString() ?? "";

            ContextMenu? contextMenu = FindVisualParent<ContextMenu>(item);
            if (contextMenu?.PlacementTarget is FrameworkElement target)
            {
                switch (target)
                {
                    case TextBox textBox:
                        int caretIndex = textBox.CaretIndex;
                        textBox.Text = textBox.Text.Insert(caretIndex, value);
                        textBox.CaretIndex = caretIndex + value.Length;
                        textBox.Focus();
                        return;

                    case DataGridCell cell:
                        var dataGrid = FindVisualParent<DataGrid>(cell);
                        if (dataGrid == null) return;

                        if (cell.IsEditing)
                        {
                            if (cell.Content is TextBox editor)
                            {
                                int caret = editor.CaretIndex;
                                editor.Text = editor.Text.Insert(caret, value);
                                editor.CaretIndex = caret + value.Length;
                                editor.Focus();
                            }
                        }
                        else
                        {
                            SetSingleCellValue(cell, dataGrid, value);
                        }
                        return;
                }
            }
        }

        private void OnSetValueClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            if (Util.IsNotAppAdmin(() => MessageBox.Show("Нет прав на выполнение операции", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error))) return;
            string value = item.Tag?.ToString() ?? string.Empty;

            if (Keyboard.FocusedElement is DataGridCell cell)
            {
                var dataGrid = FindVisualParent<DataGrid>(cell);
                if (dataGrid == null) return;

                var selectedCells = dataGrid.SelectedCells;

                if (selectedCells.Count > 0)
                {
                    foreach (var selectedCell in selectedCells)
                    {
                        SetCellValue(dataGrid, selectedCell, value);
                    }
                }
                else
                {
                    SetSingleCellValue(cell, dataGrid, value);
                }
            }
        }

        private void OnAddMultiFilterValueClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem) return;
            if (Keyboard.FocusedElement is DataGridCell cell)
            {
                DataGridColumn column = cell.Column;
                object value = cell.DataContext;
                if (DataContext is PartsInfoWindowViewModel d && value is Part p)
                {
                    switch (column.DisplayIndex)
                    {
                        case 3:
                            d.PushValueToEditor("Operator", p.Operator);
                            break;
                        case 5:
                            d.PushValueToEditor("Order", p.Order);
                            break;
                    }
                }
            }
        }

        private void SetCellValue(DataGrid dataGrid, DataGridCellInfo cellInfo, string value)
        {
            var cellContainer = GetDataGridCell(dataGrid, cellInfo);
            if (cellContainer != null)
            {
                SetSingleCellValue(cellContainer, dataGrid, value);
            }
        }

        private void SetSingleCellValue(DataGridCell cell, DataGrid dataGrid, string value)
        {
            TextBox? textBox = FindVisualChild<TextBox>(cell);
            if (textBox != null)
            {
                textBox.Text = value;
                textBox.Focus();
                return;
            }

            dataGrid.CurrentCell = new DataGridCellInfo(cell);
            dataGrid.BeginEdit();

            textBox = FindVisualChild<TextBox>(cell);
            if (textBox != null)
            {
                textBox.Text = value;
                textBox.Focus();
            }
        }

        private DataGridCell? GetDataGridCell(DataGrid dataGrid, DataGridCellInfo cellInfo)
        {
            var rowContainer = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromItem(cellInfo.Item);
            if (rowContainer != null)
            {
                var presenter = FindVisualChild<DataGridCellsPresenter>(rowContainer);
                if (presenter != null)
                {
                    var cell = (DataGridCell)presenter.ItemContainerGenerator.ContainerFromIndex(cellInfo.Column.DisplayIndex);
                    return cell;
                }
            }
            return null;
        }

        private void OnClearVariantClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem) return;

            string newText = "";

            if (Keyboard.FocusedElement is not DataGridCell cell) return;

            var row = DataGridRow.GetRowContainingElement(cell);
            var itemData = row?.Item;
            if (itemData == null) return;

            string? propertyName = null;

            if (cell.Column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
            {
                propertyName = binding.Path?.Path;
            }
            else
            {
                FrameworkElement? boundElement = FindBoundFrameworkElement<TextBox>(cell) as FrameworkElement;
                boundElement ??= FindBoundFrameworkElement<TextBlock>(cell) as FrameworkElement;

                if (boundElement != null)
                {
                    if (BindingOperations.GetBinding(boundElement, GetDependencyProperty(boundElement)) is Binding innerBinding)
                    {
                        propertyName = innerBinding.Path?.Path;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                var prop = itemData.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(itemData, newText);
                }
            }
        }

        private static void OnPaste()
        {
            if (!Clipboard.ContainsText()) return;

            string clipboardText = Clipboard.GetText();

            if (Keyboard.FocusedElement is not DataGridCell cell) return;

            var row = DataGridRow.GetRowContainingElement(cell);
            var itemData = row?.Item;
            if (itemData == null) return;

            string? propertyName = null;

            if (cell.Column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
            {
                propertyName = binding.Path?.Path;
            }
            else
            {
                FrameworkElement? boundElement = FindBoundFrameworkElement<TextBox>(cell) as FrameworkElement;
                boundElement ??= FindBoundFrameworkElement<TextBlock>(cell) as FrameworkElement;

                if (boundElement != null)
                {
                    if (BindingOperations.GetBinding(boundElement, GetDependencyProperty(boundElement)) is Binding innerBinding)
                    {
                        propertyName = innerBinding.Path?.Path;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                var prop = itemData.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(itemData, clipboardText);
                }
            }
        }

        private static T? FindBoundFrameworkElement<T>(DependencyObject parent) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && BindingOperations.IsDataBound(element, GetDependencyProperty(element)))
                {
                    return element;
                }

                var result = FindBoundFrameworkElement<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static DependencyProperty GetDependencyProperty(FrameworkElement element)
        {
            if (element is TextBox)
                return TextBox.TextProperty;
            if (element is TextBlock)
                return TextBlock.TextProperty;
            throw new NotSupportedException($"Неподдерживаемый тип: {element.GetType()}");
        }

        private static T FindVisualParent<T>(DependencyObject obj) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(obj);
            while (parent != null && parent is not T)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return (T)parent!;
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }
    }
}
