using remeLog.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace remeLog.Views
{
    public partial class MachineInspectionCalendarWindow : Window
    {
        private readonly MachineInspectionCalendarViewModel _vm;
        private bool _suppressSelectionSync;

        internal MachineInspectionCalendarWindow(MachineInspectionCalendarViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            InitializeComponent();
            vm.PropertyChanged += Vm_PropertyChanged;
            vm.Days.CollectionChanged += (_, _) => RebuildColumns();
            CalendarGrid.MouseDoubleClick += CalendarGrid_MouseDoubleClick;
            CalendarGrid.LoadingRow += CalendarGrid_LoadingRow;
            CalendarGrid.SelectionChanged += CalendarGrid_SelectionChanged;
            CalendarGrid.AddHandler(DataGridRowHeader.ClickEvent, new RoutedEventHandler(OnRowHeaderClick), true);
            Closed += (_, _) => _vm.Dispose();
            Loaded += (_, _) => RebuildColumns();
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MachineInspectionCalendarViewModel.FilteredMachines) or nameof(MachineInspectionCalendarViewModel.Days))
            {
                RebuildColumns();
            }
        }

        private void CalendarGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CalendarGrid.CurrentColumn is not DataGridTemplateColumn column) return;
            if (column.Header is not string machine) return;
            if (CalendarGrid.CurrentItem is not MachineInspectionCalendarDayRow row) return;

            var cell = row.Cells.FirstOrDefault(c => c.Machine == machine);
            if (cell == null) return;

            if (_vm.OpenPartsInfoCommand.CanExecute(cell))
            {
                _vm.OpenPartsInfoCommand.Execute(cell);
            }
        }

        private void CalendarGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is MachineInspectionCalendarDayRow row && _vm.IsDateHighlighted(row.Date))
            {
                e.Row.IsSelected = true;
            }
        }

        private void OnRowHeaderClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not DataGridRowHeader header) return;
            if (header.DataContext is not MachineInspectionCalendarDayRow row) return;

            var highlight = !_vm.IsDateHighlighted(row.Date);
            _vm.SetDateHighlighted(row.Date, highlight);
            if (CalendarGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow gridRow)
            {
                gridRow.IsSelected = highlight;
            }
        }

        private void CalendarGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionSync || _vm.IsReloading) return;

            foreach (var item in e.AddedItems)
            {
                if (item is MachineInspectionCalendarDayRow row)
                {
                    _vm.SetDateHighlighted(row.Date, true);
                }
            }

            foreach (var item in e.RemovedItems)
            {
                if (item is MachineInspectionCalendarDayRow row)
                {
                    _vm.SetDateHighlighted(row.Date, false);
                }
            }
        }

        private void RebuildColumns()
        {
            _suppressSelectionSync = true;
            try
            {
                // Статические колонки («Дата», «Проверено») объявлены в XAML
                // (MachineInspectionCalendarWindow.xaml). Здесь управляем только
                // динамическими колонками станков — по одной на выбранный станок.
                for (int i = CalendarGrid.Columns.Count - 1; i >= 0; i--)
                {
                    if (CalendarGrid.Columns[i] is DataGridTemplateColumn)
                        CalendarGrid.Columns.RemoveAt(i);
                }

                var template = (DataTemplate)FindResource("MachineCellTemplate");
                // вставляем перед последней статической колонкой («Проверено»)
                int index = Math.Max(0, CalendarGrid.Columns.Count - 1);
                foreach (var machine in _vm.FilteredMachines)
                {
                    CalendarGrid.Columns.Insert(index++, new DataGridTemplateColumn
                    {
                        Header = machine,
                        Width = new DataGridLength(50),
                        CellTemplate = template
                    });
                }
            }
            finally
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _suppressSelectionSync = false));
            }
        }
    }
}
