using System.ComponentModel;
using System.Windows;
using UserEnvVar = libeLog.Infrastructure.UserEnvVar;

namespace remeLog.Views
{
    /// <summary>
    /// Ввод переменной окружения для команды SetUserEnvVar: два поля, имя и значение.
    /// Имя проверяется по маске живьём (поле подсвечивается, кнопка ОК недоступна);
    /// значение произвольное. Получатель (eLog) проверит всё ещё раз перед применением.
    /// </summary>
    public partial class EnvVarDialogWindow : Window, IDataErrorInfo
    {
        public static readonly DependencyProperty VariableNameProperty =
            DependencyProperty.Register(nameof(VariableName), typeof(string), typeof(EnvVarDialogWindow), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty VariableValueProperty =
            DependencyProperty.Register(nameof(VariableValue), typeof(string), typeof(EnvVarDialogWindow), new PropertyMetadata(string.Empty));

        public string VariableName
        {
            get => (string)GetValue(VariableNameProperty);
            set => SetValue(VariableNameProperty, value);
        }

        public string VariableValue
        {
            get => (string)GetValue(VariableValueProperty);
            set => SetValue(VariableValueProperty, value);
        }

        // Живая проверка имени для биндинга (ValidatesOnDataErrors в XAML):
        // null — поле валидно, текст — подсветить поле и показать в подсказке.
        string IDataErrorInfo.Error => string.Empty;

        string IDataErrorInfo.this[string columnName]
        {
            get
            {
                if (columnName == nameof(VariableName)
                    && !UserEnvVar.IsValidName(VariableName?.Trim()))
                    return "Имя должно начинаться с буквы или _, далее — буквы, цифры, _.";
                return null!;
            }
        }

        public EnvVarDialogWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NameBox.Focus();
        }
    }
}
