using AiService.Models;

namespace AiService.Services;

// ═══ Свёртка сырых строк истории в PartsHistoryDto ═══
// Байт-в-байт математика клиента (AiServiceClient.SummarizePartsHistory +
// форматирование BuildPartContext): средний КПД по валидным (>0, конечные,
// округление до 2), сумма готовых, флаг необъяснённой низкой эффективности,
// null → «б/н»/«б/и», иначе «0%», решение по умолчанию «не проверено».
// Формулы КПД — зеркало Part.cs: наладка = план/факт, изготовление =
// готовоФакт*план/факт (готовоФакт = готово-1 при несовпадении времён начала).

/// <summary> Группировка строк по сменам в формат PartsHistoryDto. </summary>
public static class HistorySummarizer
{
    public static string FormatSetupRatio(double? ratio) =>
        ratio.HasValue ? ratio.Value.ToString("0%", System.Globalization.CultureInfo.InvariantCulture) : "б/н";

    public static string FormatProductionRatio(double? ratio) =>
        ratio.HasValue ? ratio.Value.ToString("0%", System.Globalization.CultureInfo.InvariantCulture) : "б/и";

    private static double? AverageValid(IEnumerable<double> ratios)
    {
        var valid = ratios.Where(r => !double.IsNaN(r) && !double.IsInfinity(r) && r > 0).ToList();
        return valid.Count == 0 ? null : Math.Round(valid.Average(), 2);
    }

    public static PartsHistoryDto Summarize(
        IReadOnlyList<HistoryRow> rows, IReadOnlyDictionary<DateTime, ReviewRow> reviews)
    {
        var dto = new PartsHistoryDto { RecordsFound = rows.Count };
        foreach (var group in rows.GroupBy(r => r.ShiftDate.Date).OrderByDescending(g => g.Key))
        {
            var list = group.ToList();
            reviews.TryGetValue(group.Key, out var review);
            dto.Lines.Add(new PartsHistoryLineDto
            {
                ShiftDate = group.Key.ToString("yyyy-MM-dd"),
                ProductionRatio = FormatProductionRatio(AverageValid(list.Select(r => r.ProductionRatio))),
                SetupRatio = FormatSetupRatio(AverageValid(list.Select(r => r.SetupRatio))),
                FinishedCount = (int)list.Sum(r => r.FinishedCount),
                AnalystDecision = review?.Decision ?? "не проверено",
                AnalystComment = review?.Comment,
                AiExplanation = review?.AiExplanation,
                AiFeedback = review?.AiFeedback,
                HasUnexplainedLowEfficiency = list.Any(r =>
                    !double.IsNaN(r.ProductionRatio) && !double.IsInfinity(r.ProductionRatio)
                    && r.ProductionRatio > 0 && r.ProductionRatio < 0.7
                    && string.IsNullOrWhiteSpace(r.EffectiveMachiningReason)),
            });
        }
        return dto;
    }

    /// <summary> КПД наладки строки Parts: план/факт (зеркало Part.SetupRatio). </summary>
    public static double SetupRatio(double plan, double fact) => plan / fact;

    /// <summary>
    /// КПД изготовления строки Parts (зеркало Part.ProductionRatio):
    /// готово>0 и готовоФакт==0 → NaN; иначе готовоФакт*план/факт.
    /// готовоФакт = готово-1 при несовпадении времён начала (зеркало
    /// Part.FinishedCountFact).
    /// </summary>
    public static double ProductionRatio(
        double finished, bool setupStartedSeparately, double plan, double fact)
    {
        var finishedFact = setupStartedSeparately && finished != 0 ? finished - 1 : finished;
        return finished > 0 && finishedFact == 0
            ? double.NaN
            : finishedFact * plan / fact;
    }
}
