using libeLog.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace remeLog.Views
{
    public partial class MultiValueEditorWindow : Window
    {
        public ObservableCollection<ValueItem> Values { get; set; }
        public string ResultString { get; private set; } = "";
        public bool Resilt { get; set; }
        public MultiValueEditorWindow(string fieldName, string currentValue)
        {
            InitializeComponent();
            DataContext = this;
            Title = $"Выбор значений: {fieldName}";

            Values = new ObservableCollection<ValueItem>(
                ParseMultiValue(currentValue)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new ValueItem { Value = s })
            );
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ResultString = string.Join("; ",
                Values.Where(v => !string.IsNullOrWhiteSpace(v.Value))
                      .Select(v => v.Value.Trim())
            );
            Resilt = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Resilt = false;
            Close();
        }

        private static string[] ParseMultiValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
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
