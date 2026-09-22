using remeLog.Core.Services;
using remeLog.Models;

namespace remeLog.Core.Tests;

public class AiExcludeSuggestionTests
{
    [Fact]
    public void TryParse_FourSegments_ParsesAllFields()
    {
        var ok = AiExcludeSuggestion.TryParse(
            "Деталь 1§2§ПР2601-00001.1.1§Разовая проблема с инструментом",
            "Общее объяснение", out var s);

        Assert.True(ok);
        Assert.NotNull(s);
        Assert.Equal("Деталь 1", s!.PartName);
        Assert.Equal(2, s.Setup);
        Assert.Equal("ПР2601-00001.1.1", s.Order);
        Assert.Equal("Разовая проблема с инструментом", s.Reason);
    }

    [Fact]
    public void TryParse_ThreeSegments_FallsBackToExplanation()
    {
        var ok = AiExcludeSuggestion.TryParse(
            "Деталь 1§2§ПР2601-00001.1.1",
            "Общее объяснение", out var s);

        Assert.True(ok);
        Assert.Equal("Общее объяснение", s!.Reason);
    }

    [Fact]
    public void TryParse_EmptyReasonSegment_FallsBackToExplanation()
    {
        var ok = AiExcludeSuggestion.TryParse(
            "Деталь 1§2§ПР2601-00001.1.1§  ",
            "Общее объяснение", out var s);

        Assert.True(ok);
        Assert.Equal("Общее объяснение", s!.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Деталь 1§2")]
    [InlineData("Деталь 1§NaN§ПР2601-00001.1.1§Причина")]
    [InlineData(null)]
    public void TryParse_Junk_ReturnsFalse(string? entry)
    {
        Assert.False(AiExcludeSuggestion.TryParse(entry, "fallback", out var s));
        Assert.Null(s);
    }

    [Fact]
    public void ParseMany_SkipsJunk_KeepsGood()
    {
        var list = AiExcludeSuggestion.ParseMany(new[]
        {
            "Деталь 1§1§З1§Причина 1",
            "мусор",
            "Деталь 2§X§З2§Причина 2",
            "Деталь 3§3§З3",
        }, "fallback");

        Assert.Equal(2, list.Count);
        Assert.Equal("Причина 1", list[0].Reason);
        Assert.Equal("fallback", list[1].Reason);
    }

    [Fact]
    public void ParseMany_Null_ReturnsEmpty()
    {
        Assert.Empty(AiExcludeSuggestion.ParseMany(null, "fallback"));
    }

    private static Part Row(string name, int setup, string order) =>
        new(Guid.NewGuid(), "Станок", "День", new DateTime(2026, 9, 20), "Оператор",
            name, order, setup, 0, 0, 0, new DateTime(2026, 9, 20, 8, 0, 0),
            new DateTime(2026, 9, 20, 8, 0, 0), 0, new DateTime(2026, 9, 20, 9, 0, 0),
            0, 0, 0, 0, TimeSpan.Zero, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "");

    private static AiExcludeSuggestion Sugg(string name, int setup, string order) =>
        new(name, setup, order, "причина");

    [Fact]
    public void FindRow_ExactMatch_Found()
    {
        var rows = new[] { Row("Деталь 1 (производство)", 1, "З1") };

        Assert.Same(rows[0], AiExcludeSuggestion.FindRow(rows, Sugg("Деталь 1 (производство)", 1, "З1")));
    }

    [Fact]
    public void FindRow_TrimmedParentheses_Found()
    {
        // Кейс 21.09: модель обрезала «(производство)» — привязываемся всё равно.
        var rows = new[] { Row("Прокладка АРКП01-15-028 (производство)", 1, "УЧ2608-0079.3.1") };

        var found = AiExcludeSuggestion.FindRow(rows, Sugg("Прокладка АРКП01-15-028", 1, "УЧ2608-0079.3.1"));

        Assert.Same(rows[0], found);
    }

    [Fact]
    public void FindRow_AmbiguousNormalized_Null()
    {
        // Та же деталь в обеих сменах — два кандидата, не гадаем.
        var rows = new[]
        {
            Row("Деталь 1 (производство)", 1, "З1"),
            Row("Деталь 1 (образец)", 1, "З1"),
        };

        Assert.Null(AiExcludeSuggestion.FindRow(rows, Sugg("Деталь 1", 1, "З1")));
    }

    [Fact]
    public void FindRow_WrongSetupOrOrder_Null()
    {
        var rows = new[] { Row("Деталь 1 (производство)", 1, "З1") };

        Assert.Null(AiExcludeSuggestion.FindRow(rows, Sugg("Деталь 1", 2, "З1")));
        Assert.Null(AiExcludeSuggestion.FindRow(rows, Sugg("Деталь 1", 1, "З2")));
        Assert.Null(AiExcludeSuggestion.FindRow(rows, Sugg("Другая", 1, "З1")));
    }
}
