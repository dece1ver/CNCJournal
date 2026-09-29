using eLog.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;

namespace eLog.Views.Windows.Dialogs
{
    /// <summary>
    /// Окно производственного ИИ-чата. DataContext — AiChatViewModel (задаётся в XAML).
    /// Здесь только поведение представления: Enter отправляет, автопрокрутка ленты.
    /// </summary>
    public partial class AiChatWindow : Window
    {
        public AiChatWindow()
        {
            InitializeComponent();
            // Ссылки источников открываем в браузере (событие всплывает от Hyperlink).
            AddHandler(Hyperlink.RequestNavigateEvent,
                new RequestNavigateEventHandler(Hyperlink_RequestNavigate));
            if (DataContext is AiChatViewModel vm)
                vm.Messages.CollectionChanged += Messages_CollectionChanged;
            Loaded += (_, _) =>
            {
                InputBox.Focus();
                // Молча стартуем приветствие: модель собирает информацию
                // об операторе инструментами, статики в ленте больше нет.
                if (DataContext is AiChatViewModel vm)
                    _ = vm.GreetAsync();
            };
        }

        // Enter без модификаторов — отправить (команда сама проверит CanExecute),
        // Shift+Enter — новая строка.
        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            if (DataContext is AiChatViewModel vm && vm.SendCommand.CanExecute(null))
                vm.SendCommand.Execute(null);
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Стрим токенов меняет Text внутри элемента — тоже мотаем вниз.
            if (e.NewItems != null)
                foreach (ChatMessageItem item in e.NewItems)
                    item.PropertyChanged += Message_PropertyChanged;
            if (e.OldItems != null)
                foreach (ChatMessageItem item in e.OldItems)
                    item.PropertyChanged -= Message_PropertyChanged;
            ScrollToBottom();
        }

        private void Message_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatMessageItem.Text))
                ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            // После перерисовки, иначе ScrollToBottom берёт старую высоту.
            Dispatcher.BeginInvoke(() => MessagesScroll.ScrollToBottom());
        }

        private static void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Infrastructure.Extensions.Util.WriteLog(ex);
            }
            e.Handled = true;
        }
    }
}
