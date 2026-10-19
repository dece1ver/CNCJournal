using System.Windows;
using System.Windows.Input;

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
