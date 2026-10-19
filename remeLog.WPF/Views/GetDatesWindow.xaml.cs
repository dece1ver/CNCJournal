using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using remeLog.ViewModels;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для GetDatesWindow.xaml
    /// </summary>
    public partial class GetDatesWindow : Window
    {
        public ObservableCollection<DateTime> Dates { get; set; } = new();

        public GetDatesWindow(DateTime dateTime)
        {
            InitializeComponent();
            DataContext = new GetDatesWindowViewModel(dateTime);
        }

        private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var listView = sender as ListView;
            if (listView?.View is GridView gridView && gridView.Columns.Count > 0)
            {
                double otherColumnsWidth = gridView.Columns.Take(gridView.Columns.Count - 1)
                                                          .Sum(c => c.ActualWidth);
                gridView.Columns.Last().Width = Math.Max(0, listView.ActualWidth - otherColumnsWidth - 10);
            }
        }
    }
}
