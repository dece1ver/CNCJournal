using remeLog.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace remeLog.Views
{
    public partial class MachineInspectionCalendarWindow : Window
    {
        private readonly MachineInspectionCalendarViewModel _vm;

        internal MachineInspectionCalendarWindow(MachineInspectionCalendarViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            InitializeComponent();
            vm.PropertyChanged += Vm_PropertyChanged;
            vm.Days.CollectionChanged += (_, _) => RebuildColumns();
            CalendarGrid.MouseDoubleClick += CalendarGrid_MouseDoubleClick;
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

        private void RebuildColumns()
        {
            CalendarGrid.Columns.Clear();

            var dateColumn = new DataGridTextColumn
            {
                Header = "Дата",
                Binding = new Binding("DateDisplay"),
                Width = new DataGridLength(100),
                ElementStyle = (Style)FindResource("DateCellStyle")
            };
            CalendarGrid.Columns.Add(dateColumn);

            var cellConverter = (IValueConverter)FindResource("MachineInspectionCalendarCellConverter");
            var iconMultiConverter = (IMultiValueConverter)FindResource("MachineInspectionCalendarCellIconMultiConverter");

            foreach (var machine in _vm.FilteredMachines)
            {
                var column = new DataGridTemplateColumn
                {
                    Header = machine,
                    Width = new DataGridLength(50),
                    CellTemplate = CreateMachineCellTemplate(machine, cellConverter, iconMultiConverter)
                };
                CalendarGrid.Columns.Add(column);
            }
        }

        private static DataTemplate CreateMachineCellTemplate(string machine, IValueConverter cellConverter, IMultiValueConverter iconMultiConverter)
        {
            var cellBinding = new Binding
            {
                Path = new PropertyPath("."),
                ConverterParameter = machine,
                Converter = cellConverter
            };

            var iconMultiBinding = new MultiBinding { Converter = iconMultiConverter };
            iconMultiBinding.Bindings.Add(cellBinding);

            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(ContentControl));
            factory.SetValue(ContentControl.WidthProperty, 16.0);
            factory.SetValue(ContentControl.HeightProperty, 16.0);
            factory.SetValue(ContentControl.MarginProperty, new Thickness(0, 1, 0, 1));
            factory.SetValue(ContentControl.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetValue(ContentControl.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.SetBinding(ContentControl.ContentProperty, iconMultiBinding);
            template.VisualTree = factory;
            template.Seal();
            return template;
        }
    }
}
