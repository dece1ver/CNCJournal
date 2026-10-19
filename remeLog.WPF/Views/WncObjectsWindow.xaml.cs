using remeLog.Models;
using remeLog.ViewModels;
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

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для WncObjectsWindow.xaml
    /// </summary>
    public partial class WncObjectsWindow : Window
    {
        public WncObjectsWindow(ObservableCollection<WncObject> wncObjects, string? initialKeyword = null)
        {
            this.DataContext = new WncObjectsViewModel(wncObjects, initialKeyword);
            InitializeComponent();
        }
    }
}
