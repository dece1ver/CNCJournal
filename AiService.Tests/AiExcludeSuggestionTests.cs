using remeLog.Core.Services;
using remeLog.Models;

namespace AiService.Tests;

/// <summary>
/// Привязка предложений/флагов ИИ к строкам суток на клиенте.
/// Кейс 30.09.2026, SKT21 25.09 «Винт позиционирующий…»: в сутках две строки
/// с ОДНИМ именем/установкой/заказом, исключение ушло на первую по списку,
/// а проблема была во второй. Теперь сервер присылает номер строки («#N»),
/// а неоднозначный ключ без номера не привязывается вовсе.
/// </summary>
public class AiExcludeSuggestionTests
{
    private static Part Row(string name = "Винт позиционирующий 1.АР0001-001 ТОС3",
        string order = "ФЛ2609-0029.3.1", int setup = 1) =>
        new(Guid.NewGuid(), "Станок1", "День", new DateTime(2026, 9, 25), "Иванов",
            name, order, setup,
            finishedCount: 0, defectiveCount: 0, totalCount: 0,
            startSetupTime: new DateTime(2026, 9, 25, 8, 0, 0),
            startMachiningTime: new DateTime(2026, 9, 25, 9, 0, 0),
            setupTimeFact: 10, endMachiningTime: new DateTime(2026, 9, 25, 10, 0, 0),
            setupTimePlan: 10, setupTimePlanForReport: 10, singleProductionTimePlan: 5,
            productionTimeFact: 0, machiningTime: TimeSpan.FromMinutes(10),
            setupDowntimes: 0, machiningDowntimes: 0, partialSetupTime: 0,
            createNcProgramTime: 0, maintenanceTime: 0, toolSearchingTime: 0,
            toolChangingTime: 0, mentoringTime: 0, contactingDepartmentsTime: 0,
            fixtureMakingTime: 0, hardwareFailureTime: 0, specialDowntimeTime: 0,
            operatorComment: "");

    [Fact]
    public void TryParse_OrdinalTail_Parsed()
    {
        Assert.True(AiExcludeSuggestion.TryParse(
            "Винт§1§ФЛ2609-0029.3.1§частичная наладка§#2", null, out var s));
        Assert.Equal(2, s!.Ordinal);
        Assert.Equal("частичная наладка", s.Reason);
    }

    [Fact]
    public void TryParse_NoOrdinalTail_Legacy()
    {
        Assert.True(AiExcludeSuggestion.TryParse(
            "Винт§1§ФЛ2609-0029.3.1§частичная наладка", null, out var s));
        Assert.Null(s!.Ordinal);
        Assert.Equal("частичная наладка", s.Reason);
    }

    [Fact]
    public void FindRow_OrdinalPicksSecondDuplicate()
    {
        var rows = new List<Part> { Row(), Row() };

        Assert.True(AiExcludeSuggestion.TryParse(
            "Винт позиционирующий 1.АР0001-001 ТОС3§1§ФЛ2609-0029.3.1§причина§#2", null, out var s));

        var found = AiExcludeSuggestion.FindRow(rows, s!);
        Assert.NotNull(found);
        Assert.Same(rows[1], found);
    }

    [Fact]
    public void FindRow_AmbiguousWithoutOrdinal_Null()
    {
        // Старые записи без номера на неоднозначном ключе: строка не выбрана
        // (раньше бралась первая — исключалась не та).
        var rows = new List<Part> { Row(), Row() };

        Assert.True(AiExcludeSuggestion.TryParse(
            "Винт позиционирующий 1.АР0001-001 ТОС3§1§ФЛ2609-0029.3.1§причина", null, out var s));

        Assert.Null(AiExcludeSuggestion.FindRow(rows, s!));
    }

    [Fact]
    public void FindRow_UniqueKey_StillWorks()
    {
        // Обычный случай (ключ уникален) — привязка как раньше, номер не нужен.
        var rows = new List<Part> { Row(), Row("Другая", "З2", 1) };

        Assert.True(AiExcludeSuggestion.TryParse(
            "Винт позиционирующий 1.АР0001-001 ТОС3§1§ФЛ2609-0029.3.1§причина", null, out var s));

        Assert.Same(rows[0], AiExcludeSuggestion.FindRow(rows, s!));
    }

    [Fact]
    public void FindRow_OrdinalMismatch_FallsBackToSearch()
    {
        // Номер не сошёлся (другая сортировка строк) — ищем по ключу, не теряя кандидата.
        var rows = new List<Part> { Row("Другая", "З2", 1), Row() };

        Assert.True(AiExcludeSuggestion.TryParse(
            "Винт позиционирующий 1.АР0001-001 ТОС3§1§ФЛ2609-0029.3.1§причина§#1", null, out var s));

        Assert.Same(rows[1], AiExcludeSuggestion.FindRow(rows, s!));
    }
}
