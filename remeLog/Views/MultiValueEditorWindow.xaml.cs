using libeLog.Base;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace remeLog.Views
{
    public partial class MultiValueEditorWindow : Window
    {
        public ObservableCollection<ValueItem> Values { get; set; }
        public string ResultString { get; private set; } = "";
        public bool Resilt { get; private set; }

        public MultiValueEditorWindow(string fieldName, string currentValue)
        {
            InitializeComponent();
            DataContext = this;

            FieldNameText.Text = fieldName;

            Values = new ObservableCollection<ValueItem>(
                ParseValues(currentValue)
                    .Select(s => new ValueItem { Value = s })
            );

            Values.CollectionChanged += (_, _) => RefreshEmptyState();
            RefreshEmptyState();

            Loaded += (_, _) => InputBox.Focus();
        }

        // Добавление значения

        private void AddValue(string raw)
        {
            // Поддержка вставки нескольких значений через ; или перенос строки
            var parts = raw
                .Split(new[] { ';', '\n', '\r' },
                       StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));

            bool added = false;
            foreach (var val in parts)
            {
                if (!Values.Any(v =>
                        string.Equals(v.Value, val, StringComparison.OrdinalIgnoreCase)))
                {
                    Values.Add(new ValueItem { Value = val });
                    added = true;
                }
            }

            if (added)
            {
                InputBox.Clear();
                ChipsScroll.ScrollToBottom();
            }
        }

        // Обработчики ввода

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(InputBox.Text))
            {
                AddValue(InputBox.Text);
                e.Handled = true;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(InputBox.Text))
                AddValue(InputBox.Text);
            InputBox.Focus();
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Placeholder.Visibility = string.IsNullOrEmpty(InputBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Обработчики чипов

        private void ChipRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ValueItem item })
                Values.Remove(item);
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            Values.Clear();
            InputBox.Focus();
        }

        // Подтверждение / отмена

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Если в поле ввода что-то есть — добавляем не требуя Enter
            if (!string.IsNullOrWhiteSpace(InputBox.Text))
                AddValue(InputBox.Text);

            ResultString = string.Join(";",
                Values
                    .Where(v => !string.IsNullOrWhiteSpace(v.Value))
                    .Select(v => v.Value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));

            Resilt = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Resilt = false;
            Close();
        }

        // Вспомогательные

        private void RefreshEmptyState()
        {
            bool isEmpty = Values.Count == 0;
            EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            ChipsScroll.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = !isEmpty;
        }

        private static string[] ParseValues(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }
    }

    public class ValueItem : ViewModel
    {
        private string _value = "";
        public string Value
        {
            get => _value;
            set => Set(ref _value, value);
        }
    }
}