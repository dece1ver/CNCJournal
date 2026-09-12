using System;
using System.Collections.Generic;

namespace remeLog.Core.Services
{
    /// <summary>
    /// Кандидат на исключение записи из расчёта К1, предложенный ИИ-анализом суток.
    /// Формат строки от сервера (AiService AnalyzeResponse.SuggestExcludeFromReports):
    /// PartName§SetupNumber§Order§Причина; старый трёхсегментный формат без причины —
    /// откат на общее объяснение по суткам (fallbackReason).
    /// </summary>
    public record AiExcludeSuggestion(string PartName, int Setup, string Order, string Reason)
    {
        /// <summary>
        /// Разбирает одну строку предложения. Возвращает false для мусора
        /// (меньше 3 сегментов, нечисловой номер установки, пустая строка) —
        /// такие записи молча пропускаются, как раньше в UI.
        /// </summary>
        public static bool TryParse(string? entry, string? fallbackReason, out AiExcludeSuggestion? suggestion)
        {
            suggestion = null;
            if (string.IsNullOrWhiteSpace(entry)) return false;

            var parts = entry!.Split('§');
            if (parts.Length < 3) return false;

            if (!int.TryParse(parts[1], out var setupNumber)) return false;

            var reason = parts.Length > 3 ? string.Join("§", parts[3..]).Trim() : string.Empty;
            if (string.IsNullOrEmpty(reason)) reason = fallbackReason ?? string.Empty;

            suggestion = new AiExcludeSuggestion(parts[0], setupNumber, parts[2], reason);
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
    }
}
