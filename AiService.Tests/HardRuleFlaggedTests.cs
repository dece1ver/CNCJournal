using AiService.Controllers;
using AiService.Models;
using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Ключи строк для флагов СГТ (FlaggedParts): hard-сигналы флагуют всегда,
/// soft — только если модель их не понизила. Ключ точный: PartName§Setup§Order.
/// </summary>
public class HardRuleFlaggedTests
{
    private static AnalyzeRequest Request(params PartContext[] parts) => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-12",
        Parts = [.. parts],
    };

    [Fact]
    public void Evaluate_NoSetupNormative_FlagsExactKey()
    {
        var req = Request(new PartContext
        {
            PartName = "Деталь",
            Order = "З1",
            Setup = 2,
            SetupTimePlan = 0,
            SingleProductionTimePlan = 5,
            MasterSetupComment = "",
        });

        var result = HardRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains("Деталь§2§З1", result.HardFlaggedPartKeys);
    }

    [Fact]
    public void Evaluate_EscalationReason_FlagsKey()
    {
        var req = Request(new PartContext
        {
            PartName = "Деталь",
            Order = "З1",
            Setup = 1,
            SetupTimePlan = 60,
            SetupTimeFact = 50,
            SingleProductionTimePlan = 5,
            MasterSetupComment = "Некорректные нормативы",
        });

        var result = HardRuleEvaluator.Evaluate(req);

        Assert.Contains("Деталь§1§З1", result.HardFlaggedPartKeys);
    }

    [Fact]
    public void Evaluate_CleanPart_NoKeys()
    {
        var req = Request(new PartContext
        {
            PartName = "Деталь",
            Order = "З1",
            Setup = 1,
            SetupTimePlan = 60,
            SetupTimeFact = 50,
            SetupRatio = 1.2,
            SingleProductionTimePlan = 5,
            ProductionTimeFact = 100,
            ProductionRatio = 1,
            FinishedCount = 10,
            MasterSetupComment = "Неопытный оператор",
            MasterMachiningComment = "Неопытный оператор",
        });

        var result = HardRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
        Assert.Empty(result.HardFlaggedPartKeys);
        Assert.Empty(result.SoftFlagged);
    }

    [Fact]
    public void CollectFlaggedPartKeys_HardAlwaysIncluded_SoftOnlyWhenSurvived()
    {
        var hardRules = new HardRuleResult([], [])
        {
            HardFlaggedPartKeys = ["A§1§З1"],
            SoftFlagged =
            [
                ("soft про наладку", "B§1§З2"),
                ("soft про изготовление", "C§1§З3"),
            ],
        };

        var flagged = AnalysisController.CollectFlaggedPartKeys(
            hardRules, ["soft про изготовление"], new AnalyzeRequest(), []);

        Assert.Contains("A§1§З1", flagged); // hard — всегда
        Assert.Contains("C§1§З3", flagged); // soft уцелел
        Assert.DoesNotContain("B§1§З2", flagged); // soft понижен моделью
    }

    [Fact]
    public void CollectFlaggedPartKeys_EmptyWhenNothingSurvived()
    {
        var hardRules = new HardRuleResult([], [])
        {
            SoftFlagged = [("soft", "A§1§З1")],
        };

        Assert.Empty(AnalysisController.CollectFlaggedPartKeys(hardRules, [], new AnalyzeRequest(), []));
    }

    [Fact]
    public void CollectFlaggedPartKeys_ModelNamedPart_Flagged()
    {
        // Кейс 22.09.2026 (Mazak 04.09): модель пишет про Гильзу, но hard/soft
        // молчат — строка всё равно подсвечивается.
        var req = Request(new PartContext
        {
            PartName = "Гильза АР156.1-125-01-301М8Л",
            Order = "З1",
            Setup = 1,
        });

        var flagged = AnalysisController.CollectFlaggedPartKeys(
            new HardRuleResult([], []), [], req,
            ["Гильза АР156.1-125-01-301М8Л: низкий КПД изготовления (33%) без достаточной детализации причины «Другое»"]);

        Assert.Contains("Гильза АР156.1-125-01-301М8Л§1§З1", flagged);
    }

    [Fact]
    public void CollectFlaggedPartKeys_UnknownName_Ignored()
    {
        // Выдуманное имя ни с чем не сопоставляется — галлюцинация не флагует.
        var req = Request(new PartContext
        {
            PartName = "Втулка",
            Order = "З1",
            Setup = 1,
        });

        var flagged = AnalysisController.CollectFlaggedPartKeys(
            new HardRuleResult([], []), [], req,
            ["Несуществующая деталь: низкий КПД"]);

        Assert.Empty(flagged);
    }

    [Fact]
    public void CollectFlaggedPartKeys_AmbiguousName_FlagsAllRows()
    {
        // Одно имя на двух установках — флагуются обе, лишнее снимет аналитик.
        var req = Request(
            new PartContext { PartName = "Втулка", Order = "З1", Setup = 1 },
            new PartContext { PartName = "Втулка", Order = "З1", Setup = 2 });

        var flagged = AnalysisController.CollectFlaggedPartKeys(
            new HardRuleResult([], []), [], req,
            ["Втулка: КПД наладки 45% ниже нормы"]);

        Assert.Contains("Втулка§1§З1", flagged);
        Assert.Contains("Втулка§2§З1", flagged);
    }
}
