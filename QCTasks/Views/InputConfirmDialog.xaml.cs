using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QCTasks.Views;

public partial class InputConfirmDialog : DialogBase
{
    private bool _disallowNoOnEmpty;
    public (bool? Confirmed, string Text) Result { get; private set; }

    private InputConfirmDialog()
    {
        InitializeComponent();
        PreviewMouseDown += OnPreviewMouseDown;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            e.Handled = true;
            if (YesButton.IsEnabled)
            {
                YesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
    }

    /// <summary>
    /// Диалог: Yes / No / Cancel + ввод строки.
    /// </summary>
    /// <param name="owner">Родительское окно.</param>
    /// <param name="title">Заголовок.</param>
    /// <param name="message">Основной текст.</param>
    /// <param name="defaultValue">Начальное значение.</param>
    /// <param name="yesText">Текст кнопки Yes.</param>
    /// <param name="noText">Текст кнопки No.</param>
    /// <param name="cancelText">Текст кнопки Cancel.</param>
    /// <param name="detail">Дополнительный текст.</param>
    /// <param name="disallowNoOnEmpty">Запретить No при пустом вводе.</param>
    /// <param name="defaultIsYes">true — дефолт кнопка «Да» (Enter/MMB), false — «Нет». По умолчанию true.</param>
    /// <returns>(bool?, string)</returns>
    public static (bool? Confirmed, string Text) Show(
        Window? owner,
        string title,
        string message,
        string? defaultValue = null,
        string yesText = "Да",
        string noText = "Нет",
        string cancelText = "Отмена",
        string? detail = null,
        bool disallowNoOnEmpty = false,
        bool defaultIsYes = true)
    {
        var dlg = new InputConfirmDialog
        {
            Owner = owner,
            _disallowNoOnEmpty = disallowNoOnEmpty
        };

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;

        dlg.YesButton.Content = yesText;
        dlg.NoButton.Content = noText;
        dlg.CancelButton.Content = cancelText;

        if (defaultIsYes)
        {
            dlg.YesButton.IsDefault = true;
        }
        else
        {
            dlg.NoButton.IsDefault = true;
        }

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
            dlg.UpdateButtonsState();
        };

        dlg.ShowDialog();
        return dlg.Result;
    }

    /// <summary>
    /// Обновляет доступность кнопок в зависимости от ввода.
    /// </summary>
    private void UpdateButtonsState()
    {
        if (!_disallowNoOnEmpty)
            return;

        var isEmpty = string.IsNullOrWhiteSpace(InputTextBox.Text);
        NoButton.IsEnabled = !isEmpty;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Result = (true, InputTextBox.Text);
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Result = (false, InputTextBox.Text);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = (null, InputTextBox.Text);
        Close();
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Yes_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel_Click(sender, e);
        }
    }

    private void InputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateButtonsState();
    }
}