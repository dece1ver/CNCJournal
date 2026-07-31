using remeLog.Infrastructure.Winnum.Data;
using remeLog.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для WinnumInfoWindow.xaml
    /// </summary>
    public partial class WinnumInfoWindow : Window
    {
        public WinnumInfoWindow(string generalInfo, string ncProgramFolder, List<PriorityTagDuration> priorityTagDurations, List<TimeInterval> timeIntervals, DataTable? timeline = null, string? winnumPageUrl = null)
        {
            DataContext = new WinnumInfoViewModel(generalInfo, ncProgramFolder, priorityTagDurations, timeIntervals, timeline, winnumPageUrl);
            InitializeComponent();

            if (winnumPageUrl != null)
            {
                var browser = new WebBrowser();
                browser.Source = new Uri(winnumPageUrl);
                BrowserContainer.Children.Add(browser);
            }
        }
    }
}
