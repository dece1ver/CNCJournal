using System.Text.RegularExpressions;

namespace AiService.Services;

/// <summary>
/// Гигиена объяснения: модель регулярно перечисляет жёсткие правила текстом
/// («[Палец АР30-02-004 …] Причина…; …») вопреки требованию краткости в промпте —
/// тогда объяснение дублирует буллеты signals. Маркер перечисления — скобочные
/// ссылки на детали вида [Название…] (≥5 символов внутри): короткие маркеры
/// простоев [н]/[и] не задевает, кавычки «…» — тем более.
/// </summary>
public static partial class ExplanationHygiene
{
    public static bool EnumeratesRules(string? explanation) =>
        !string.IsNullOrWhiteSpace(explanation) && BracketSpan().IsMatch(explanation);

    [GeneratedRegex(@"\[[^\[\]]{5,}\]")]
    private static partial Regex BracketSpan();

    /// <summary>
    /// Протухшая ссылка: объяснение называет конкретные детали, но НИ ОДНА из них
    /// не встречается ни в одном выжившем сигнале, а хотя бы одна — в срезанных
    /// модельных сигналах. Модель писала текст по своему сырому signals, пост-фильтры
    /// его потом снесли (кейс 08.10.2026 QTS200ML 20.09: explanation про «Корпус
    /// верхний», чей сигнал «Другое с детализацией» срезан как объяснённый, а выжил
    /// только hard по «Крышке»). Сверка — по именам деталей запроса (точное вхождение,
    /// без стемминга): упоминание детали, которую вообще не флаговали (прошлые решения,
    /// история), не трогаем — только ссылку на срезанное.
    /// </summary>
    public static bool ReferencesOnlyRemovedParts(
        string? explanation,
        List<string> survivingSignals,
        List<string> removedSignals,
        List<string> partNames)
    {
        if (string.IsNullOrWhiteSpace(explanation))
            return false;
        var named = partNames
            .Where(n => !string.IsNullOrWhiteSpace(n)
                && explanation.Contains(n, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (named.Count == 0)
            return false;
        if (named.Any(n => survivingSignals.Any(s =>
            s.Contains(n, StringComparison.OrdinalIgnoreCase))))
            return false;
        return named.Any(n => removedSignals.Any(s =>
            s.Contains(n, StringComparison.OrdinalIgnoreCase)));
    }
}
