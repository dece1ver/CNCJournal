using remeLog.Models;
using remeLog.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;


namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для LongSetupsWindow.xaml
    /// </summary>
    public partial class LongSetupsWindow : Window
    {
        public LongSetupsWindow(ObservableCollection<Part> parts)
        {
            DataContext = new LongSetupsViewModel(parts);
            InitializeComponent();
        }
    }
}
