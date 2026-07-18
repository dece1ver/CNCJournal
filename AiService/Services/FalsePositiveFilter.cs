using AiService.Models;

namespace AiService.Services;

public static class FalsePositiveFilter
{
    private const string SetupMarker = "наладк";
    private const string ProductionMarker = "изготов";

    private static readonly string[] SetupKeywords =
    [
        "б/н",
        "отсутств",
        "не выполнен",
        "не проведен",
        "не была",
        "не было",
        "нулев",
        "0 мин",
        "0мин",
        "наладка без",
        "наладка не",
        "наладки не",
    ];

    private static readonly string[] ProductionKeywords =
    [
        "б/и",
        "отсутств",
        "не выполнен",
        "не проведен",
        "не было",
        "нулев",
        "изготовление без",
        "изготовление не",
        "изготовления не",
    ];

    /// <summary>
    /// Удаляет из ответа LLM сигналы, в которых модель галлюцинирует про
    /// "отсутствие наладки/изготовления требует объяснения" для частей,
    /// помеченных как б/н / б/и. Если после фильтрации не осталось ни одного
    /// основания для эскалации (hard rules, soft, клиентские сигналы),
    /// флаг <paramref name="shouldReset"/> устанавливается в true — вызывающий
    /// код принудительно сбрасывает requiresReview и очищает explanation/reason.
    /// </summary>
    public static (List<string> FilteredSignals, bool ShouldReset) Apply(
        AnalyzeRequest request,
        HardRuleResult hardRules,
        List<string> notDowngraded,
        List<string> llmSignals)
    {
        var hasNoSetupPart = request.Parts.Any(IsNoSetup);
        var hasNoProductionPart = request.Parts.Any(IsNoProduction);

        if (!hasNoSetupPart && !hasNoProductionPart)
            return (llmSignals, false);

        var filtered = new List<string>(llmSignals.Count);
        var removedAny = false;

        foreach (var signal in llmSignals)
        {
            var removed = false;

            if (hasNoSetupPart && IsNoSetupHallucination(signal))
                removed = true;
            else if (hasNoProductionPart && IsNoProductionHallucination(signal))
                removed = true;

            if (removed)
                removedAny = true;
            else
                filtered.Add(signal);
        }

        if (!removedAny)
            return (llmSignals, false);

        if (filtered.Count > 0)
            return (filtered, false);

        if (hardRules.MustEscalate)
            return (filtered, false);

        if (notDowngraded.Count > 0)
            return (filtered, false);

        if (request.Signals.Count > 0)
            return (filtered, false);

        if (request.Parts.Any(p => p.Signals.Count > 0))
            return (filtered, false);

        return (filtered, true);
    }

    private static bool IsNoSetup(PartContext p) =>
        p.NoSetupHappened
        || (p.SetupRatio == null && p.SetupTimeFact <= 0);

    private static bool IsNoProduction(PartContext p) =>
        p.NoProductionHappened
        || (p.ProductionRatio == null && p.FinishedCount <= 0);

    private static bool IsNoSetupHallucination(string signal)
    {
        var lower = signal.ToLowerInvariant();
        return lower.Contains(SetupMarker)
            && SetupKeywords.Any(kw => lower.Contains(kw));
    }

    private static bool IsNoProductionHallucination(string signal)
    {
        var lower = signal.ToLowerInvariant();
        return lower.Contains(ProductionMarker)
            && ProductionKeywords.Any(kw => lower.Contains(kw));
    }
}
