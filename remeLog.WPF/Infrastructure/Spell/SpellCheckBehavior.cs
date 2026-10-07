using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace remeLog.Infrastructure.Spell
{
    /// <summary>
    /// Подсветка орфографии в редакторе ячейки DataGrid: волнистое подчёркивание
    /// ошибок (системный Windows Spell Checking API, русский поддерживается ОС).
    /// Включается attached-свойством на стиле редактора; при недоступности API
    /// тихо ничего не делает. Публичный класс — свойство ставится из XAML.
    /// </summary>
    public static class SpellCheckBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(SpellCheckBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State", typeof(BoxState), typeof(SpellCheckBehavior),
                new PropertyMetadata(null));

        private sealed class BoxState
        {
            public DispatcherTimer? Timer;
            public SpellingErrorAdorner? Adorner;
            public ScrollViewer? Scroller;
            public IReadOnlyList<NativeSpellingError> Errors = Array.Empty<NativeSpellingError>();
        }

        private static BoxState GetState(TextBox box)
        {
            if (box.GetValue(StateProperty) is not BoxState state)
            {
                state = new BoxState();
                box.SetValue(StateProperty, state);
            }
            return state;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox box) return;
            if ((bool)e.NewValue) Attach(box);
            else Detach(box);
        }

        private static void Attach(TextBox box)
        {
            Detach(box);
            var state = GetState(box);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (_, _) => { timer.Stop(); Recheck(box); };
            state.Timer = timer;
            box.TextChanged += OnTextChanged;
            box.Loaded += OnLoaded;
            box.Unloaded += OnUnloaded;
            box.SizeChanged += OnSizeChanged;
            if (box.IsLoaded) OnLoaded(box, null);
        }

        private static void Detach(TextBox box)
        {
            if (box.GetValue(StateProperty) is not BoxState state) return;
            state.Timer?.Stop();
            state.Timer = null;
            box.TextChanged -= OnTextChanged;
            box.Loaded -= OnLoaded;
            box.Unloaded -= OnUnloaded;
            box.SizeChanged -= OnSizeChanged;
            UnhookScroller(state);
            RemoveAdorner(box, state);
            box.ClearValue(StateProperty);
        }

        private static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox box) return;
            var state = GetState(box);
            state.Timer?.Stop();
            state.Timer?.Start();
        }

        private static void OnLoaded(object sender, RoutedEventArgs? e)
        {
            if (sender is not TextBox box) return;
            HookScroller(box);
            Recheck(box);
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox box) return;
            var state = GetState(box);
            state.Timer?.Stop();
            UnhookScroller(state);
            RemoveAdorner(box, state);
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is TextBox box && GetState(box).Adorner is { } adorner) adorner.InvalidateVisual();
        }

        private static void HookScroller(TextBox box)
        {
            var state = GetState(box);
            UnhookScroller(state);
            if (box.Template.FindName("PART_ContentHost", box) is ScrollViewer scroller)
            {
                state.Scroller = scroller;
                scroller.ScrollChanged += OnScrollChanged;
            }
        }

        private static void UnhookScroller(BoxState state)
        {
            if (state.Scroller is { } scroller)
            {
                scroller.ScrollChanged -= OnScrollChanged;
                state.Scroller = null;
            }
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // отправитель — PART_ContentHost внутри TextBox: подчёркивания едут
            // вместе с текстом, adorner перерисовывается по новым координатам
            DependencyObject? current = sender as DependencyObject;
            while (current is not null && current is not TextBox)
                current = VisualTreeHelper.GetParent(current);
            if (current is TextBox box && GetState(box).Adorner is { } adorner)
                adorner.InvalidateVisual();
        }

        private static void EnsureAdorner(TextBox box, BoxState state)
        {
            if (state.Adorner is not null) return;
            var layer = AdornerLayer.GetAdornerLayer(box);
            if (layer is null) return;
            state.Adorner = new SpellingErrorAdorner(box);
            layer.Add(state.Adorner);
        }

        private static void RemoveAdorner(TextBox box, BoxState state)
        {
            if (state.Adorner is null) return;
            AdornerLayer.GetAdornerLayer(box)?.Remove(state.Adorner);
            state.Adorner = null;
        }

        private static void Recheck(TextBox box)
        {
            var state = GetState(box);
            if (!GetIsEnabled(box) || !box.IsLoaded)
            {
                state.Errors = Array.Empty<NativeSpellingError>();
                state.Adorner?.SetErrors(state.Errors);
                return;
            }
            var errors = NativeSpellCheck.Check(box.Text ?? string.Empty, ShopDictionary.IsAllowed);
            state.Errors = errors;
            if (errors.Count > 0) EnsureAdorner(box, state);
            if (state.Adorner is { } adorner)
            {
                adorner.SetErrors(errors);
                if (errors.Count == 0) RemoveAdorner(box, state);
            }
        }

        /// <summary>Ошибки текущей проверки редактора (для контекстного меню).</summary>
        public static IReadOnlyList<NativeSpellingError> GetErrors(TextBox box) =>
            box.GetValue(StateProperty) is BoxState state ? state.Errors : Array.Empty<NativeSpellingError>();

        /// <summary>Ошибка под кареткой и само слово; null если каретка не на ошибке.</summary>
        public static bool TryGetWordAtCaret(TextBox box, out NativeSpellingError error, out string word)
        {
            error = default;
            word = string.Empty;
            string text = box.Text ?? string.Empty;
            int caret = box.CaretIndex;
            foreach (var e in GetErrors(box))
            {
                if (caret >= e.Start && caret <= e.Start + e.Length
                    && e.Start + e.Length <= text.Length)
                {
                    error = e;
                    word = text.Substring(e.Start, e.Length);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Замена слова-опечатки вариантом; привязка сразу уходит в источник.</summary>
        public static void ApplySuggestion(TextBox box, NativeSpellingError error, string replacement)
        {
            string text = box.Text ?? string.Empty;
            if (error.Start < 0 || error.Start + error.Length > text.Length) return;
            box.Text = text.Substring(0, error.Start) + replacement + text.Substring(error.Start + error.Length);
            box.CaretIndex = Math.Min(error.Start + replacement.Length, box.Text.Length);
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.Focus();
            Recheck(box);
        }

        public static IReadOnlyList<string> GetSuggestions(string word, int max = 5) =>
            NativeSpellCheck.Suggest(word, max);
    }
}
