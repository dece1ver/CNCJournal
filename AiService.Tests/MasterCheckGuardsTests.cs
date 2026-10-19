using AiService.Models;
using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Детерминированные гарды verify-part: известные пустые формулировки.
/// Живые кейсы 10.2026: SKT21 «нет норм», HTC650M «ТП на 230» и «работа с технологом».
/// </summary>
public class MasterCheckGuardsTests
{
    private static PartContext Part(string setupReason, string setupDetail, string machiningReason, string machiningDetail) =>
        new() { MasterSetupComment = setupReason, MasterSetupDetail = setupDetail, MasterMachiningComment = machiningReason, MasterMachiningDetail = machiningDetail };

    private static List<VerifyAnomaly> Anomaly(string field) =>
        [new VerifyAnomaly { Field = field }];

    [Fact]
    public void NormativeAtDrugoe_LiveCase_Fires()
    {
        // SKT21 №104 20.09: изготовление «Другое» + «нет норм».
        var part = Part("Отсутствие нормативов", "", "Другое", "нет норм");
        Assert.Equal(MasterCheckGuards.NormativeRemark,
            MasterCheckGuards.CheckKnownFalsePattern(part, Anomaly(nameof(PartContext.MasterMachiningDetail))));
    }

    [Fact]
    public void NormativeAtDrugoe_SetupSide_Fires()
    {
        var part = Part("Другое", "норматива нет", "", "");
        Assert.Equal(MasterCheckGuards.NormativeRemark,
            MasterCheckGuards.CheckKnownFalsePattern(part, Anomaly(nameof(PartContext.MasterSetupDetail))));
    }

    [Fact]
    public void NormativeAtDrugoe_Variants_Fire()
    {
        foreach (var detail in new[] { "Нет норм ", "норм нет", "отсутствие нармативов", "нет  нормативов" })
        {
            var part = Part("Другое", detail, "", "");
            Assert.Equal(MasterCheckGuards.NormativeRemark,
                MasterCheckGuards.CheckKnownFalsePattern(part, Anomaly(nameof(PartContext.MasterSetupDetail))));
        }
    }

    [Fact]
    public void NormativeAtDrugoe_Negatives_Null()
    {
        // Не «Другое» — не наша юрисдикция.
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Освоение", "нет норм", "", ""), Anomaly(nameof(PartContext.MasterSetupDetail))));
        // «Нормально» — другое слово, не стем «норм».
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Другое", "всё нормально", "", ""), Anomaly(nameof(PartContext.MasterSetupDetail))));
        // M5-кейс решает модель, не гард.
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Другое", "изготовление оправки", "Другое", "изготовление оправки"),
            Anomaly(nameof(PartContext.MasterSetupDetail))));
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Другое", "", "", ""), Anomaly(nameof(PartContext.MasterSetupDetail))));
    }

    [Fact]
    public void Cryptogram_Tp_LiveCase_Fires()
    {
        // HTC650M 03.09: «Другое» + «ТП на 230» с обеих сторон.
        var part = Part("Другое", "ТП на 230", "Другое", "ТП на 230");
        Assert.Equal(MasterCheckGuards.TpRemark,
            MasterCheckGuards.CheckKnownFalsePattern(part, Anomaly(nameof(PartContext.MasterSetupDetail))));
    }

    [Fact]
    public void Cryptogram_Tp_NormalizedVariants_Fire()
    {
        foreach (var detail in new[] { "  тп  на  230 ", "ТП на 230.", "тп на 230" })
        {
            var part = Part("Другое", detail, "", "");
            Assert.Equal(MasterCheckGuards.TpRemark,
                MasterCheckGuards.CheckKnownFalsePattern(part, Anomaly(nameof(PartContext.MasterSetupDetail))));
        }
    }

    [Fact]
    public void Cryptogram_Tp_NearbyText_Null()
    {
        // Другой номер / продолжение фразы — решает модель.
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Другое", "ТП на 231", "", ""), Anomaly(nameof(PartContext.MasterSetupDetail))));
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Другое", "ТП на 230 выполнен", "", ""), Anomaly(nameof(PartContext.MasterSetupDetail))));
    }

    [Fact]
    public void Cryptogram_Technologist_LiveCase_Fires()
    {
        // HTC650M 25.09: «Другое» + «работа с технологом».
        var part = Part("Другое", "работа с технологом", "", "");
        Assert.Equal(MasterCheckGuards.TechnologistRemark,
            MasterCheckGuards.CheckKnownFalsePattern(part, Anomaly(nameof(PartContext.MasterSetupDetail))));
    }

    [Fact]
    public void Cryptogram_Technologist_NearbyText_Null()
    {
        // Другой глагол/число и M-пример с подгонкой — решает модель.
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Другое", "работал с технологом", "", ""), Anomaly(nameof(PartContext.MasterSetupDetail))));
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(
            Part("Другое", "Работа с технологами, подгон размеров", "", ""), Anomaly(nameof(PartContext.MasterSetupDetail))));
    }

    [Fact]
    public void UnknownFieldOrEmpty_Null()
    {
        var part = Part("Другое", "ТП на 230", "Другое", "нет норм");
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(part, Anomaly("MasterComment")));
        Assert.Null(MasterCheckGuards.CheckKnownFalsePattern(part, []));
    }
}
