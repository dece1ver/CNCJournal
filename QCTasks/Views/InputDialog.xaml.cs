using System.Windows;
using System.Windows.Input;

namespace QCTasks.Views;

public partial class InputDialog : DialogBase
{
    public string? Result { get; private set; }

    private InputDialog() => InitializeComponent();

    /// <summary>
    /// Показывает диалог ввода строки.
    /// </summary>
    /// <param name="owner">Родительское окно.</param>
    /// <param name="title">Заголовок в шапке.</param>
    /// <param name="message">Основной текст с пояснением.</param>
    /// <param name="defaultValue">Начальное значение в поле ввода.</param>
    /// <param name="okText">Подпись кнопки подтверждения.</param>
    /// <param name="cancelText">Подпись кнопки отмены.</param>
    /// <param name="detail">Дополнительный текст под сообщением (необязательно).</param>
    /// <returns>Введённая строка; null — отмена или закрытие окна.</returns>
    public static string? Show(
        Window? owner,
        string title,
        string message,
        string? defaultValue = null,
        string okText = "OK",
        string cancelText = "Отмена",
        string? detail = null)
    {
        var dlg = new InputDialog
        {
            Owner = owner
        };

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkButton.Content = okText;
        dlg.CancelButton.Content = cancelText;

        if (detail is not null)
        {
            dlg.DetailText.Text = detail;
            dlg.DetailText.Visibility = Visibility.Visible;
        }

        if (defaultValue is not null)
        {
            dlg.InputTextBox.Text = defaultValue;
        }

        dlg.Loaded += (_, _) =>
        {
            dlg.InputTextBox.Focus();
            dlg.InputTextBox.SelectAll();
        };

        dlg.ShowDialog();
        return dlg.Result;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = InputTextBox.Text;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Ok_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel_Click(sender, e);
        }
    }
}