using eLog.ViewModels;
using libeLog.Models;
using libeLog.Views;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace eLog.Views.Windows.Dialogs
{
    /// <summary>
    /// Логика взаимодействия для TasksForProductionWindow.xaml
    /// </summary>
    public partial class TasksForProductionWindow : Window, INotifyPropertyChanged
    {
        public TasksForProductionWindow(IReadOnlyList<ProductionTaskData> tasks)
        {
            DataContext = this;
            Tasks = tasks;
            _Status = "";
            InitializeComponent();
        }

        private ProductionTaskData? _SelectedTask;

        public ProductionTaskData? SelectedTask
        {
            get { return _SelectedTask; }
            set
            {
                _SelectedTask = value;
                foreach (var task in Tasks)
                {
                    task.IsSelected = false;
                }
                if (SelectedTask is { } st) { st.IsSelected = true; }
            }
        }


        private string _Status;
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }


        public bool NeedStart { get; set; }


        public IReadOnlyList<ProductionTaskData> Tasks { get; set; }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (App.Current.MainWindow.DataContext is MainWindowViewModel cxt && cxt.CanAddPart)
            {
                if (MessageBoxWindow.Show($"Запустить изготовление {SelectedTask?.PartName} по М/Л {SelectedTask?.Order}?",
                    "Запуск детали",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxDefaultButton.Yes) == MessageBoxResult.Yes)
                {
                    NeedStart = true;
                    Close();
                }
            }
            else
            {
                MessageBoxWindow.Show($"В данный момент нельзя запустить изготовление данной детали т.к. уже запущена другая деталь, либо не начата смена", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (SelectedTask is { } st && !string.IsNullOrEmpty(st.NcProgramHref) && st.NcProgramHref != "-")
            {
                Process.Start(new ProcessStartInfo($"dinfo://{st.NcProgramHref.Trim('\\')}") { UseShellExecute = true});
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string PropertyName = null!)
        {
            var handlers = PropertyChanged;
            if (handlers is null) return;

            var invokationList = handlers.GetInvocationList();
            var args = new PropertyChangedEventArgs(PropertyName);

            foreach (var action in invokationList)
            {
                if (action.Target is DispatcherObject dispatcherObject)
                    dispatcherObject.Dispatcher.Invoke(action, this, args);
                else
                {
                    action.DynamicInvoke(this, args);
                }
            }
        }

        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string PropertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(PropertyName);
            return true;
        }

    }
}
