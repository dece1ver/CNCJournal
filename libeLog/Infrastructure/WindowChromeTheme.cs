using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace libeLog.Infrastructure
{
    /// <summary>
    /// Тёмный/светлый chrome окон (заголовок) вслед за темой приложения.
    /// WPF палитрой non-client area не красится — это делает DWM по HWND
    /// через DWMWA_USE_IMMERSIVE_DARK_MODE. Системную тему не читаем:
    /// вкл/выкл строго по <see cref="ThemeManager.Current"/>.
    /// Подключение — атрибут на корневом теге окна:
    /// &lt;Window ... chrome:WindowChromeTheme.Enabled="True"&gt;
    /// (xmlns:chrome="clr-namespace:libeLog.Infrastructure;assembly=libeLog").
    /// Class-handler на Loaded здесь НЕ подходит: Loaded без instance-подписчиков
    /// доставляется ненадёжно, а поведение ниже подписывается само.
    /// </summary>
    public static class WindowChromeTheme
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 — Win10 20H1+/Win11, 19 — Win10 1809–1909.
        // Где атрибут не поддерживается, DWM вернёт ошибку — это нормальный no-op.
        private const int DwmwaUseImmersiveDarkModeLatest = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(WindowChromeTheme),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window) return;
            // Сначала снять: XAML-парсер и Hide/Show могут вызывать повторно
            window.SourceInitialized -= OnSourceInitialized;
            window.Loaded -= OnWindowLoaded;
            if ((bool)e.NewValue)
            {
                window.SourceInitialized += OnSourceInitialized;
                window.Loaded += OnWindowLoaded;
            }
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window) HookWindow(window);
        }

        private static void OnSourceInitialized(object? sender, EventArgs e)
        {
            if (sender is Window window) HookWindow(window);
        }

        private static void HookWindow(Window window)
        {
            bool dark = ThemeManager.Current == AppTheme.Dark;
            Apply(window, dark);
            // DWM может сбросить атрибут при первом показе кадра — повторить после отрисовки
            window.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    if (window.IsLoaded) Apply(window, ThemeManager.Current == AppTheme.Dark);
                }));
        }

        /// <summary>
        /// Обновляет chrome всех открытых окон. Вызывается из ThemeManager.Apply.
        /// </summary>
        public static void RefreshAll(bool dark)
        {
            var app = Application.Current;
            if (app == null) return;
            foreach (Window window in app.Windows)
            {
                try
                {
                    Apply(window, dark);
                }
                catch
                {
                    // Закрывающееся окно и т.п. — пропускаем
                }
            }
        }

        /// <summary>
        /// Применяет тему chrome к одному окну. Безопасно вызывать когда угодно.
        /// </summary>
        public static void Apply(Window window, bool dark)
        {
            if (window == null) return;
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLatest, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
            }
            catch
            {
                // Нет DWM / окно разбирается — chrome остаётся системным
            }
        }
    }
}
