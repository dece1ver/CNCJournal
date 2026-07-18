using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace libeLog.Views;

public partial class MessageBoxWindow : Window
{
    private MessageBoxResult _result;
    private Button _defaultButton = null!;
    private bool _closedByButton;

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
        ShowCore(text, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxDefaultButton.Ok, null);

    public static MessageBoxResult Show(string text, string caption) =>
        ShowCore(text, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxDefaultButton.Ok, null);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons) =>
        ShowCore(text, caption, buttons, MessageBoxImage.None, MapDefaultButton(buttons), null);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon) =>
        ShowCore(text, caption, buttons, icon, MapDefaultButton(buttons), null);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon,
        MessageBoxDefaultButton defaultButton) =>
        ShowCore(text, caption, buttons, icon, defaultButton, null);

    public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton buttons,
        MessageBoxImage icon, MessageBoxDefaultButton defaultButton) =>
        ShowCore(text, caption, buttons, icon, defaultButton, owner);

    private static MessageBoxResult ShowCore(string? text, string caption, MessageBoxButton buttons,
        MessageBoxImage icon, MessageBoxDefaultButton defaultButton, Window? owner)
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() =>
                ShowCore(text, caption, buttons, icon, defaultButton, owner));
        }
        var dlg = new MessageBoxWindow
        {
            Owner = owner ?? CurrentOwner(),
            Title = string.IsNullOrEmpty(caption) ? ApplicationTitle() : caption
        };
        dlg.MessageText.Text = text ?? "null";
        dlg.SetupButtons(buttons);
        dlg.SetupIcon(icon);
        dlg.SetupDefaultButton(buttons, defaultButton);

        dlg.ShowDialog();
        return dlg._result;
    }

    private void SetupButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                ButtonRight.Content = "OK";
                ButtonRight.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Collapsed;
                ButtonLeft.Visibility = Visibility.Collapsed;
                break;
            case MessageBoxButton.OKCancel:
                ButtonRight.Content = "OK";
                ButtonLeft.Content = "Отмена";
                ButtonRight.Visibility = Visibility.Visible;
                ButtonLeft.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Collapsed;
                break;
            case MessageBoxButton.YesNo:
                ButtonRight.Content = "Да";
                ButtonLeft.Content = "Нет";
                ButtonRight.Visibility = Visibility.Visible;
                ButtonLeft.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Collapsed;
                break;
            case MessageBoxButton.YesNoCancel:
                ButtonRight.Content = "Да";
                ButtonMiddle.Content = "Нет";
                ButtonLeft.Content = "Отмена";
                ButtonRight.Visibility = Visibility.Visible;
                ButtonMiddle.Visibility = Visibility.Visible;
                ButtonLeft.Visibility = Visibility.Visible;
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
            MessageBoxButton.YesNo => defaultButton switch
            {
                MessageBoxDefaultButton.Yes or MessageBoxDefaultButton.First => ButtonRight,
                MessageBoxDefaultButton.No or MessageBoxDefaultButton.Second => ButtonLeft,
                _ => ButtonRight
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
        var buttons = GetCurrentButtons();
        _result = buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.Yes,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            _ => MessageBoxResult.None
        };
        Close();
    }

    private void OnMiddleClick(object sender, RoutedEventArgs e)
    {
        _closedByButton = true;
        _result = GetCurrentButtons() switch
        {
            MessageBoxButton.YesNoCancel => MessageBoxResult.No,
            _ => MessageBoxResult.None
        };
        Close();
    }

    private void OnLeftClick(object sender, RoutedEventArgs e)
    {
        _closedByButton = true;
        var buttons = GetCurrentButtons();
        _result = buttons switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };
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
