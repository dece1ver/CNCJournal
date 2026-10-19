using System.Windows;
using System.Windows.Media;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Кисти текущей темы Areopag для конвертеров и вьюмоделей.
    /// Возвращает разделяемую кисть из Application.Resources (тот же объект,
    /// что в палитре) либо замороженный фолбэк светлой темы, если ресурс
    /// недоступен (дизайнер, юнит-тесты).
    /// Шрифты не затрагивает.
    /// </summary>
    public static class ThemeBrushes
    {
        public static Brush Get(string resourceKey, Brush fallback)
        {
            var app = Application.Current;
            if (app != null)
            {
                var found = app.TryFindResource(resourceKey) as Brush;
                if (found != null) return found;
            }
            return fallback;
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static Brush Text => Get("Areopag.Text", Frozen(0x00, 0x24, 0x3D));
        public static Brush Text2 => Get("Areopag.Text2", Frozen(0x5E, 0x67, 0x84));
        public static Brush Text3 => Get("Areopag.Text3", Frozen(0x75, 0x7C, 0x95));
        public static Brush Accent => Get("Areopag.Accent", Frozen(0x30, 0x3C, 0x60));
        public static Brush Ok => Get("Areopag.Ok", Frozen(0x00, 0xAD, 0x68));
        public static Brush Alert => Get("Areopag.Alert", Frozen(0xE6, 0x3C, 0x2F));
        public static Brush Warn => Get("Areopag.Warn", Frozen(0xB8, 0x86, 0x2E));
        public static Brush Disabled => Get("Areopag.Disabled", Frozen(0xBD, 0xBD, 0xBD));
    }
}
