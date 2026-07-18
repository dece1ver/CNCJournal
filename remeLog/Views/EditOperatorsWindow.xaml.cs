using remeLog.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using libeLog.Views;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для EditOperatorsWindow.xaml
    /// </summary>
    public partial class EditOperatorsWindow : Window
    {
        public EditOperatorsWindow()
        {
            InitializeComponent();
        }

        private void EmployeesDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (sender is DataGrid grid && grid.SelectedItem is OperatorInfo operatorInfo)
                {
                    if (MessageBoxWindow.Show($"Вы уверены, что хотите удалить оператора: \"{operatorInfo.FullName}\"?\nЭто действие необратимо.", "Подтверждение удаления",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxDefaultButton.No) != MessageBoxResult.Yes) 
                    { 
                        e.Handled = true; 
                        return; 
                    }
                }
            }
        }
    }
}
