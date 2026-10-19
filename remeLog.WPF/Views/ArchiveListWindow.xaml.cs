using libeLog.Extensions;
using remeLog.Models;
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
    /// Логика взаимодействия для ArchiveListWindow.xaml
    /// </summary>
    public partial class ArchiveListWindow : Window
    {
        public static readonly DependencyProperty PartsProperty =
        DependencyProperty.Register(nameof(Parts), typeof(List<Part>), typeof(ArchiveListWindow), new PropertyMetadata(null));

        public List<Part> Parts
        {
            get { return (List<Part>)GetValue(PartsProperty); }
            set { SetValue(PartsProperty, value); }
        }
        public ArchiveListWindow(List<Part> parts)
        {
            Parts = parts;
            InitializeComponent();
        }
    }
}
