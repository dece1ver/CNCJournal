using remeLog.Infrastructure.Types;
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
using libeLog.Views;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для SetRoleDialog.xaml
    /// </summary>
    public partial class SetRoleDialog : Window
    {
        public SetRoleDialog()
        {
            InitializeComponent();
        }

        public User SelectedRole { get; private set; }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (!MasterRadioButton.IsChecked == true && !TechnologistRadioButton.IsChecked == true)
            {
                MessageBoxWindow.Show("Пожалуйста, выберите роль!", "Предупреждение",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedRole = MasterRadioButton.IsChecked == true ? User.Master : User.Engineer;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
