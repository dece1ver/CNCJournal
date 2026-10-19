using remeLog.Infrastructure;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для AutoCloseHistoryWindow.xaml
    /// </summary>
    public partial class AutoCloseHistoryWindow : Window
    {
        public AutoCloseHistoryWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                var rows = await Database.GetAutoCloseHistoryAsync();
                HistoryGrid.ItemsSource = rows;
                StatusLine.Text = rows.Count == 0
                    ? "Автозакрытий пока нет"
                    : $"Записей: {rows.Count}";
            }
            catch (Exception ex)
            {
                StatusLine.Text = $"Ошибка загрузки: {ex.Message}";
            }
        }
    }
}
