using System;
using System.Windows;
using remeLog.ViewModels;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для PeriodsComparisonWindow.xaml
    /// </summary>
    public partial class PeriodsComparisonWindow : Window
    {
        public DateTime FromDate1 { get; set; }
        public DateTime ToDate1 { get; set; }
        public string Label1 { get; set; } = "";
        public DateTime FromDate2 { get; set; }
        public DateTime ToDate2 { get; set; }
        public string Label2 { get; set; } = "";

        public PeriodsComparisonWindow(DateTime defaultFromDate, DateTime defaultToDate)
        {
            InitializeComponent();
            DataContext = new PeriodsComparisonWindowViewModel(defaultFromDate, defaultToDate);
        }
    }
}
