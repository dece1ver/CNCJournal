using remeLog.Core;
using remeLog.Models;

namespace remeLog.Core.Tests;

/// <summary>
/// Валидация ввода (IDataErrorInfo, «восклицательные знаки») проверяет ИТОГОВУЮ причину:
/// решение СГТ при наличии переопределения, иначе отметку мастера.
/// Построчная ИИ-проверка мастера (GetAiCheckAnomalies) при этом по-прежнему
/// работает только по отметкам мастера.
/// </summary>
public class PartValidationOverrideTests
{
    public PartValidationOverrideTests()
    {
        // Зеркало справочника cnc_deviation_reasons (см. SqlSchemaBootstrapper).
        DomainSettings.SetupReasons = new List<(string Reason, bool RequireComment)>
        {
            ("Отсутствие нормативов", false),
            ("Освоение", false),
            ("Другое", true),
        };
        DomainSettings.MachiningReasons = new List<(string Reason, bool RequireComment)>
        {
            ("Отсутствие нормативов", false),
            ("Другое", true),
        };
        DomainSettings.MaxSetupLimit = 2;
    }

    private static Part BuildPart(
        string masterSetupComment = "",
        string masterSetupDetail = "",
        string masterMachiningComment = "",
        string masterMachiningDetail = "",
        double setupTimePlan = 0,
        double singleProductionTimePlan = 5)
    {
        var now = DateTime.Now;
        return new Part(
            Guid.NewGuid(), "Станок1", "День", DateTime.Today,
            "Иванов", "Деталь 1", "ПР2601-00001.1.1", 1,
            0, 0, 0,
            now, now, 0, now,
            setupTimePlan, 0, singleProductionTimePlan, 0,
            TimeSpan.Zero,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            "",
            masterSetupComment, masterMachiningComment,
            "", "",
            "", masterSetupDetail, masterMachiningDetail);
    }

    [Fact]
    public void MissingMasterReason_FailsValidation()
    {
        var part = BuildPart();

        Assert.NotNull(part[nameof(Part.MasterSetupComment)]);
        Assert.NotNull(part.Error);
    }

    [Fact]
    public void SetupOverride_SuppressesMasterError()
    {
        var part = BuildPart();
        Assert.NotNull(part.Error); // исходно ошибка мастера есть

        part.SetupReasonOverride = "Отсутствие нормативов";
        part.ReasonOverrideBy = "tester";
        part.ReasonOverrideAt = DateTime.Now;

        Assert.Null(part[nameof(Part.MasterSetupComment)]);
        Assert.Null(part.Error);
    }

    [Fact]
    public void SetupOverride_EffectiveReason_StillValidated()
    {
        // «Освоение» не объясняет отсутствие норматива — ошибка уже на итоговую причину.
        var part = BuildPart();
        part.SetupReasonOverride = "Освоение";
        part.ReasonOverrideBy = "tester";

        var error = part[nameof(Part.MasterSetupComment)];
        Assert.NotNull(error);
        Assert.Contains("Освоение", error);
    }

    [Fact]
    public void SetupOverride_RequireCommentWithoutComment_FailsDetail()
    {
        var part = BuildPart();
        part.SetupReasonOverride = "Другое";
        part.ReasonOverrideBy = "tester";

        Assert.NotNull(part[nameof(Part.MasterSetupDetail)]);
        Assert.NotNull(part.Error);
    }

    [Fact]
    public void SetupOverride_WithComment_Passes()
    {
        var part = BuildPart();
        part.SetupReasonOverride = "Другое";
        part.SetupReasonOverrideComment = "Ни одна типовая причина не подошла, детали в журнале";
        part.ReasonOverrideBy = "tester";

        Assert.Null(part.Error);
    }

    [Fact]
    public void MachiningOverride_ContradictingNormative_Fails()
    {
        // Норматив изготовления задан (5), а итоговая причина — «Отсутствие нормативов».
        var part = BuildPart(masterSetupComment: "Отсутствие нормативов",
            masterSetupDetail: "x", masterMachiningComment: "Другое", masterMachiningDetail: "y");
        part.MachiningReasonOverride = "Отсутствие нормативов";
        part.MachiningReasonOverrideComment = "z";
        part.ReasonOverrideBy = "tester";

        var error = part[nameof(Part.MasterMachiningComment)];
        Assert.NotNull(error);
        Assert.Contains("неприменимо", error);
    }

    [Fact]
    public void AiCheckAnomalies_StillMasterBased()
    {
        // Построчная проверка мастера смотрит только отметки мастера,
        // переопределение СГТ на неё не влияет.
        var part = BuildPart();
        var before = part.GetAiCheckAnomalies().Count;
        Assert.True(before > 0);

        part.SetupReasonOverride = "Отсутствие нормативов";
        part.ReasonOverrideBy = "tester";

        Assert.Equal(before, part.GetAiCheckAnomalies().Count);
    }
}
