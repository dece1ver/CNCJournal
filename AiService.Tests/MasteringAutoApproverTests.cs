using AiService.Models;
using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Регресс-набор по флипам verify-part из прод-логов (08–09.26).
/// Каждый кейс — реальная связка «причина + комментарий», где модель
/// отвечала то true, то false при том же входе. Ожидаемый вердикт —
/// решение по правилам master_check.txt (строгая сторона: спорные
/// случаи #11 «Шайба», #14 «ТФ-ОФ-71» здесь НЕ зафиксированы как true).
/// </summary>
public class MasteringAutoApproverTests
{
    private static PartContext SetupPart(
        string reason, string detail, string order = "УЧ2607-0001.1.1",
        PartsHistoryDto? history = null) => new()
        {
            PartName = "Тестовая деталь",
            Order = order,
            Setup = 1,
            MasterSetupComment = reason,
            MasterSetupDetail = detail,
            PartsHistory = history,
        };

    private static PartContext MachiningPart(
        string reason, string detail, string order = "УЧ2607-0001.1.1") => new()
        {
            PartName = "Тестовая деталь",
            Order = order,
            Setup = 1,
            MasterMachiningComment = reason,
            MasterMachiningDetail = detail,
        };

    // Заказ «Без М/Л» осознанно НЕ автоапрувится кодом: «любой непустой
    // комментарий достаточен» — слишком грубое правило, такие случаи
    // оценивает модель по особому случаю правила 3 master_check.txt.
    [Theory]
    [InlineData("Другое", "Без М/Л")]
    [InlineData("Другое", "оправка")]
    [InlineData("Доработка", "переточка сверла")]
    public void NoOrder_GoesToModel_NotAutoApproved(string reason, string detail)
    {
        var p = SetupPart(reason, detail, order: "Без М/Л");
        Assert.False(MasteringAutoApprover.IsSetupReasonSelfSufficient(p));
        Assert.False(MasteringAutoApprover.IsAnomalyFieldSelfSufficient(
            nameof(PartContext.MasterSetupDetail), p));
    }

    // #10 «Колокол», #15 «Оправка»: универсальный участок — глоссарий правила 4.
    [Theory]
    [InlineData("С универсального участка")]
    [InlineData("деталь с универсального участка")]
    [InlineData("с ручного участка")]
    public void NotByProcess_NonCncArea_IsSelfSufficient(string detail)
    {
        var p = SetupPart("Изготовление не по техпроцессу", detail);
        Assert.True(MasteringAutoApprover.IsSetupReasonSelfSufficient(p));
    }

    // #2, #4, #5 «за 2 установки», #12 «изготовление в одну установку».
    [Theory]
    [InlineData("за 2 установки")]
    [InlineData("изготовление в одну установку")]
    [InlineData("Заложена 1 установка")]
    [InlineData("деталь с других станков")]
    public void NotByProcess_GlossaryPhrases_AreSelfSufficient(string detail)
    {
        var setup = SetupPart("Изготовление не по техпроцессу", detail);
        var machining = MachiningPart("Изготовление не по техпроцессу", detail);
        Assert.True(MasteringAutoApprover.IsSetupReasonSelfSufficient(setup));
        Assert.True(MasteringAutoApprover.IsMachiningReasonSelfSufficient(machining));
    }

    // #7 «Крышка», #17 «Втулка АР80»: упоминание чужого станка.
    [Theory]
    [InlineData("деталь с 550 (там неработает конвеер)")]
    [InlineData("заготовка с Rontek")]
    public void NotByProcess_PartFromOtherMachine_IsSelfSufficient(string detail)
    {
        var p = MachiningPart("Изготовление не по техпроцессу", detail);
        Assert.True(MasteringAutoApprover.IsMachiningReasonSelfSufficient(p));
    }

    [Theory]
    [InlineData("требуется проверка")]
    [InlineData("")]
    public void NotByProcess_VagueDetail_IsNotSelfSufficient(string detail)
    {
        var p = SetupPart("Изготовление не по техпроцессу", detail);
        Assert.False(MasteringAutoApprover.IsSetupReasonSelfSufficient(p));
    }

    // Дословные примеры правила 3 master_check.txt.
    [Theory]
    [InlineData("перекаленные")]
    [InlineData("перекалены")]
    [InlineData("заготовки разных диаметров")]
    [InlineData("припуск больше чертежа")]
    public void MismatchedBlanks_ConcreteDefect_IsSelfSufficient(string detail)
    {
        var p = MachiningPart("Несоответствующие заготовки", detail);
        Assert.True(MasteringAutoApprover.IsMachiningReasonSelfSufficient(p));
    }

    // Мусор — по-прежнему на модель.
    [Theory]
    [InlineData("")]
    [InlineData("хз")]
    [InlineData("просто")]
    [InlineData("просто так написал")]
    [InlineData("конкретика")]
    [InlineData("ы")]
    public void MismatchedBlanks_GarbageDetail_IsNotSelfSufficient(string detail)
    {
        var p = MachiningPart("Несоответствующие заготовки", detail);
        Assert.False(MasteringAutoApprover.IsMachiningReasonSelfSufficient(p));
    }

    // #9 «Кольцо»: упоминание смены УП подтверждает освоение (правило 2).
    [Theory]
    [InlineData("Написание УП")]
    [InlineData("написали новую программу")]
    public void Mastering_ProgramWorkMention_ConfirmsMastering(string detail)
    {
        var p = SetupPart("Освоение", detail);
        Assert.True(MasteringAutoApprover.IsConfirmedMastering(p));
        Assert.True(MasteringAutoApprover.IsSetupReasonSelfSufficient(p));
    }

    // История опровергает, упоминания УП/КД нет — строго, на модель (#11-подобные).
    [Fact]
    public void Mastering_HistoryRefutesWithoutRevisionClaim_IsNotConfirmed()
    {
        var p = SetupPart("Освоение", "", history: new PartsHistoryDto
        {
            RecordsFound = 1,
            Lines = { new PartsHistoryLineDto { FinishedCount = 8 } },
        });
        Assert.False(MasteringAutoApprover.IsConfirmedMastering(p));
    }

    // #6: «Другое» + названное действие над программой.
    [Theory]
    [InlineData("редактирование программы под конкретную деталь")]
    [InlineData("написание УП")]
    public void OtherReason_ProgramWork_IsSelfSufficient(string detail)
    {
        var p = SetupPart("Другое", detail);
        Assert.True(MasteringAutoApprover.IsSetupReasonSelfSufficient(p));
    }

    // «Другое» без действия над программой — по-прежнему на модель.
    [Theory]
    [InlineData("долго искали инструмент")]
    [InlineData("нужна была доработка")]
    public void OtherReason_WithoutProgramWork_IsNotSelfSufficient(string detail)
    {
        var p = SetupPart("Другое", detail);
        Assert.False(MasteringAutoApprover.IsSetupReasonSelfSufficient(p));
    }

    [Fact]
    public void Rework_Tautology_IsNotSelfSufficient()
    {
        var p = SetupPart("Доработка", "нужна была доработка");
        Assert.False(MasteringAutoApprover.IsSetupReasonSelfSufficient(p));
    }

    // Существующее поведение не сломано.
    [Fact]
    public void NormativesReasons_AreUnconditionallySelfSufficient()
    {
        Assert.True(MasteringAutoApprover.IsMachiningReasonSelfSufficient(
            MachiningPart("Некорректные нормативы", "")));
        Assert.True(MasteringAutoApprover.IsMachiningReasonSelfSufficient(
            MachiningPart("Отсутствие нормативов", "")));
    }

    [Fact]
    public void UnknownReason_IsNotSelfSufficient()
    {
        Assert.False(MasteringAutoApprover.IsSetupReasonSelfSufficient(
            SetupPart("Некорректное заполнение", "исправил")));
    }

    [Fact]
    public void AnomalyFieldDispatcher_MapsDetailFields()
    {
        var p = SetupPart("Освоение", "");
        Assert.True(MasteringAutoApprover.IsAnomalyFieldSelfSufficient(
            nameof(PartContext.MasterSetupDetail), p));
        Assert.False(MasteringAutoApprover.IsAnomalyFieldSelfSufficient(
            nameof(PartContext.MasterSetupComment), p));
    }
}
