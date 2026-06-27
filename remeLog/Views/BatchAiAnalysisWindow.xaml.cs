using remeLog.ViewModels;
using System.Windows;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для BatchAiAnalysisWindow.xaml
    /// </summary>
    public partial class BatchAiAnalysisWindow : Window
    {
        public BatchAiAnalysisWindow()
        {
            DataContext = new BatchAiAnalysisViewModel();
            InitializeComponent();
        }
    }
}
