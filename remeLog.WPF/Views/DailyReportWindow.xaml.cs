using remeLog.Models;
using remeLog.ViewModels;
using System;
using System.Collections.Generic;
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

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для DailyReportWindow.xaml
    /// </summary>
    public partial class DailyReportWindow : Window
    {
        public DailyReportWindow((ICollection<Part> parts, DateTime date, string machine) shiftInfo)
        {
            DataContext = new DailyReportWindowViewModel(shiftInfo);
            InitializeComponent();
        }
    }
}
