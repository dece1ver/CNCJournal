using eLog.Infrastructure;
using eLog.Models;
using eLog.ViewModels;
using libeLog.Base;
using libeLog.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace eLog.Views.Windows.Settings
{


    public partial class OperatorsEditWindow : INotifyPropertyChanged
    {


        private ObservableCollection<Operator> _Operators = new ();
        /// <summary> Операторы </summary>
        public ObservableCollection<Operator> Operators
        {
            get => _Operators;
            set => Set(ref _Operators, value);
        }

        private ObservableCollection<Operator> _TotalOperators = new();
        /// <summary> Все операторы </summary>
        public ObservableCollection<Operator> TotalOperators
        {
            get => _TotalOperators;
            set => Set(ref _TotalOperators, value);
        }

        public Operator SelectedLeftOperator { get; set; }
        public Operator SelectedRightOperator { get; set; }


        private string _Status = "";
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }


        private bool _InProgress;
        /// <summary> Работа про процессе </summary>
        public bool InProgress
        {
            get => _InProgress;
            set => Set(ref _InProgress, value);
        }


        public OperatorsEditWindow()
        {
            var tempOperators =

            Operators = new ObservableCollection<Operator>(AppSettings.Instance.Operators.Select(op => new Operator(op)));
            Task.Run(async () =>
            {
                InProgress = true;
                IProgress<string> progress = new Progress<string>(p => Status = p);
                TotalOperators = await Database.GetOperatorsAsync(progress);
                InProgress = false;
                Status = "";
            });
            InitializeComponent();
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var grid = (DataGrid)sender;
            if (Key.Delete != e.Key) return;
            if (MessageBox.Show(
                    $"Удалить оператора?",
                    "Подтверждение!",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                == MessageBoxResult.Yes
                && grid.SelectedItem is Operator @operator)
            {
                Operators.Remove(@operator);
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

        private async void Add_Button_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedLeftOperator == null)
            {
                Status = "Ничего не выбрано.";
            }
            else
            {
                if (Operators.Any(op =>
                    op.FullName == SelectedLeftOperator.FullName))
                {
                    Status = $"Оператор [{SelectedLeftOperator.DisplayName}] уже в списке.";
                }
                else
                {
                    Operators.Add(SelectedLeftOperator);
                    Status = $"Оператор [{SelectedLeftOperator.DisplayName}] добавлен.";
                }
            }

            await Task.Delay(3000);
            Status = "";
        }

        private async void Remove_Button_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRightOperator == null)
            {
                Status = "Ничего не выбрано для удаления.";
            }
            else
            {
                try
                {
                    var @operator = SelectedRightOperator.DisplayName;
                    bool removed = Operators.Remove(SelectedRightOperator);
                    if (removed)
                    {
                        Status = $"Оператор [{@operator}] успешно удален из списка.";
                    }
                    else
                    {
                        Status = $"Не удалось удалить оператора [{@operator}].";
                    }
                }
                catch (Exception ex)
                {
                    Status = $"Ошибка при удалении: {ex.Message}";
                }
            }

            await Task.Delay(3000);
            Status = "";
        }
    }
}