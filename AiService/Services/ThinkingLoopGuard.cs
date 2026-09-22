using System.Text.RegularExpressions;

namespace AiService.Services;

/// <summary>
/// Маркер вырожденной генерации: модель зациклилась в рассуждении.
/// Ловится отдельно от TaskCanceledException — это не отмена пользователем,
/// а сбой модели: по fail-safe политике (как HasError) он эскалирует день,
/// а не роняет анализ. После одного ретрая с температурой повыше.
/// </summary>
public class DegenerateGenerationException(string message) : Exception(message);

/// <summary>
/// Детектор зацикливания в thinking-потоке. Смотрит хвост буфера: до 5 последних
/// предложений (короче 20 символов игнорируются, регистр/ё/пробелы нормализуются).
/// Петля — если предложений ≥3, различных ≤2 и последнее уже встречалось раньше
/// в окне («Однако…/Но…/Однако…» — классика; «Итак…/Итак…/Но…» — нет, модель
/// двинулась дальше). Мягко: легитимные рассуждения почти всегда разнообразнее.
/// </summary>
public static partial class ThinkingLoopGuard
{
    private const int MinSentenceLength = 20;
    private const int MinSentences = 3;
    private const int WindowSentences = 5;
    private const int MaxDistinct = 2;
    private const int TailLength = 600;

    public static bool IsLooping(string thinkBuffer)
    {
        if (string.IsNullOrEmpty(thinkBuffer)
            || thinkBuffer.Length < MinSentenceLength * MinSentences)
            return false;

        var tail = thinkBuffer.Length > TailLength
            ? thinkBuffer[^TailLength..]
            : thinkBuffer;

        var sentences = SplitSentences(tail)
            .Where(s => s.Length >= MinSentenceLength)
            .TakeLast(WindowSentences)
            .ToList();

        if (sentences.Count < MinSentences) return false;
        if (sentences.Distinct(StringComparer.Ordinal).Count() > MaxDistinct) return false;
        return sentences.SkipLast(1).Contains(sentences[^1], StringComparer.Ordinal);
    }

    private static List<string> SplitSentences(string text) =>
        [.. SentenceSplit().Split(text)
            .Select(s => SpaceCollapse().Replace(s.Trim().ToLowerInvariant(), " "))
            .Where(s => s.Length > 0)];

    [GeneratedRegex(@"[.!?…\n\r]+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceCollapse();
}
