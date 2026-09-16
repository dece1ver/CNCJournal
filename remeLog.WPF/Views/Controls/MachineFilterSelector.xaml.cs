using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace remeLog.Views.Controls
{
    /// <summary>
    /// Фильтр по станкам: кнопка-«комбобокс» + попап со списком чекбоксов.
    /// Один контрол на все окна (PartsInfoWindow, календарь проверок), чтобы
    /// не дублировать разметку и логику закрытия попапа.
    /// </summary>
    public partial class MachineFilterSelector : UserControl
    {
        public MachineFilterSelector()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(MachineFilterSelector));

        /// <summary> Коллекция MachineFilter (Machine/Filter). </summary>
        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SummaryProperty =
            DependencyProperty.Register(nameof(Summary), typeof(string), typeof(MachineFilterSelector));

        /// <summary> Текст на кнопке (например «Станки: 3»). </summary>
        public string? Summary
        {
            get => (string?)GetValue(SummaryProperty);
            set => SetValue(SummaryProperty, value);
        }

        public static readonly DependencyProperty ShowAllCommandProperty =
            DependencyProperty.Register(nameof(ShowAllCommand), typeof(ICommand), typeof(MachineFilterSelector));

        public ICommand? ShowAllCommand
        {
            get => (ICommand?)GetValue(ShowAllCommandProperty);
            set => SetValue(ShowAllCommandProperty, value);
        }

        public static readonly DependencyProperty HideAllCommandProperty =
            DependencyProperty.Register(nameof(HideAllCommand), typeof(ICommand), typeof(MachineFilterSelector));

        public ICommand? HideAllCommand
        {
            get => (ICommand?)GetValue(HideAllCommandProperty);
            set => SetValue(HideAllCommandProperty, value);
        }

        public static readonly DependencyProperty InvertCommandProperty =
            DependencyProperty.Register(nameof(InvertCommand), typeof(ICommand), typeof(MachineFilterSelector));

        public ICommand? InvertCommand
        {
            get => (ICommand?)GetValue(InvertCommandProperty);
            set => SetValue(InvertCommandProperty, value);
        }

        private Window? _host;

        private void Popup_Opened(object sender, System.EventArgs e)
        {
            _host = Window.GetWindow(this);
            if (_host != null)
            {
                _host.PreviewMouseDown += Host_PreviewMouseDown;
                _host.Deactivated += Host_Deactivated;
            }
        }

        private void Popup_Closed(object sender, System.EventArgs e)
        {
            if (_host != null)
            {
                _host.PreviewMouseDown -= Host_PreviewMouseDown;
                _host.Deactivated -= Host_Deactivated;
                _host = null;
            }
        }

        private void Host_Deactivated(object? sender, System.EventArgs e) => Popup.IsOpen = false;

        private void Host_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!Popup.IsOpen) return;
            var src = e.OriginalSource as DependencyObject;
            if (IsWithin(src, Toggle)) return;
            if (Popup.Child is DependencyObject child && IsWithin(src, child)) return;
            Popup.IsOpen = false;
        }

        private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
        {
            while (source != null)
            {
                if (ReferenceEquals(source, ancestor)) return true;
                if (source is not (Visual or System.Windows.Media.Media3D.Visual3D)) return false;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }
    }
}
