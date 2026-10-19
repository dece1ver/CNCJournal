using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows;
using System;
using remeLog;
using remeLog.Infrastructure;
using libeLog.Extensions;

namespace remeLog.Views
{
    public partial class PartSelectionFilterWindow : Window
    {
        private static class Props
        {
            public static readonly DependencyProperty RunCountFilter = DependencyProperty.Register(
                nameof(PartSelectionFilterWindow.RunCountFilter),
                typeof(string),
                typeof(PartSelectionFilterWindow),
                new PropertyMetadata(string.Empty, OnRunCountFilterChanged));

            public static readonly DependencyProperty IsInputValid = DependencyProperty.Register(
                nameof(PartSelectionFilterWindow.IsInputValid),
                typeof(bool),
                typeof(PartSelectionFilterWindow),
                new PropertyMetadata(false));

            public static readonly DependencyProperty AddSheetPerMachine = DependencyProperty.Register(
                nameof(PartSelectionFilterWindow.AddSheetPerMachine),
                typeof(bool),
                typeof(PartSelectionFilterWindow),
                new PropertyMetadata(false));

            public static readonly DependencyProperty Status = DependencyProperty.Register(
                nameof(PartSelectionFilterWindow.Status),
                typeof(string),
                typeof(PartSelectionFilterWindow),
                new PropertyMetadata(string.Empty));
        }

        public string RunCountFilter
        {
            get => (string)GetValue(Props.RunCountFilter);
            set => SetValue(Props.RunCountFilter, value);
        }

        public bool IsInputValid
        {
            get => (bool)GetValue(Props.IsInputValid);
            private set => SetValue(Props.IsInputValid, value);
        }

        public bool AddSheetPerMachine
        {
            get => (bool)GetValue(Props.AddSheetPerMachine);
            private set => SetValue(Props.AddSheetPerMachine, value);
        }

        public string Status
        {
            get => (string)GetValue(Props.Status);
            private set => SetValue(Props.Status, value);
        }

        private readonly DebounceDispatcher _debouncer = new(TimeSpan.FromMilliseconds(300));

        public PartSelectionFilterWindow(string initialValue, bool addSheetPerMachine)
        {
            InitializeComponent();
            DataContext = this;
            RunCountFilter = initialValue;
            AddSheetPerMachine = addSheetPerMachine;
            ValidateInput();
        }

        private static void OnRunCountFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PartSelectionFilterWindow dialog)
            {
                dialog._debouncer.Debounce(() => dialog.ValidateInput());
            }
        }

        private void ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(RunCountFilter))
            {
                UpdateValidationStatus(true, "Будут выбраны все изготовления");
                return;
            }

            if (Util.TryParseComparison(RunCountFilter, out var op, out var value))
            {
                UpdateValidationStatus(
                    value > 0,
                    value > 0 ? $"Выбрано: {Util.GetOperatorSymbol(op)} {value.FormattedLaunches(Util.ShouldUseGenitive(op))}" : string.Empty
                );
                return;
            }

            UpdateValidationStatus(
                int.TryParse(RunCountFilter, out var simpleValue) && simpleValue > 0,
                string.Empty
            );
        }

        private void UpdateValidationStatus(bool isValid, string status)
        {
            IsInputValid = isValid;
            Status = status;
        }

        private void RunCountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Разрешаем ввод операторов сравнения
            e.Handled = !(int.TryParse(e.Text, out _) || IsComparisonOperator(e.Text));
        }

        private static bool IsComparisonOperator(string text) =>
            text is ">" or "<" or "=" or "≥" or "≤";

        private void OnPasteHandler(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(typeof(string)))
            {
                e.CancelCommand();
                return;
            }

            var text = (string)e.DataObject.GetData(typeof(string));
            if (!text.All(c => char.IsDigit(c) || IsComparisonOperator(c.ToString())))
            {
                e.CancelCommand();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }

    // Вспомогательный класс для дебаунсинга
    public class DebounceDispatcher
    {
        private readonly TimeSpan _interval;
        private System.Threading.Timer? _timer;

        public DebounceDispatcher(TimeSpan interval)
        {
            _interval = interval;
        }

        public void Debounce(Action action)
        {
            _timer?.Dispose();
            _timer = new System.Threading.Timer(
                _ =>
                {
                    App.Current.Dispatcher.Invoke(action);
                    _timer?.Dispose();
                },
                null,
                _interval,
                Timeout.InfiniteTimeSpan
            );
        }
    }
}