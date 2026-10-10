using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

        // меню создано поведением (а не окном) — при отключении убирается
        private static readonly DependencyProperty IsDefaultMenuProperty =
            DependencyProperty.RegisterAttached(
                "IsDefaultMenu", typeof(bool), typeof(SpellCheckBehavior),
                new PropertyMetadata(false));

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
            box.ContextMenuOpening += OnContextMenuOpening;
            if (box.ContextMenu is null)
            {
                box.ContextMenu = CreateDefaultEditingMenu();
                box.SetValue(IsDefaultMenuProperty, true);
            }
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
            box.ContextMenuOpening -= OnContextMenuOpening;
            if ((bool)box.GetValue(IsDefaultMenuProperty))
            {
                box.ClearValue(IsDefaultMenuProperty);
                box.ContextMenu = null;
            }
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

        /// <summary>
        /// Варианты исправления слова под кареткой — в начало переданного меню.
        /// Меню может переиспользоваться: внедрённые пункты чистятся при закрытии.
        /// </summary>
        public static void AppendSuggestionsToMenu(ContextMenu menu, TextBox box)
        {
            if (!GetIsEnabled(box)) return;
            if (!TryGetWordAtCaret(box, out var error, out var word)) return;
            var candidates = GetSuggestions(word);
            if (candidates.Count == 0) return;

            var injected = new List<object>();
            var header = new MenuItem
            {
                Header = $"Исправить «{word}»:",
                IsEnabled = false,
                FontWeight = FontWeights.SemiBold,
            };
            menu.Items.Insert(0, header);
            injected.Add(header);
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                string candidate = candidates[i];
                var item = new MenuItem { Header = candidate };
                item.Click += (_, _) => ApplySuggestion(box, error, candidate);
                menu.Items.Insert(1, item);
                injected.Add(item);
            }
            var separator = new Separator();
            menu.Items.Insert(1 + candidates.Count, separator);
            injected.Add(separator);

            void OnClosed(object s, RoutedEventArgs _)
            {
                menu.Closed -= OnClosed;
                foreach (var item in injected) menu.Items.Remove(item);
            }
            menu.Closed -= OnClosed;
            menu.Closed += OnClosed;
        }

        private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not TextBox box || box.ContextMenu is not { } menu) return;
            // каретку — в точку правого клика (по клавиатуре меню открывается без сдвига)
            if (Mouse.RightButton == MouseButtonState.Pressed)
            {
                int clickIndex = box.GetCharacterIndexFromPoint(Mouse.GetPosition(box), true);
                MoveCaretToClick(box, clickIndex);
            }
            AppendSuggestionsToMenu(menu, box);
        }

        /// <summary>
        /// Каретка в точку правого клика, но клик внутри выделения выделение
        /// не схлопывает (иначе Копировать/Вырезать в контекстном меню нечего взять).
        /// </summary>
        public static void MoveCaretToClick(TextBox box, int clickIndex)
        {
            if (clickIndex < 0) return;
            if (box.SelectionLength > 0
                && clickIndex >= box.SelectionStart
                && clickIndex < box.SelectionStart + box.SelectionLength)
                return;
            box.CaretIndex = clickIndex;
        }

        // меню уровня обычного TextBox (как системное): поведение ставит его только
        // полям без своего меню, чтобы варианты исправлений было куда вставить
        private static ContextMenu CreateDefaultEditingMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Отменить", Command = ApplicationCommands.Undo });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Копировать", Command = ApplicationCommands.Copy });
            menu.Items.Add(new MenuItem { Header = "Вырезать", Command = ApplicationCommands.Cut });
            menu.Items.Add(new MenuItem { Header = "Вставить", Command = ApplicationCommands.Paste });
            menu.Items.Add(new MenuItem { Header = "Выделить все", Command = ApplicationCommands.SelectAll });
            return menu;
        }
    }
}
