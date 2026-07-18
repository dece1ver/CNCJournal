using eLog.Models;
using eLog.Services;
using libeLog.Interfaces;
using libeLog.Models;
using libeLog.Views;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using static eLog.Infrastructure.Text;

namespace eLog.Views.Windows.Dialogs
{
    /// <summary>
    /// Логика взаимодействия для EditDownTimesDialogWindow.xaml
    /// </summary>
    public partial class EditDownTimesDialogWindow : Window, INotifyPropertyChanged, IOverlay
    {
        private Part _Part;
        private string _Status;
        private Overlay _Overlay = new() { State = false };
        private bool _CanBeClosed;
        private bool _CanAddDownTime;

        public Part Part
        {
            get => _Part;
            set => Set(ref _Part, value);
        }

        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }
        private void DeleteDownTimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: DownTime downTime })
            {
                Part.DownTimes.Remove(downTime);
                OnPropertyChanged(nameof(Part.DownTimesIsClosed));
                Validate();
            }
        }

        private void AddDownTimeButton_Click(object sender, RoutedEventArgs e)
        {
            using (Overlay = new())
            {
                var (downTimeType, toolType, comment) = WindowsUserDialogService.SetDownTimeType(this);
                if (downTimeType is { } type)
                {
                    if (type is DownTime.Types.CreateNcProgram && Part is { SetupIsFinished: true })
                    {
                        MessageBoxWindow.Show($"{DownTimes.CreateNcProgram} может быть только в наладке.", "Молодой человек, это не для вас простой.", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Part.DownTimes.Add(new DownTime(Part, type) { ToolType = toolType, Comment = comment});
                }
                CanAddDownTime = Part.DownTimesIsClosed && CanBeClosed;
            }
            Validate();
        }

        public EditDownTimesDialogWindow(Part part)
        {
            _Part = part;
            _Status = string.Empty;
            InitializeComponent();
        }

        public bool CanBeClosed
        {
            get => _CanBeClosed;
            set => Set(ref _CanBeClosed, value);
        }

        public bool CanAddDownTime
        {
            get => _CanAddDownTime;
            set => Set(ref _CanAddDownTime, value);
        }

        #region PropertyChanged

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
                {
                    dispatcherObject.Dispatcher.Invoke(action, this, args);
                }
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

        #endregion

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Validate();
            CanAddDownTime = Part.DownTimesIsClosed && CanBeClosed;
        }

        public Overlay Overlay
        {
            get => _Overlay;
            set => Set(ref _Overlay, value);
        }

        private void TimeTextBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e) => Validate();

        private void Validate()
        {
            var result = true;
            foreach (var downTime in Part.DownTimes)
            {
                downTime.UpdateError();
                if (downTime.HasError) result = false;
            }
            CanBeClosed = result;
            CanAddDownTime = Part.DownTimesIsClosed && CanBeClosed;
            OnPropertyChanged(nameof(Part.DownTimes));
        }

        private void Border_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border { Parent: StackPanel { Parent: Grid } grid }) return;
            foreach (UIElement gridChild in grid.Children)
            {
                if (gridChild is AdornedElementPlaceholder { AdornedElement: TextBox textBox } && Validation.GetErrors(textBox) is ICollection<ValidationError> { Count: > 0 } errors)
                {
                    MessageBoxWindow.Show(errors.First().ErrorContent.ToString(), "Некорректный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var dt in Part.DownTimes.Where(d => d.Type == DownTime.Types.ToolSearching && d.IsSuccess == null && d.EndTime > d.StartTime))
            {
                dt.IsSuccess = MessageBoxWindow.Show($"Удалось ли найти инструмент?\n[{dt.ToolType}] {dt.Comment}\nс {dt.StartTimeText} по {dt.EndTimeText}", "Уточните", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.Yes) == MessageBoxResult.Yes;
            }
            DialogResult = true;
            Close();
        }
    }
}