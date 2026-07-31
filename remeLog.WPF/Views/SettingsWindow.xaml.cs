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
    /// Логика взаимодействия для SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        int _connectionStringEnablerCounter;
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void ConnectionStringTextBlock_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _connectionStringEnablerCounter++;
            if (_connectionStringEnablerCounter >= 5 )
            {
                ConnectionStringTextBox.IsEnabled = true;
            }
        }
    }
}
