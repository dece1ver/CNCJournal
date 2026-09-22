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
}
