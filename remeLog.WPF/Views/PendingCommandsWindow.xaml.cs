using System.Windows;
using remeLog.ViewModels;

namespace remeLog.Views
{
    public partial class PendingCommandsWindow : Window
    {
        /// <param name="application">
        /// Показывать очередь только этого приложения (remeLog.Core.AppNames); null — всю.
        /// </param>
        /// <param name="applicationTitle">Название приложения для заголовка окна.</param>
        public PendingCommandsWindow(string? application = null, string? applicationTitle = null)
        {
            InitializeComponent();
            DataContext = new PendingCommandsViewModel(application);

            if (!string.IsNullOrEmpty(applicationTitle))
                Title = $"Очередь задач — {applicationTitle}";

            Closed += (_, _) => ((PendingCommandsViewModel)DataContext).Dispose();
        }
    }
}
