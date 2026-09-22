using AiService.Models;
using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Пост-фильтр галлюцинаций в data-сигналах. Здесь — правило plan=0 без заказа:
/// строка без заказа (Order пустой или «Без М/Л») с нулевым планом — норма,
/// сомнения модели режутся (кейс 21.09.2026 Rontek HTC420 №2).
/// </summary>
public class FalsePositiveFilterTests
{
    private static AnalyzeRequest Request(params PartContext[] parts) => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-21",
        Parts = [.. parts],
    };

    private static readonly HardRuleResult NoHard = new([], []);
    private static readonly List<string> NoDowngraded = [];

    private static PartContext NoOrderPart() => new()
    {
        PartName = "Втулка",
        Order = "",
        Setup = 1,
        SetupTimePlan = 0,
        SingleProductionTimePlan = 0,
    };

    [Fact]
    public void NoOrderPlan0_NamedPart_RemovedAndReset()
    {
        var (filtered, reset, removed) = FalsePositiveFilter.Apply(
            Request(NoOrderPart()), NoHard, NoDowngraded,
            ["[Втулка] plan=0 без заказа — требует проверки"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
        Assert.True(reset);
    }

    [Fact]
    public void OrderedPlan0_Kept()
    {
        // Строка с заказом: plan=0 — недоработка нормирования, детерминированный hard.
        // (SetupTimeFact задан, чтобы не сработало б/н-правило на дефолтных нулях.)
        var part = NoOrderPart();
        part.Order = "З1";
        part.SetupTimeFact = 50;
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(part), NoHard, NoDowngraded,
            ["[Втулка] отсутствует норматив наладки"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    [Fact]
    public void MixedDay_UnnamedClaim_Kept()
    {
        // Безымянная жалоба при смешанном дне может относиться к строке
        // с заказом — консервативно сохраняем.
        var ordered = NoOrderPart();
        ordered.Order = "З1";
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(NoOrderPart(), ordered), NoHard, NoDowngraded,
            ["plan=0 без заказа — требует проверки"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    [Fact]
    public void BezMl_NamedPart_Removed()
    {
        var part = NoOrderPart();
        part.Order = "Без М/Л";
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(part), NoHard, NoDowngraded,
            ["[Втулка] план=0, норматив отсутствует"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
    }
}
