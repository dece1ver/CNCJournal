using remeLog.Core.Extensions;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace remeLog.Core.Services
{
    /// <summary>
    /// Кандидат на исключение записи из расчёта К1, предложенный ИИ-анализом суток.
    /// Формат строки от сервера (AiService AnalyzeResponse.SuggestExcludeFromReports и
    /// FlaggedParts): PartName§SetupNumber§Order§Причина[§#N]; старый трёхсегментный
    /// формат без причины — откат на общее объяснение по суткам (fallbackReason).
    /// Хвостовой «#N» — порядковый номер строки в сутках (с сервера): внутри дня
    /// бывает несколько строк с ОДНИМ именем/установкой/заказом (работа разбита
    /// на отрезки, например ночная и дневная), и без номера привязка уходила
    /// на первую по списку — исключалась не та строка (кейс 30.09.2026, SKT21
    /// 25.09 «Винт…»: проблемная строка вторая, исключили первую).
    /// </summary>
    public record AiExcludeSuggestion(string PartName, int Setup, string Order, string Reason, int? Ordinal = null)
    {
        /// <summary> Хвостовой дискриминатор «#N» (net6 — без source-generated regex).</summary>
        private static readonly Regex OrdinalPattern = new(@"^#\d{1,3}$", RegexOptions.Compiled);

        /// <summary> Разбирает одну строку предложения (с необязательным хвостом «#N»).</summary>
        public static bool TryParse(string? entry, string? fallbackReason, out AiExcludeSuggestion? suggestion)
        {
            suggestion = null;
            if (string.IsNullOrWhiteSpace(entry)) return false;

            var parts = entry!.Split('§');
            if (parts.Length < 3) return false;

            if (!int.TryParse(parts[1], out var setupNumber)) return false;

            var tail = parts.Length > 3 ? new List<string>(parts.Skip(3)) : new List<string>();
            int? ordinal = null;
            if (tail.Count > 0 && OrdinalPattern.IsMatch(tail[tail.Count - 1].Trim())
                && int.TryParse(tail[tail.Count - 1].Trim().TrimStart('#'), out var parsed))
            {
                ordinal = parsed;
                tail.RemoveAt(tail.Count - 1);
            }

            var reason = tail.Count > 0 ? string.Join("§", tail).Trim() : string.Empty;
            if (string.IsNullOrEmpty(reason)) reason = fallbackReason ?? string.Empty;

            suggestion = new AiExcludeSuggestion(parts[0], setupNumber, parts[2], reason, ordinal);
            return true;
        }

        /// <summary>Разбирает весь список предложений, мусор пропускает.</summary>
        public static List<AiExcludeSuggestion> ParseMany(IEnumerable<string>? entries, string? fallbackReason)
        {
            var result = new List<AiExcludeSuggestion>();
            if (entries == null) return result;

            foreach (var entry in entries)
            {
                if (TryParse(entry, fallbackReason, out var suggestion) && suggestion != null)
                    result.Add(suggestion);
            }

            return result;
        }

        /// <summary>
        /// Привязывает предложение к строке суток. При наличии номера «#N» — строка
        /// по порядку (с проверкой имени/установки/заказа, при расхождении падаем
        /// на общий поиск). Без номера: точное совпадение (имя + установка + заказ),
        /// но ТОЛЬКО если оно единственное — при двух и более строках с одинаковым
        /// ключом вернуть null (однозначно не выбрать, а排除 первую неверно:
        /// кейс 30.09.2026 SKT21 25.09 «Винт…»). Далее нормализованное имя
        /// (без скобочных комментариев, регистра и лишних пробелов — модель
        /// регулярно режет суффиксы вида «(производство)»). Нормализованное
        /// совпадение принимается только при единственном кандидате — иначе
        /// неоднозначность (та же деталь в обеих сменах), возвращается null.
        /// </summary>
        public static Part? FindRow(IEnumerable<Part> rows, AiExcludeSuggestion s)
        {
            var list = rows as IList<Part> ?? rows.ToList();
            if (s.Ordinal is int ordinal && ordinal >= 1 && ordinal <= list.Count)
            {
                var byIndex = list[ordinal - 1];
                if (Matches(byIndex, s)) return byIndex;
            }

            var exact = list.Where(p => Matches(p, s)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1) return null;

            var norm = s.PartName.NormalizedPartNameWithoutComments();
            if (string.IsNullOrEmpty(norm)) return null;

            var candidates = list.Where(p =>
                p.Setup == s.Setup &&
                p.Order == s.Order &&
                p.PartName.NormalizedPartNameWithoutComments() == norm).ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static bool Matches(Part p, AiExcludeSuggestion s) =>
            p.PartName == s.PartName && p.Setup == s.Setup && p.Order == s.Order;
    }
}
