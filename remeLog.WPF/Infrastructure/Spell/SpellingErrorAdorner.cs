using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace remeLog.Infrastructure.Spell
{
    /// <summary>Волнистое подчёркивание слов с ошибками в TextBox редактора.</summary>
    internal sealed class SpellingErrorAdorner : Adorner
    {
        private IReadOnlyList<NativeSpellingError> _errors = Array.Empty<NativeSpellingError>();

        public SpellingErrorAdorner(TextBox adorned) : base(adorned)
        {
            IsHitTestVisible = false;
        }

        public void SetErrors(IReadOnlyList<NativeSpellingError> errors)
        {
            _errors = errors;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_errors.Count == 0 || AdornedElement is not TextBox box) return;
            string text = box.Text;
            if (string.IsNullOrEmpty(text)) return;
            if (box.TryFindResource("Areopag.Alert") is not SolidColorBrush alert) return;
            var pen = new Pen(new SolidColorBrush(alert.Color), 1.2);
            pen.Freeze();

            foreach (var error in _errors)
            {
                if (error.Start < 0 || error.Start + error.Length > text.Length || error.Length <= 0) continue;
                // символы слова группируются по визуальным строкам (перенос слов)
                double segX0 = double.NaN, segX1 = 0, segY = 0, lineBottom = double.NaN;
                for (int i = error.Start; i < error.Start + error.Length; i++)
                {
                    Rect r = box.GetRectFromCharacterIndex(i);
                    if (r.IsEmpty) continue;
                    if (double.IsNaN(lineBottom) || Math.Abs(r.Bottom - lineBottom) > 1)
                    {
                        DrawWave(dc, pen, ref segX0, ref segX1, segY);
                        lineBottom = r.Bottom;
                        segY = r.Bottom - 1;
                    }
                    segX0 = double.IsNaN(segX0) ? r.Left : Math.Min(segX0, r.Left);
                    segX1 = Math.Max(segX1, r.Right);
                }
                DrawWave(dc, pen, ref segX0, ref segX1, segY);
            }
        }

        private static void DrawWave(DrawingContext dc, Pen pen, ref double x0, ref double x1, double y)
        {
            if (!double.IsNaN(x0) && x1 - x0 >= 2)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(x0, y), false, false);
                    double x = x0;
                    while (x < x1)
                    {
                        double mid = Math.Min(x + 1.5, x1);
                        double end = Math.Min(x + 3, x1);
                        ctx.LineTo(new Point(mid, y - 1.5), true, false);
                        if (end > mid) ctx.LineTo(new Point(end, y), true, false);
                        x = end;
                    }
                }
                dc.DrawGeometry(null, pen, geo);
            }
            x0 = double.NaN;
            x1 = 0;
        }
    }
}
