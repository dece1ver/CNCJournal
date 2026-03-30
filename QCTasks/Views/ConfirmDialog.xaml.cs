using System.Windows;

namespace QCTasks.Views;

public partial class ConfirmDialog : DialogBase
{
    public bool? Result { get; private set; }

    private ConfirmDialog() => InitializeComponent();

    /// <summary>
    /// Показывает диалог подтверждения.
    /// </summary>
    /// <param name="owner">Родительское окно.</param>
    /// <param name="title">Заголовок в шапке.</param>
    /// <param name="message">Основной текст.</param>
    /// <param name="yesText">Подпись кнопки «Да».</param>
    /// <param name="noText">Подпись кнопки «Нет».</param>
    /// <param name="detail">Дополнительный текст под сообщением (необязательно).</param>
    /// <param name="cancelText">Подпись кнопки «Отмена»; null — кнопка скрыта.</param>
    /// <returns>true — Yes, false — No, null — Cancel / закрыли крестиком.</returns>
    public static bool? Show(
        Window? owner,
        string title,
        string message,
        string yesText = "Да",
        string noText = "Нет",
        string? detail = null,
        string? cancelText = null)
    {
        var dlg = new ConfirmDialog
        {
            Owner = owner
        };

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.YesButton.Content = yesText;
        dlg.NoButton.Content = noText;

        if (detail is not null)
        {
            dlg.DetailText.Text = detail;
            dlg.DetailText.Visibility = Visibility.Visible;
        }

        if (cancelText is not null)
        {
            dlg.CancelButton.Content = cancelText;
            dlg.CancelButton.Visibility = Visibility.Visible;
        }

        dlg.ShowDialog();
        return dlg.Result;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
