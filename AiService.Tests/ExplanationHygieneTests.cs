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
            "[Шайба АР136-01-007-11М8] Машинное время 6мин > норматива."));
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

    // Кейс 08.10.2026 QTS200ML 20.09: explanation описывает «Корпус верхний»,
    // чей модельный сигнал срезан («Другое» с детализацией — объяснено),
    // а выжил только hard по «Крышке».
    [Fact]
    public void ReferencesOnlyRemovedParts_StalePartReference_True()
    {
        var explanation = "Эскалация по жёстким правилам (1). Деталь «Корпус верхний АРПГА-01-10-004-192» " +
            "имеет КПД наладки ниже 100% без объяснения, что требует проверки. Отчёт мастера проверен, замечаний нет.";
        var surviving = new List<string>
        {
            "[Крышка АР91-00-004] Причина отклонения в наладке требует пересмотра технологии: «Отсутствие нормативов»"
        };
        var removed = new List<string>
        {
            "КПД наладки <100% без объяснения (Корпус верхний АРПГА-01-10-004-192)"
        };
        var parts = new List<string>
        {
            "Корпус верхний АРПГА-01-10-004-192",
            "Крышка АР91-00-004",
            "Фланец АРКО32-16-03-003-01",
            "Фланец АРКО32-16-03-003"
        };
        Assert.True(ExplanationHygiene.ReferencesOnlyRemovedParts(explanation, surviving, removed, parts));
    }

    // Легитимное «без объяснения»: названная деталь есть в выживших сигналах.
    [Fact]
    public void ReferencesOnlyRemovedParts_NamedPartSurvives_False()
    {
        var explanation = "Эскалация по жёстким правилам (1). Деталь «Червяк АР31.1-02-001 (100 ход)» " +
            "имеет КПД изготовления ниже порога в 70% без объяснения мастера.";
        var surviving = new List<string>
        {
            "[Червяк АР31.1-02-001 (100 ход)] КПД изготовления 0% при отсутствии норматива"
        };
        Assert.False(ExplanationHygiene.ReferencesOnlyRemovedParts(
            explanation, surviving, new List<string>(), new List<string> { "Червяк АР31.1-02-001 (100 ход)" }));
    }

    // Названа и срезанная, и выжившая детали — не трогаем.
    [Fact]
    public void ReferencesOnlyRemovedParts_MixedReference_False()
    {
        var explanation = "Эскалация по жёстким правилам (2). Корпус верхний АРПГА-01-10-004-192 снят с изготовления, " +
            "а Крышка АР91-00-004 требует пересмотра технологии.";
        var surviving = new List<string> { "[Крышка АР91-00-004] Причина отклонения в наладке требует пересмотра технологии" };
        var removed = new List<string> { "КПД наладки <100% без объяснения (Корпус верхний АРПГА-01-10-004-192)" };
        var parts = new List<string> { "Корпус верхний АРПГА-01-10-004-192", "Крышка АР91-00-004" };
        Assert.False(ExplanationHygiene.ReferencesOnlyRemovedParts(explanation, surviving, removed, parts));
    }

    // Упоминание детали, которую вообще не флаговали (история, прошлые решения), — не трогаем.
    [Fact]
    public void ReferencesOnlyRemovedParts_NeverFlaggedPart_False()
    {
        var explanation = "Эскалация по жёстким правилам (1). Как и раньше, Фланец АРКО32-16-03-003 идёт без отклонений.";
        var surviving = new List<string> { "[Крышка АР91-00-004] Причина отклонения в наладке требует пересмотра технологии" };
        var removed = new List<string> { "КПД наладки <100% без объяснения (Корпус верхний АРПГА-01-10-004-192)" };
        var parts = new List<string> { "Корпус верхний АРПГА-01-10-004-192", "Крышка АР91-00-004", "Фланец АРКО32-16-03-003" };
        Assert.False(ExplanationHygiene.ReferencesOnlyRemovedParts(explanation, surviving, removed, parts));
    }

    [Fact]
    public void ReferencesOnlyRemovedParts_NoPartsOrEmpty_False()
    {
        var surviving = new List<string> { "[Крышка АР91-00-004] Причина отклонения в наладке" };
        var removed = new List<string> { "КПД наладки <100% (Корпус верхний АРПГА-01-10-004-192)" };
        var parts = new List<string> { "Корпус верхний АРПГА-01-10-004-192", "Крышка АР91-00-004" };
        Assert.False(ExplanationHygiene.ReferencesOnlyRemovedParts(
            "Отчёт мастера проверен, замечаний нет.", surviving, removed, parts));
        Assert.False(ExplanationHygiene.ReferencesOnlyRemovedParts(null, surviving, removed, parts));
        Assert.False(ExplanationHygiene.ReferencesOnlyRemovedParts("", surviving, removed, parts));
    }
}
