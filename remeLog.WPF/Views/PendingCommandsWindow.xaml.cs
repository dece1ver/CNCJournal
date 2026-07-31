using System.Windows;
using remeLog.ViewModels;

namespace remeLog.Views
{
    public partial class PendingCommandsWindow : Window
    {
        public PendingCommandsWindow()
        {
            InitializeComponent();
            Closed += (_, _) => ((PendingCommandsViewModel)DataContext).Dispose();
        }
    }
}
