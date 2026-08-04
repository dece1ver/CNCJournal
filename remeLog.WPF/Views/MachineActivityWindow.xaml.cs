using System.Windows;
using System.Windows.Controls;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для MachineActivityWindow.xaml
    /// </summary>
    public partial class MachineActivityWindow : Window
    {
        public MachineActivityWindow()
        {
            InitializeComponent();
        }

        /// <summary>Колонка "Оператор" — последняя, растягивается на весь остаток ширины окна.</summary>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (RowsListView.View is not GridView gridView) return;

            double otherColumnsWidth = 0;
            foreach (var column in gridView.Columns)
            {
                if (column != OperatorColumn) otherColumnsWidth += column.Width;
            }

            const double reserve = 40; // отступы ListView + запас под вертикальный скроллбар
            var remaining = RowsListView.ActualWidth - otherColumnsWidth - reserve;
            OperatorColumn.Width = remaining > 150 ? remaining : 150;
        }
    }
}
