using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace libeLog.Views;

/// <summary> Порядок кнопок Да/Нет — какая из них идёт первой (левее). </summary>
public enum YesNoButtonOrder
{
    YesFirst,
    NoFirst,
}

public partial class MessageBoxWindow : Window
{
    private MessageBoxResult _result;
    private Button _defaultButton = null!;
    private bool _closedByButton;

    // Результат, соответствующий каждой физической кнопке — заполняется в SetupButtons.
    // Нужно отдельно от GetCurrentButtons(), потому что для Да/Нет порядок кнопок
    // настраивается через YesNoButtonOrder и больше не привязан жёстко к позиции.
    private MessageBoxResult _leftResult = MessageBoxResult.None;
    private MessageBoxResult _middleResult = MessageBoxResult.None;
    private MessageBoxResult _rightResult = MessageBoxResult.None;

    private MessageBoxWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(_defaultButton)),
                System.Windows.Threading.DispatcherPriority.Input);
        };
        PreviewMouseDown += OnPreviewMouseDown;
        Closing += (_, _) =>
        {
            if (!_closedByButton)
            {
                _result = GetCurrentButtons() switch
                {
                    MessageBoxButton.YesNo => MessageBoxResult.No,
                    _ => MessageBoxResult.Cancel
                };
            }
        };
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            e.Handled = true;
            if (_defaultButton.IsEnabled)
            {
                _defaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
    }

    public static MessageBoxResult Show(string? text) =>
        ShowCore(text, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxDefaultButton.Ok, null, YesNoButtonOrder.YesFirst);

    public static MessageBoxResult Show(string text, string caption) =>
        ShowCore(text, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxDefaultButton.Ok, null, YesNoButtonOrder.YesFirst);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons,
        YesNoButtonOrder yesNoOrder = YesNoButtonOrder.YesFirst) =>
        ShowCore(text, caption, buttons, MessageBoxImage.None, MapDefaultButton(buttons), null, yesNoOrder);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon,
        YesNoButtonOrder yesNoOrder = YesNoButtonOrder.YesFirst) =>
        ShowCore(text, caption, buttons, icon, MapDefaultButton(buttons), null, yesNoOrder);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon,
        MessageBoxDefaultButton defaultButton, YesNoButtonOrder yesNoOrder = YesNoButtonOrder.YesFirst) =>
        ShowCore(text, caption, buttons, icon, defaultButton, null, yesNoOrder);

    public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton buttons,
        MessageBoxImage icon, MessageBoxDefaultButton defaultButton,
        YesNoButtonOrder yesNoOrder = YesNoButtonOrder.YesFirst) =>
        ShowCore(text, caption, buttons, icon, defaultButton, owner, yesNoOrder);

    private static MessageBoxResult ShowCore(string? text, string caption, MessageBoxButton buttons,
        MessageBoxImage icon, MessageBoxDefaultButton defaultButton, Window? owner, YesNoButtonOrder yesNoOrder)
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() =>
                ShowCore(text, caption, buttons, icon, defaultButton, owner, yesNoOrder));
        }
        var dlg = new MessageBoxWindow
        {
            Owner = owner ?? CurrentOwner(),
            Title = string.IsNullOrEmpty(caption) ? ApplicationTitle() : caption
        };
        dlg.MessageText.Text = text ?? "null";
        dlg.SetupButtons(buttons, yesNoOrder);
        dlg.SetupIcon(icon);
        dlg.SetupDefaultButton(buttons, defaultButton);

        dlg.ShowDialog();
        return dlg._result;
    }

    /// <summary>
    /// Показывает окно немодально (Show вместо ShowDialog) и возвращает ответ пользователя
    /// задачей. Предназначен для уведомлений из фонового кода — например, для команд
    /// AppPresenceService.
    /// </summary>
    /// <remarks>
    /// ShowDialog без видимого owner блокирует все окна приложения на время показа, поэтому
    /// для диалога, который может открыться вне фокуса оператора, он не годится: ответить
    /// на него будет некому. Окно показывается с ShowInTaskbar, чтобы его можно было найти.
    /// </remarks>
    public static Task<MessageBoxResult> ShowNonModalAsync(string text, string caption, MessageBoxButton buttons,
        MessageBoxImage icon, YesNoButtonOrder yesNoOrder = YesNoButtonOrder.YesFirst) =>
        ShowNonModalCore(text, caption, buttons, icon, MapDefaultButton(buttons), null, yesNoOrder);

    private static Task<MessageBoxResult> ShowNonModalCore(string? text, string caption, MessageBoxButton buttons,
        MessageBoxImage icon, MessageBoxDefaultButton defaultButton, Window? owner, YesNoButtonOrder yesNoOrder)
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() =>
                ShowNonModalCore(text, caption, buttons, icon, defaultButton, owner, yesNoOrder));
        }

        var dlg = new MessageBoxWindow
        {
            Owner = owner ?? CurrentOwner(),
            Title = string.IsNullOrEmpty(caption) ? ApplicationTitle() : caption,
            ShowInTaskbar = true,
        };
        dlg.MessageText.Text = text ?? "null";
        dlg.SetupButtons(buttons, yesNoOrder);
        dlg.SetupIcon(icon);
        dlg.SetupDefaultButton(buttons, defaultButton);

        var tcs = new TaskCompletionSource<MessageBoxResult>();
        dlg.Closed += (_, _) => tcs.TrySetResult(dlg._result);

        dlg.Show();
        // Поднимаем окно на передний план, но не держим Topmost постоянно —
        // тот же приём, что и в AppPresenceService.ActivateMainWindow().
        dlg.Activate();
        dlg.Topmost = true;
        dlg.Topmost = false;
        dlg.Focus();

        return tcs.Task;
    }

    private void SetupButtons(MessageBoxButton buttons, YesNoButtonOrder yesNoOrder)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                ButtonRight.Content = "OK";
                ButtonRight.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Collapsed;
                ButtonLeft.Visibility = Visibility.Collapsed;
                _rightResult = MessageBoxResult.OK;
                break;
            case MessageBoxButton.OKCancel:
                ButtonRight.Content = "OK";
                ButtonLeft.Content = "Отмена";
                ButtonRight.Visibility = Visibility.Visible;
                ButtonLeft.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Collapsed;
                _rightResult = MessageBoxResult.OK;
                _leftResult = MessageBoxResult.Cancel;
                break;
            case MessageBoxButton.YesNo:
                ButtonRight.Visibility = Visibility.Visible;
                ButtonLeft.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Collapsed;
                if (yesNoOrder == YesNoButtonOrder.YesFirst)
                {
                    ButtonLeft.Content = "Да";
                    ButtonRight.Content = "Нет";
                    _leftResult = MessageBoxResult.Yes;
                    _rightResult = MessageBoxResult.No;
                }
                else
                {
                    ButtonLeft.Content = "Нет";
                    ButtonRight.Content = "Да";
                    _leftResult = MessageBoxResult.No;
                    _rightResult = MessageBoxResult.Yes;
                }
                break;
            case MessageBoxButton.YesNoCancel:
                ButtonRight.Content = "Да";
                ButtonMiddle.Content = "Нет";
                ButtonLeft.Content = "Отмена";
                ButtonRight.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Visible;
                ButtonLeft.Visibility = Visibility.Visible;
                _rightResult = MessageBoxResult.Yes;
                _middleResult = MessageBoxResult.No;
                _leftResult = MessageBoxResult.Cancel;
                break;
        }
    }

    private void SetupDefaultButton(MessageBoxButton buttons, MessageBoxDefaultButton defaultButton)
    {
        _defaultButton = buttons switch
        {
            MessageBoxButton.OK => ButtonRight,
            MessageBoxButton.OKCancel => defaultButton switch
            {
                MessageBoxDefaultButton.Ok or MessageBoxDefaultButton.Yes or MessageBoxDefaultButton.First => ButtonRight,
                MessageBoxDefaultButton.Cancel or MessageBoxDefaultButton.No or MessageBoxDefaultButton.Second => ButtonLeft,
                _ => ButtonRight
            },
            // Да/Нет может быть в любом порядке (см. YesNoButtonOrder), поэтому кнопка
            // ищется по уже сохранённому в SetupButtons результату, а не по позиции.
            MessageBoxButton.YesNo => defaultButton switch
            {
                MessageBoxDefaultButton.Yes or MessageBoxDefaultButton.First =>
                    _leftResult == MessageBoxResult.Yes ? ButtonLeft : ButtonRight,
                MessageBoxDefaultButton.No or MessageBoxDefaultButton.Second =>
                    _leftResult == MessageBoxResult.No ? ButtonLeft : ButtonRight,
                _ => _leftResult == MessageBoxResult.Yes ? ButtonLeft : ButtonRight
            },
            MessageBoxButton.YesNoCancel => defaultButton switch
            {
                MessageBoxDefaultButton.Yes or MessageBoxDefaultButton.First => ButtonRight,
                MessageBoxDefaultButton.No or MessageBoxDefaultButton.Second => ButtonMiddle,
                MessageBoxDefaultButton.Cancel or MessageBoxDefaultButton.Third => ButtonLeft,
                _ => ButtonRight
            },
            _ => ButtonRight
        };
        _defaultButton.IsDefault = true;
        _defaultButton.FontWeight = FontWeights.Bold;
        if (buttons == MessageBoxButton.OK) return;
        foreach (var btn in new[] { ButtonLeft, ButtonMiddle, ButtonRight })
        {
            if (btn != _defaultButton && btn.Visibility == Visibility.Visible)
            {
                btn.IsCancel = true;
                break;
            }
        }
    }

    private void SetupIcon(MessageBoxImage icon)
    {
        var brush = icon switch
        {
            MessageBoxImage.Error => (DrawingBrush)FindResource("IconError"),
            MessageBoxImage.Warning => (DrawingBrush)FindResource("IconWarning"),
            MessageBoxImage.Information => (DrawingBrush)FindResource("IconInformation"),
            MessageBoxImage.Question => (DrawingBrush)FindResource("IconQuestion"),
            _ => null
        };
        if (brush is null)
        {
            IconRect.Visibility = Visibility.Collapsed;
            return;
        }
        IconRect.Fill = brush;
        IconRect.Visibility = Visibility.Visible;
    }

    private void OnRightClick(object sender, RoutedEventArgs e)
    {
        _closedByButton = true;
        _result = _rightResult;
        Close();
    }

    private void OnMiddleClick(object sender, RoutedEventArgs e)
    {
        _closedByButton = true;
        _result = _middleResult;
        Close();
    }

    private void OnLeftClick(object sender, RoutedEventArgs e)
    {
        _closedByButton = true;
        _result = _leftResult;
        Close();
    }

    private MessageBoxButton GetCurrentButtons()
    {
        if (ButtonMiddle.Visibility == Visibility.Visible) return MessageBoxButton.YesNoCancel;
        if (ButtonLeft.Visibility == Visibility.Visible)
        {
            return (string)ButtonRight.Content == "OK"
                ? MessageBoxButton.OKCancel
                : MessageBoxButton.YesNo;
        }
        return MessageBoxButton.OK;
    }

    private static MessageBoxDefaultButton MapDefaultButton(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => MessageBoxDefaultButton.Ok,
        MessageBoxButton.OKCancel => MessageBoxDefaultButton.Ok,
        MessageBoxButton.YesNo => MessageBoxDefaultButton.Yes,
        MessageBoxButton.YesNoCancel => MessageBoxDefaultButton.Yes,
        _ => MessageBoxDefaultButton.Ok
    };

    private static Window? CurrentOwner()
    {
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.IsActive) return window;
            }
        }
        catch
        {
        }
        return null;
    }

    private static string ApplicationTitle()
    {
        try
        {
            return Application.Current?.MainWindow?.Title ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
