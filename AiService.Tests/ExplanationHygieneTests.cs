using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Маркер перечисления правил в объяснении — скобочные ссылки [Деталь…].
/// </summary>
public class ExplanationHygieneTests
{
    [Fact]
    public void EnumeratesRules_BracketedParts_True()
    {
        Assert.True(ExplanationHygiene.EnumeratesRules(
            "Эскалация: [Палец АР30-02-004 (в ЗИП выбирать 40Х)] Причина наладки требует пересмотра; " +
            "[Шайба АР136-01-007-11М8] Машинное время 6мин >= норматива."));
    }

    [Fact]
    public void EnumeratesRules_PlainProse_False()
    {
        Assert.False(ExplanationHygiene.EnumeratesRules(
            "День требует проверки: необъяснённые отклонения в изготовлении."));
    }

    [Fact]
    public void EnumeratesRules_ShortDowntimeMarkers_False()
    {
        Assert.False(ExplanationHygiene.EnumeratesRules(
            "Простои [н] и [и] вычтены из расчёта, вопросов нет."));
    }

    [Fact]
    public void EnumeratesRules_NullOrEmpty_False()
    {
        Assert.False(ExplanationHygiene.EnumeratesRules(null));
        Assert.False(ExplanationHygiene.EnumeratesRules(""));
        Assert.False(ExplanationHygiene.EnumeratesRules("   "));
    }
}
