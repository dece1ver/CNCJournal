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
            hardRules, ["soft про изготовление"]);

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

        Assert.Empty(AnalysisController.CollectFlaggedPartKeys(hardRules, []));
    }
}
