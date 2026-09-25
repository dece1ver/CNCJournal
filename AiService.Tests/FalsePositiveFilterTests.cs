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

    [Fact]
    public void NoOrderPlan0_PrefixNameCollision_Removed()
    {
        // Кейс QTS350 2026-09-24: имя «Кольцо …» — префикс имени
        // «Кольцо … расточка кулачков» (Без М/Л, plan=0). Сигнал «plan=0
        // при наличии заказа» относится к Без М/Л-строке и обязан резаться;
        // generic-совпадения со строками с заказом — артефакт подстроки.
        PartContext Part(string name, string order, int plan) => new()
        {
            PartName = name,
            Order = order,
            Setup = 1,
            SetupTimePlan = plan,
            SetupTimeFact = plan > 0 ? plan : 64,
            SingleProductionTimePlan = plan > 0 ? 30 : 0,
        };
        var (filtered, reset, removed) = FalsePositiveFilter.Apply(
            Request(
                Part("Кольцо АРМ2-49.3-01-041", "УЧ2608-0028.1.1", 86),
                Part("Кольцо АРМ2-49.3-01-041 расточка кулачков", "Без М/Л", 0),
                Part("Кольцо АРМ2-49.3-01-041", "УЧ2608-0028.1.1", 86)),
            NoHard, NoDowngraded,
            ["plan=0 при наличии заказа для «Кольцо АРМ2-49.3-01-041 расточка кулачков»"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
        Assert.True(reset);
    }

    [Fact]
    public void AbsentSetup_AmbiguousName_FactPositivePart_Removed()
    {
        // Кейс QTS350 2026-09-24 (второй заход): «Отсутствие наладки при наличии
        // заказа (Кольцо…, уст 1)» матчит ДВЕ одноимённые строки — б/н (план 86)
        // и нормальную (факт 70). Претензия неверна про обе: про первую запрещена
        // (такой категории нет), про вторую фактически ложна → режется.
        var bn = new PartContext
        {
            PartName = "Кольцо", Order = "З1", Setup = 1,
            NoSetupHappened = true, SetupTimePlan = 86, SetupTimeFact = 0,
            SingleProductionTimePlan = 30,
        };
        var ok = new PartContext
        {
            PartName = "Кольцо", Order = "З1", Setup = 1,
            SetupRatio = 1.2286, SetupTimePlan = 86, SetupTimeFact = 70,
            SingleProductionTimePlan = 30,
        };
        var (filtered, reset, removed) = FalsePositiveFilter.Apply(
            Request(bn, ok), NoHard, NoDowngraded,
            ["Отсутствие наладки при наличии заказа (Кольцо, уст 1)"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
        Assert.True(reset);
    }

    [Fact]
    public void AbsentProduction_FinishedPart_Removed()
    {
        // Зеркало для изготовления: «не выполнено» про строку с фактом/готовыми —
        // фактически ложно; про б/и-строку — запрещённая категория. Режется.
        var bi = new PartContext
        {
            PartName = "Втулка", Order = "З1", Setup = 1,
            NoProductionHappened = true, SingleProductionTimePlan = 5,
        };
        var ok = new PartContext
        {
            PartName = "Втулка", Order = "З1", Setup = 1,
            ProductionRatio = 0.9, ProductionTimeFact = 40, FinishedCount = 5,
            SingleProductionTimePlan = 5,
        };
        var (filtered, reset, removed) = FalsePositiveFilter.Apply(
            Request(bi, ok), NoHard, NoDowngraded,
            ["Изготовление не выполнено при наличии заказа (Втулка, уст 1)"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
        Assert.True(reset);
    }

    [Fact]
    public void NormativeAbsenceClaim_WithFact_Kept()
    {
        // Страж от регрессии: «отсутствует норматив» — не про отсутствие работы,
        // даже с фактом. Недоработка нормирования, решает hard, не этот фильтр.
        var part = new PartContext
        {
            PartName = "Гильза", Order = "З1", Setup = 1,
            SetupRatio = 0.5, SetupTimePlan = 0, SetupTimeFact = 56,
            SingleProductionTimePlan = 0,
        };
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(part), NoHard, NoDowngraded,
            ["[Гильза] отсутствует норматив наладки"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    private static PartContext NoSetupOrderedPart() => new()
    {
        PartName = "Гильза",
        Order = "З1",
        Setup = 1,
        SetupTimePlan = 114,
        SetupTimeFact = 0,
        NoSetupHappened = true,
        ProductionRatio = 1.0,
        FinishedCount = 10,
        SingleProductionTimePlan = 5,
    };

    [Fact]
    public void ForbiddenBnCategory_NamedNoSetupPart_Removed()
    {
        // Запрещённая категория «наладка не выполнена при наличии заказа» —
        // б/н при заказе НЕ аномалия (кейс Goodway 2026-09-23, A/B Integrex 2026-09-21).
        // Все формулировки модели обязаны гаснуть, заказ значения не имеет.
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(NoSetupOrderedPart()), NoHard, NoDowngraded,
            [
                "[Гильза] наладка не выполнена при наличии заказа",
                "[Гильза] отсутствие наладки при наличии норматива",
                "[Гильза] КПД наладки вне нормы (б/н), не объяснено",
            ]);

        Assert.Empty(filtered);
        Assert.Equal(3, removed.Count);
    }

    [Fact]
    public void ForbiddenBnCategory_RealSetupPart_Kept()
    {
        // Та же формулировка, но наладка реально была — сигнал сохраняется.
        var part = NoSetupOrderedPart();
        part.NoSetupHappened = false;
        part.SetupTimeFact = 60;
        part.SetupRatio = 0.5;
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(part), NoHard, NoDowngraded,
            ["[Гильза] наладка не выполнена при наличии заказа"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    [Fact]
    public void AbsentSetup_NoFactParaphrase_Removed()
    {
        // Кейс QTS350 2026-09-24 (v14): «план наладки=86мин при наличии заказа,
        // но факта нет» — та же запрещённая категория новыми словами.
        // Привязка двусмысленна (б/н-строка + строка с фактом 70) → режется.
        var bn = new PartContext
        {
            PartName = "Кольцо", Order = "З1", Setup = 1,
            NoSetupHappened = true, SetupTimePlan = 86, SetupTimeFact = 0,
            SingleProductionTimePlan = 30,
        };
        var ok = new PartContext
        {
            PartName = "Кольцо", Order = "З1", Setup = 1,
            SetupRatio = 1.2286, SetupTimePlan = 86, SetupTimeFact = 70,
            SingleProductionTimePlan = 30,
        };
        var (filtered, reset, removed) = FalsePositiveFilter.Apply(
            Request(bn, ok), NoHard, NoDowngraded,
            ["план наладки=86мин при наличии заказа, но факта нет"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
        Assert.True(reset);
    }

    [Fact]
    public void PositiveFinishedCount_NotRemoved()
    {
        // Позитивное «выполнено N шт» про деталь с реальной наладкой —
        // новые ключевые формы («выполнена/выполнено») его не задевают.
        var part = NoSetupOrderedPart();
        part.NoSetupHappened = false;
        part.SetupTimeFact = 60;
        part.SetupRatio = 0.5;
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(part), NoHard, NoDowngraded,
            ["[Гильза] КПД наладки 50%, выполнено 10 шт"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    [Fact]
    public void VagueOutOfNormClaim_NamedNoSetupPart_Removed()
    {
        // Расплывчатое «вне нормы» без чисел про б/н-деталь (пилот 24.09.2026:
        // «КПД наладки вне нормы» при setupRatio=null) — та же галлюцинация.
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(NoSetupOrderedPart()), NoHard, NoDowngraded,
            ["[Гильза] КПД наладки вне нормы"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
    }

    [Fact]
    public void NumericOutOfNormClaim_RealSetupPart_Kept()
    {
        // Конкретное значение вне диапазона по реальной наладке — не задевается:
        // у претензии есть число, у детали — факт работы.
        var part = NoSetupOrderedPart();
        part.NoSetupHappened = false;
        part.SetupTimeFact = 200;
        part.SetupTimePlan = 100;
        part.SetupRatio = 0.5;
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(part), NoHard, NoDowngraded,
            ["[Гильза] КПД наладки 50% вне нормы, объяснение сомнительно"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    [Fact]
    public void IsIncoherentOk_SignalsWithoutReview_True()
    {
        // Пилот 24.09.2026 (Goodway 01.06): модель перечислила необъяснённые
        // проблемы, но вердикт False — противоречие, поднимаем в True.
        Assert.True(FalsePositiveFilter.IsIncoherentOk(false, ["КПД изготовления=67%"]));
        Assert.False(FalsePositiveFilter.IsIncoherentOk(true, ["КПД изготовления=67%"]));
        Assert.False(FalsePositiveFilter.IsIncoherentOk(false, []));
    }

    private static PartContext DrugoePart() => new()
    {
        PartName = "Корпус проточной части",
        Order = "З1",
        Setup = 1,
        SetupTimePlan = 57,
        SetupTimeFact = 270,
        SetupRatio = 0.21,
        MasterSetupComment = "Другое",
        MasterSetupDetail = "Вмятины по диаметру. Реставрация.",
    };

    private static PartContext BareDrugoePart() => new()
    {
        PartName = "Втулка",
        Order = "З1",
        Setup = 1,
        SetupTimePlan = 60,
        SetupTimeFact = 100,
        SetupRatio = 0.6,
        MasterSetupComment = "Другое",
        MasterSetupDetail = "",
        MasterComment = "",
    };

    [Fact]
    public void DrugoeWithDetail_NamedClaim_Removed()
    {
        // Пилот 25.09 (QTS350 23.09): «без подтверждения освоения» при детальном
        // «Другом» — детализация есть, жалоба неверна фактом.
        var (filtered, reset, removed) = FalsePositiveFilter.Apply(
            Request(DrugoePart()), NoHard, NoDowngraded,
            ["[Корпус проточной части] КПД наладки=21% без подтверждения освоения"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
        Assert.True(reset);
    }

    [Fact]
    public void DrugoeBareDetail_Claim_Kept()
    {
        // Детализации правда нет — жалобе есть на что указывать.
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(BareDrugoePart()), NoHard, NoDowngraded,
            ["[Втулка] КПД наладки 60% без детализации причины «Другое»"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    [Fact]
    public void DrugoeUnnamed_MixedDay_Kept()
    {
        // Безымянная жалоба при смешанном дне: у одной строки детализация есть,
        // у другой нет — может относиться к пустой, сохраняем.
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(DrugoePart(), BareDrugoePart()), NoHard, NoDowngraded,
            ["КПД наладки вне нормы без детализации причины «Другое»"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    private static PartContext DetailedOtherPart() => new()
    {
        PartName = "Корпус проточной части",
        Order = "З1",
        Setup = 1,
        SetupTimePlan = 57,
        SetupTimeFact = 270,
        SetupRatio = 0.21,
        MasterSetupComment = "Другое",
        MasterSetupDetail = "Вмятины по диаметру. Реставрация.",
    };

    [Fact]
    public void MisaddressedDemand_NumberAnchored_Removed()
    {
        // Пилот 25.09 (QTS350 23.09, флип): «без подтверждения освоения/заготовок»,
        // хотя выбрано детальное «Другое»; число 21% привязывает к строке.
        var (filtered, reset, removed) = FalsePositiveFilter.Apply(
            Request(DetailedOtherPart()), NoHard, NoDowngraded,
            ["КПД наладки=21% (ниже нормы) без подтверждения освоения/некорректных заготовок"]);

        Assert.Empty(filtered);
        Assert.Single(removed);
        Assert.True(reset);
    }

    [Fact]
    public void MisaddressedDemand_CorrectAddressee_Kept()
    {
        // Требование относится к выбранной причине — сохраняем.
        var part = DetailedOtherPart();
        part.MasterSetupComment = "Освоение";
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(part), NoHard, NoDowngraded,
            ["КПД наладки=21% без подтверждения освоения"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    [Fact]
    public void MisaddressedDemand_EmptyReasonRow_Kept()
    {
        // У второй строки причины нет вообще — требование может относиться к ней.
        // Число 21% привязывает только первую строку... поэтому здесь БЕЗ числа:
        // безымянно и без чисел, при пустой строке — сохраняем.
        var bare = BareDrugoePart();
        bare.MasterSetupComment = "";
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(DetailedOtherPart(), bare), NoHard, NoDowngraded,
            ["КПД наладки вне нормы без подтверждения освоения"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }

    private static AnalyzeRequest NonSerialRequest(params PartContext[] parts) => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-21",
        Parts = [.. parts],
        IsSerialMachine = false,
    };

    [Fact]
    public void NonSerial_ProductionKpdClaim_Removed()
    {
        // Несерийный станок: любые претензии к КПД изготовления режутся
        // (с числами и без); машинное время и нормативы не задеваются.
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            NonSerialRequest(), NoHard, NoDowngraded,
            ["[Гильза] КПД изготовления 44% ниже нормы",
             "[Гильза] КПД изготовления вне нормы",
             "[Гильза] Машинное время 5мин >= норматива 5мин (100%)"]);

        Assert.Single(filtered);
        Assert.Equal(2, removed.Count);
    }

    [Fact]
    public void Serial_ProductionKpdClaim_Kept()
    {
        // Серийный (IsSerialMachine=null, старый клиент): правило не действует.
        var (filtered, _, removed) = FalsePositiveFilter.Apply(
            Request(), NoHard, NoDowngraded,
            ["[Гильза] КПД изготовления 44% ниже нормы"]);

        Assert.Single(filtered);
        Assert.Empty(removed);
    }
}
