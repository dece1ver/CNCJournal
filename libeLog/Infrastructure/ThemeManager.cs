using System;
using System.Linq;
using System.Windows;

namespace libeLog.Infrastructure
{
    /// <summary>
    /// Тема оформления Areopag. Light — по умолчанию.
    /// </summary>
    public enum AppTheme
    {
        Light = 0,
        Dark = 1
    }

    /// <summary>
    /// Переключение палитры Areopag без перезапуска.
    /// Требование: тематические кисти (Areopag.*, ComboBox.*, Menu.*,
    /// Outline*) должны ссылаться через DynamicResource, иначе уже
    /// созданные элементы не обновятся до перезапуска.
    /// Шрифты (Areopag.FontUI/FontMono) не трогает.
    /// Заодно обновляет chrome открытых окон (см. WindowChromeTheme).
    /// </summary>
    public static class ThemeManager
    {
        public const string LightPaletteUri =
            "pack://application:,,,/libeLog;component/Areopag/Areopag.Palette.xaml";
        public const string DarkPaletteUri =
            "pack://application:,,,/libeLog;component/Areopag/Areopag.Palette.Dark.xaml";

        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static string PaletteUriFor(AppTheme theme) =>
            theme == AppTheme.Dark ? DarkPaletteUri : LightPaletteUri;

        public static void Apply(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var wanted = PaletteUriFor(theme);
            var merged = app.Resources.MergedDictionaries;

            // Сравнение по имени файла: в App.xaml палитра может быть
            // подключена как относительным, так и абсолютным pack-URI.
            static bool IsPalette(ResourceDictionary d) =>
                d.Source != null &&
                (d.Source.OriginalString.Contains("Areopag.Palette.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.EndsWith("Areopag/Areopag.Palette.xaml", StringComparison.OrdinalIgnoreCase));

            var existing = merged.FirstOrDefault(IsPalette);

            if (existing != null && existing.Source != null &&
                ((theme == AppTheme.Dark && existing.Source.OriginalString.Contains("Areopag.Palette.Dark.xaml", StringComparison.OrdinalIgnoreCase)) ||
                 (theme == AppTheme.Light && !existing.Source.OriginalString.Contains("Areopag.Palette.Dark.xaml", StringComparison.OrdinalIgnoreCase))))
            {
                Current = theme;
                return;
            }

            var palette = new ResourceDictionary { Source = new Uri(wanted, UriKind.Absolute) };
            if (existing != null)
            {
                var index = merged.IndexOf(existing);
                merged[index] = palette;
            }
            else
            {
                merged.Insert(0, palette);
            }

            Current = theme;

            // Chrome окон (заголовки) — вслед за палитрой, на всех открытых окнах
            WindowChromeTheme.RefreshAll(theme == AppTheme.Dark);
        }
    }
}
