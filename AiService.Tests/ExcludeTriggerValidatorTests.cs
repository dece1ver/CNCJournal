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
