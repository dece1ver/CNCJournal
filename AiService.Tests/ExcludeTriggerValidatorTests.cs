using AiService.Models;
using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Привязка кандидатов на исключение и проверка триггерных причин.
/// Кейс 21.09: обрезанное имя + выдуманное «освоение» без отметок.
/// </summary>
public class ExcludeTriggerValidatorTests
{
    private static PartContext Part(
        string name = "Прокладка АРКП01-15-028 (производство)",
        int setup = 1,
        string order = "УЧ2608-0079.3.1",
        string setupComment = "",
        string machiningComment = "",
        string operatorComment = "") => new()
    {
        PartName = name,
        Order = order,
        Setup = setup,
        MasterSetupComment = setupComment,
        MasterMachiningComment = machiningComment,
        OperatorComment = operatorComment,
    };

    [Fact]
    public void NormalizeName_StripsParensCaseSpaces()
    {
        Assert.Equal("прокладка аркп01-15-028",
            ExcludeTriggerValidator.NormalizeName("Прокладка АРКП01-15-028 (производство)"));
        Assert.Equal("деталь 1",
            ExcludeTriggerValidator.NormalizeName("  Деталь   1  "));
        Assert.Equal("", ExcludeTriggerValidator.NormalizeName(null));
    }

    [Fact]
    public void FindPart_ExactMatch_Found()
    {
        var parts = new List<PartContext> { Part() };

        Assert.Same(parts[0], ExcludeTriggerValidator.FindPart(
            parts, "Прокладка АРКП01-15-028 (производство)", "1", "УЧ2608-0079.3.1"));
    }

    // ── Неоднозначный ключ: одна деталь/установка/заказ = несколько строк суток ──

    /// <summary> Кейс 30.09.2026, SKT21 25.09 «Винт…» и 24.09 «Гайка нажимная…»:
    /// строк две с одинаковым ключом, проблема — не в первой по списку. </summary>
    private static List<PartContext> DuplicateKeyDay() =>
    [
        new PartContext
        {
            PartName = "Гайка нажимная АР32-01-002 (Подготовка к отмене)",
            Order = "УЧ2609-0101.2.1", Setup = 1,
            SetupRatio = 1.0, SetupTimePlan = 86, SetupTimeFact = 86,
        },
        new PartContext
        {
            PartName = "Гайка нажимная АР32-01-002 (Подготовка к отмене)",
            Order = "УЧ2609-0101.2.1", Setup = 1,
            NoSetupHappened = true, SetupTimePlan = 57, SetupTimeFact = 0,
            PartialSetup = 3, SingleProductionTimePlan = 3,
        },
    ];

    [Fact]
    public void FindAllParts_DuplicateKey_ReturnsBothWithOrdinals()
    {
        var parts = DuplicateKeyDay();

        var found = ExcludeTriggerValidator.FindAllParts(
            parts, "Гайка нажимная АР32-01-002 (Подготовка к отмене)", "1", "УЧ2609-0101.2.1");

        Assert.Equal(2, found.Count);
        Assert.Equal((1, 0), (found[0].Ordinal, found[0].Part.PartialSetup));
        Assert.Equal((2, 3), (found[1].Ordinal, found[1].Part.PartialSetup));
        // FindPart на неоднозначном ключе строку не выбирает (раньше брал первую).
        Assert.Null(ExcludeTriggerValidator.FindPart(
            parts, "Гайка нажимная АР32-01-002 (Подготовка к отмене)", "1", "УЧ2609-0101.2.1"));
    }

    [Fact]
    public void PickRowForExclude_PartialSetupRow_Wins()
    {
        // «частичная наладка без объяснения» → вторая строка (частичная 3 мин),
        // а не первая (КПД 100%).
        var picked = ExcludeTriggerValidator.PickRowForExclude(
            ExcludeTriggerValidator.FindAllParts(
                DuplicateKeyDay(), "Гайка нажимная АР32-01-002 (Подготовка к отмене)",
                "1", "УЧ2609-0101.2.1"),
            "частичная наладка без объяснения");

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.Value.Ordinal);
    }

    [Fact]
    public void PickRowForExclude_TieOrNoEvidence_Null()
    {
        // Ничья (обе строки без улик) и «нулевой» кандидат — не привязываем вовсе:
        // галка на неверной строке хуже, чем кандидат без строки.
        var tie = ExcludeTriggerValidator.PickRowForExclude(
            ExcludeTriggerValidator.FindAllParts(
                DuplicateKeyDay(), "Гайка нажимная АР32-01-002 (Подготовка к отмене)",
                "1", "УЧ2609-0101.2.1"),
            "разовое изменение геометрии");
        Assert.Null(tie);
    }

    [Fact]
    public void WithOrdinal_And_ReasonOf_RoundTrip()
    {
        const string entry = "Гайка§1§З1§частичная наладка";

        var withOrdinal = ExcludeTriggerValidator.WithOrdinal(entry, 2);

        Assert.Equal("Гайка§1§З1§частичная наладка§#2", withOrdinal);
        Assert.Equal("частичная наладка", ExcludeTriggerValidator.ReasonOf(withOrdinal));
        // Повторный вызов не копит хвосты.
        Assert.Equal(withOrdinal, ExcludeTriggerValidator.WithOrdinal(withOrdinal, 2));
    }

    [Fact]
    public void FindPart_TrimmedName_Found()
    {
        var parts = new List<PartContext> { Part() };

        Assert.Same(parts[0], ExcludeTriggerValidator.FindPart(
            parts, "Прокладка АРКП01-15-028", "1", "УЧ2608-0079.3.1"));
    }

    [Fact]
    public void FindPart_Ambiguous_Null()
    {
        var parts = new List<PartContext>
        {
            Part("Деталь 1 (производство)"),
            Part("Деталь 1 (образец)"),
        };

        Assert.Null(ExcludeTriggerValidator.FindPart(parts, "Деталь 1", "1", "УЧ2608-0079.3.1"));
    }

    [Fact]
    public void HasGrounds_UnconfirmedMastering_False()
    {
        // Выдуманное «освоение» без единой отметки — дропается.
        Assert.False(ExcludeTriggerValidator.HasGrounds(Part(),
            "освоение: КПД наладки ниже 100% может негативно повлиять на К1 оператора"));
    }

    [Fact]
    public void HasGrounds_ConfirmedMastering_True()
    {
        Assert.True(ExcludeTriggerValidator.HasGrounds(
            Part(setupComment: "Освоение"), "освоение: КПД наладки ниже 100%"));
    }

    [Fact]
    public void HasGrounds_OperatorOnlyMastering_True()
    {
        var p = Part(operatorComment: "освоение детали, делаем впервые");
        Assert.True(ExcludeTriggerValidator.HasGrounds(p, "освоение: КПД наладки ниже 100%"));
    }

    [Fact]
    public void HasGrounds_OperatorMasteringRefuted_False()
    {
        var p = Part(operatorComment: "освоение детали");
        p.PartsHistory = new PartsHistoryDto
        {
            RecordsFound = 1,
            Lines = [new PartsHistoryLineDto { FinishedCount = 10 }],
        };
        Assert.False(ExcludeTriggerValidator.HasGrounds(p, "освоение: КПД наладки ниже 100%"));
    }

    [Fact]
    public void HasGrounds_Apprentice_RequiresReason()
    {
        Assert.True(ExcludeTriggerValidator.HasGrounds(
            Part(machiningComment: "Работа ученика"), "работа ученика: КПД ниже 100%"));
        Assert.False(ExcludeTriggerValidator.HasGrounds(
            Part(), "работа ученика: КПД ниже 100%"));
    }

    [Fact]
    public void HasGrounds_Inexperienced_Never()
    {
        Assert.False(ExcludeTriggerValidator.HasGrounds(Part(), "неопытный оператор, низкий КПД"));
    }

    [Fact]
    public void HasGrounds_FreeText_True()
    {
        Assert.True(ExcludeTriggerValidator.HasGrounds(Part(), "замена сверла на менее производительное"));
    }
}
