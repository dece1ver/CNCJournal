using eLog.Infrastructure;
using eLog.Models;
using eLog.ViewModels;
using libeLog.Models;
using libeLog.Views;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace eLog.Views.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer _serialTimer;

        public MainWindow()
        {
            InitializeComponent();
            Closing += OnWindowClosing!;
            _serialTimer = new DispatcherTimer();
        }

        public void OnWindowClosing(object sender, CancelEventArgs e)
        {
            AppSettings.Save();
            switch (AppSettings.Instance.IsShiftStarted)
            {
                case false:
                    return;
                case true when AppSettings.Instance.Parts.Count == AppSettings.Instance.Parts.Count(x => x.IsFinished is not Part.State.InProgress):
                    {
                        var res = MessageBoxWindow.Show("Смена не завершена.", "Внимание!", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxDefaultButton.Cancel);
                        switch (res)
                        {
                            case MessageBoxResult.OK:
                                break;
                            case MessageBoxResult.Cancel or _:
                                e.Cancel = true;
                                break;
                        }
                        break;
                    }
                case true when AppSettings.Instance.Parts.Count != AppSettings.Instance.Parts.Count(x => x.IsFinished is not Part.State.InProgress):
                    {
                        var res = MessageBoxWindow.Show("Есть незавершенные детали.", "Внимание!", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxDefaultButton.Cancel);
                        switch (res)
                        {
                            case MessageBoxResult.OK:
                                break;
                            case MessageBoxResult.Cancel or _:
                                e.Cancel = true;
                                break;
                        }
                        break;
                    }
            }
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serialTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _serialTimer.Tick += SerialTimer_Tick!;
            _serialTimer.Start();
        }

        private void SerialTimer_Tick(object sender, EventArgs e)
        {
            var listView = FindVisualChild<ListView>(this);
            if (listView != null)
            {
                for (int i = 0; i < listView.Items.Count; i++)
                {
                    var container = listView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                    if (container != null)
                    {
                        var part = container.DataContext as Part;
                        if (part?.IsSerial == true)
                        {
                            var image = FindVisualChild<Image>(container, "SerialBadge");
                            if (image != null)
                            {
                                var storyboard = (Storyboard)Resources["SerialShowStoryboard"];
                                Storyboard sb = storyboard.Clone();
                                sb.Begin(image);
                            }
                        }
                    }
                }
            }
        }

        private T FindVisualChild<T>(DependencyObject parent, string name = null) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && (name == null || (child as FrameworkElement)?.Name == name))
                    return t;

                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}