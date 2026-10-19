using remeLog.Infrastructure;
using remeLog.Validation;
using System;
using System.ComponentModel;
using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace remeLog.Views
{
    public partial class UserInputDialogWindow : Window
    {
        public class OptionalCheckBoxItem
        {
            public bool IsChecked { get; set; }
            public string Content { get; init; }

            public OptionalCheckBoxItem(bool isChecked, string content)
            {
                IsChecked = isChecked;
                Content = content;
            }
        }

        public static new readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(UserInputDialogWindow), new PropertyMetadata("Ввод"));

        public static readonly DependencyProperty PromptTextProperty =
            DependencyProperty.Register(nameof(PromptText), typeof(string), typeof(UserInputDialogWindow), new PropertyMetadata("Введите значение:"));

        public static readonly DependencyProperty UserInputProperty =
            DependencyProperty.Register(nameof(UserInput), typeof(string), typeof(UserInputDialogWindow), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ExpectedTypeProperty =
            DependencyProperty.Register(nameof(ExpectedType), typeof(Type), typeof(UserInputDialogWindow), new PropertyMetadata(typeof(string)));

        public static readonly DependencyProperty UseOperationsContextMenuProperty =
            DependencyProperty.Register(nameof(UseOperationsContextMenu), typeof(bool), typeof(UserInputDialogWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty FocusAndSelectProperty =
            DependencyProperty.Register(nameof(FocusAndSelect), typeof(bool), typeof(UserInputDialogWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty OptionalCheckBoxProperty =
            DependencyProperty.Register(nameof(OptionalCheckBox), typeof(OptionalCheckBoxItem), typeof(UserInputDialogWindow), new PropertyMetadata(null));


        public bool UseOptionalCheckBox => OptionalCheckBox != null;

        public new string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string PromptText
        {
            get => (string)GetValue(PromptTextProperty);
            set => SetValue(PromptTextProperty, value);
        }

        public string UserInput
        {
            get => (string)GetValue(UserInputProperty);
            set => SetValue(UserInputProperty, value);
        }

        public Type? ExpectedType
        {
            get => (Type)GetValue(ExpectedTypeProperty);
            set => SetValue(ExpectedTypeProperty, value);
        }

        public bool UseOperationsContextMenu
        {
            get => (bool)GetValue(UseOperationsContextMenuProperty);
            set => SetValue(UseOperationsContextMenuProperty, value);
        }
        public bool FocusAndSelect
        {
            get { return (bool)GetValue(FocusAndSelectProperty); }
            set { SetValue(FocusAndSelectProperty, value); }
        }
        public OptionalCheckBoxItem? OptionalCheckBox
        {
            get { return (OptionalCheckBoxItem?)GetValue(OptionalCheckBoxProperty); }
            set { SetValue(OptionalCheckBoxProperty, value); }
        }

        public UserInputDialogWindow(string title = "Ввод", string prompt = "Введите значение:", string? defaultInput = null, Type? expectedType = null, bool focusAndSelect = false, bool useOperationsContexMenu = false, OptionalCheckBoxItem? checkBox = null)
        {
            InitializeComponent();

            Title = title;
            PromptText = prompt;
            UserInput = defaultInput ?? string.Empty;
            ExpectedType = expectedType;
            if (ExpectedType != null)
            {
                var binding = BindingOperations.GetBinding(InputBox, TextBox.TextProperty);
                binding?.ValidationRules.Add(new InputTypeValidationRule { ExpectedType = expectedType });
            }
            DataContext = this;
            FocusAndSelect = focusAndSelect;
            UseOperationsContextMenu = useOperationsContexMenu;
            OptionalCheckBox = checkBox;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (UseOperationsContextMenu)
            {
                var ctxm = CreateEditingContextMenu();
                InputBox.ContextMenu = ctxm;
            }
            if (FocusAndSelect)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    InputBox.Focus();
                    InputBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            
        }

        private void OnVariantClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            var text = "";
            if (item.Header != null) text = item.Header.ToString();
            if (item.Tag != null) text = item.Tag.ToString();
            if (Keyboard.FocusedElement is TextBox textBox)
            {
                textBox.Text = text;
            }
        }

        public ContextMenu CreateEditingContextMenu()
        {
            var contextMenu = new ContextMenu();

            // Стандартные команды редактирования
            var copyItem = new MenuItem
            {
                Header = "Копировать",
                Command = ApplicationCommands.Copy
            };
            contextMenu.Items.Add(copyItem);

            var cutItem = new MenuItem
            {
                Header = "Вырезать",
                Command = ApplicationCommands.Cut
            };
            contextMenu.Items.Add(cutItem);

            var pasteItem = new MenuItem
            {
                Header = "Вставить",
                Command = ApplicationCommands.Paste
            };
            contextMenu.Items.Add(pasteItem);

            var selectAllItem = new MenuItem
            {
                Header = "Выделить все",
                Command = ApplicationCommands.SelectAll
            };
            contextMenu.Items.Add(selectAllItem);

            contextMenu.Items.Add(new Separator());

            var clearItem = new MenuItem
            {
                Header = "Очистить",
                Tag = "",
            };
            clearItem.Click += OnVariantClick;
            contextMenu.Items.Add(clearItem);

            if (UseOperationsContextMenu)
            {
                contextMenu.Items.Add(new Separator());

                // Операции
                foreach (var op in AppSettings.CncOperations)
                {
                    var menuItem = new MenuItem { Header = op };
                    menuItem.Click += OnVariantClick;
                    contextMenu.Items.Add(menuItem);
                }
            }



            return contextMenu;
        }

        private static T? FindBoundFrameworkElement<T>(DependencyObject parent) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && BindingOperations.IsDataBound(element, GetDependencyProperty(element)))
                {
                    return element;
                }

                var result = FindBoundFrameworkElement<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static DependencyProperty GetDependencyProperty(FrameworkElement element)
        {
            if (element is TextBox)
                return TextBox.TextProperty;
            if (element is TextBlock)
                return TextBlock.TextProperty;
            throw new NotSupportedException("Unsupported element type");
        }
    }
}
