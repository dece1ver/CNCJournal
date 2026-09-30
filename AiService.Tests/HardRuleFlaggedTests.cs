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
        Assert.Contains("Деталь§2§З1§#1", result.HardFlaggedPartKeys);
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

        Assert.Contains("Деталь§1§З1§#1", result.HardFlaggedPartKeys);
    }

    [Fact]
    public void Evaluate_BadFillReason_HardWithDataMessage()
    {
        // «Некорректное заполнение» — данные недостоверны: безусловная эскалация
        // с формулировкой про исправление данных, а не про технологию
        // (пилот агента 24.09.2026, SKT21 №104 23.09 — модель сняла такой день).
        var req = Request(new PartContext
        {
            PartName = "Седло",
            Order = "З1",
            Setup = 2,
            SetupTimePlan = 29,
            SetupTimeFact = 0,
            SingleProductionTimePlan = 7,
            MasterSetupComment = "Некорректное заполнение",
        });

        var result = HardRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("недостоверны"));
        Assert.DoesNotContain(result.HardSignals, s => s.Contains("пересмотра технологии"));
        Assert.Contains("Седло§2§З1§#1", result.HardFlaggedPartKeys);
    }

    private static PartContext LowKpdPart() => new()
    {
        PartName = "Деталь",
        Order = "З1",
        Setup = 1,
        SetupTimePlan = 0,
        SetupTimeFact = 50,
        SingleProductionTimePlan = 5,
        ProductionTimeFact = 100,
        ProductionRatio = 0.4,
        FinishedCount = 10,
    };

    [Fact]
    public void Evaluate_NonSerial_SkipsProductionKpd()
    {
        // Несерийный станок: КПД изготовления не оценивается ни в одном контуре.
        var req = Request(LowKpdPart());
        req.IsSerialMachine = false;

        var result = HardRuleEvaluator.Evaluate(req);

        Assert.DoesNotContain(result.HardSignals, s => s.Contains("изготовления"));
    }

    [Fact]
    public void Evaluate_NonSerial_KeepsSetupAndNormatives()
    {
        // Остальное в силе: наладка, plan=0, машинное время — как обычно.
        var req = Request(LowKpdPart());
        req.IsSerialMachine = false;

        var result = HardRuleEvaluator.Evaluate(req);

        Assert.Contains(result.HardSignals, s => s.Contains("наладки"));
    }

    [Fact]
    public void Evaluate_SerialOrUnknown_Unchanged()
    {
        // Серийный и неизвестный (null/старый клиент): обе КПД-проверки на месте.
        foreach (bool? flag in new bool?[] { true, null })
        {
            var req = Request(LowKpdPart());
            req.IsSerialMachine = flag;

            var result = HardRuleEvaluator.Evaluate(req);

            Assert.Contains(result.HardSignals, s => s.Contains("КПД изготовления 40%"));
            Assert.Contains(result.HardSignals, s => s.Contains("норматив наладки"));
        }
    }

    private static PartContext OperatorPart(string operatorComment) => new()
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
        OperatorComment = operatorComment,
    };

    [Fact]
    public void Evaluate_OperatorTechnologyDemand_Hard()
    {
        // Явное требование оператора проверить технологию — hard без объяснений мастера.
        var result = HardRuleEvaluator.Evaluate(Request(OperatorPart("требуется проверка технологии")));

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals,
            s => s.Contains("Оператор сообщает о некорректном нормативе"));
    }

    [Fact]
    public void Evaluate_OperatorTechnologyDoubt_SoftWithMasterReason()
    {
        // Та же жалоба, но мастер дал причину — soft на верификацию модели.
        var part = OperatorPart("технология неверная, надо проверить");
        part.MasterMachiningComment = "Другое";
        part.MasterMachiningDetail = "смотрели с технологом";

        var result = HardRuleEvaluator.Evaluate(Request(part));

        Assert.False(result.MustEscalate);
        Assert.Single(result.SoftSignals);
    }

    [Fact]
    public void Evaluate_BareTechnologyMention_NoSignal()
    {
        // Голое «технология» без дефекта — не жалоба (не расширяем слишком широко).
        var result = HardRuleEvaluator.Evaluate(Request(OperatorPart("работали по технологии")));

        Assert.False(result.MustEscalate);
        Assert.Empty(result.SoftSignals);
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

        Assert.Equal(["Гильза АР156.1-125-01-301М8Л§1§З1§#1"], flagged);
    }

    [Fact]
    public void Evaluate_MachiningTime_ClientSignalPresent_NoDuplicate()
    {
        // Кейс 30.09.2026, SKT21 27.09: клиент уже прислал «Машинное время 4.7мин
        // >= штучного норматива 4мин (117%)» — серверный дубль «…(118%)» в ответе
        // только шумит. Обратный случай (сигнала клиента нет) проверяется рядом.
        var part = new PartContext
        {
            PartName = "Седло",
            Order = "З1",
            Setup = 1,
            MachiningTime = 4.7,
            SingleProductionTimePlan = 4,
            ProductionRatio = 0.53,
            FinishedCount = 17,
        };
        part.Signals.Add("Машинное время 4.7мин >= штучного норматива 4мин (117%)");

        var result = HardRuleEvaluator.Evaluate(Request(part));

        Assert.DoesNotContain(result.HardSignals, s => s.Contains("Машинное время"));
        Assert.DoesNotContain(result.SoftSignals, s => s.Contains("Машинное время"));
    }

    [Fact]
    public void Evaluate_MachiningTime_NoClientSignal_HardFires()
    {
        // Клиентского сигнала нет (например, при КПД изготовления > 100% клиент
        // его подавляет) — серверный сигнал остаётся, иначе аномалия потеряется.
        var part = new PartContext
        {
            PartName = "Седло",
            Order = "З1",
            Setup = 1,
            MachiningTime = 4.7,
            SingleProductionTimePlan = 4,
            ProductionRatio = 0.53,
            FinishedCount = 17,
        };

        var result = HardRuleEvaluator.Evaluate(Request(part));

        Assert.Contains(result.HardSignals, s => s.Contains("Машинное время"));
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
    public void CollectFlaggedPartKeys_AmbiguousName_OnlyEvidenceRow()
    {
        // Одно имя на двух установках: флагуется та строка, где есть доказательства
        // (клиентский сигнал), а не обе и не первая (жалоба 30.09.2026: по флагу
        // на каждую строку дубля аналитику прилетало лишнее).
        var withSignal = new PartContext { PartName = "Втулка", Order = "З1", Setup = 1 };
        withSignal.Signals.Add("КПД наладки 45% без объяснения мастера");
        var req = Request(withSignal, new PartContext { PartName = "Втулка", Order = "З1", Setup = 2 });

        var flagged = AnalysisController.CollectFlaggedPartKeys(
            new HardRuleResult([], []), [], req,
            ["Втулка: КПД наладки 45% ниже нормы"]);

        Assert.Equal(["Втулка§1§З1§#1"], flagged);
    }

    [Fact]
    public void CollectFlaggedPartKeys_NoEvidence_NotFlagged()
    {
        // Строки-дубли без единой улики: флаг не ставим ни одной (текст сигнала
        // аналитику всё равно виден в диалоге).
        var req = Request(
            new PartContext { PartName = "Втулка", Order = "З1", Setup = 1 },
            new PartContext { PartName = "Втулка", Order = "З1", Setup = 2 });

        var flagged = AnalysisController.CollectFlaggedPartKeys(
            new HardRuleResult([], []), [], req,
            ["Втулка: КПД наладки 45% ниже нормы"]);

        Assert.Empty(flagged);
    }

    [Fact]
    public void CollectFlaggedPartKeys_DuplicateKey_OneRowPerHardSignal()
    {
        // Кейс 30.09.2026, SKT21 27.09: hard-сигнал приходит со своей строкой,
        // поэтому ключ флага точный — вторая строка-дубль не флагуется.
        PartContext Row(string reason) => new()
        {
            PartName = "Винт позиционирующий 1.АР0001-001 ТОС3",
            Order = "ФЛ2609-0029.3.1",
            Setup = 1,
            SetupTimePlan = 86,
            SetupTimeFact = 101,
            SetupRatio = 0.85,
            SingleProductionTimePlan = 15,
            NoProductionHappened = true,
            MasterMachiningComment = reason,
        };
        var req = Request(Row(""), Row("Некорректные нормативы"));

        var hard = HardRuleEvaluator.Evaluate(req);
        var flagged = AnalysisController.CollectFlaggedPartKeys(
            hard, hard.SoftSignals.ToList(), req, []);

        Assert.Equal(["Винт позиционирующий 1.АР0001-001 ТОС3§1§ФЛ2609-0029.3.1§#2"], flagged);
    }
}
