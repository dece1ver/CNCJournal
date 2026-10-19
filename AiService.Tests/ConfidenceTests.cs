using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// ComputeConfidence: детерминированная эскалация (hard по данным, hard
/// по отчёту мастера, сбой парсинга HasError) — всегда 1.0, иначе
/// самооценка модели проходит как пришла (soft notDowngraded и модельные
/// S1-вопросы уверенность не меняют).
/// </summary>
public class ConfidenceTests
{
    // hard по данным: решение принимает код, самооценка модели не важна —
    // 0.2, 0.85 и 0.95 означают один и тот же исход.
    [Theory]
    [InlineData(0.2)]
    [InlineData(0.85)]
    [InlineData(0.95)]
    public void ComputeConfidence_HardEscalation_AlwaysOne(double llmConfidence)
    {
        Assert.Equal(1.0, ShiftReportRuleEvaluator.ComputeConfidence(
            hardMustEscalate: true, shiftMustEscalate: false, hasError: false, llmConfidence: llmConfidence));
    }

    [Fact]
    public void ComputeConfidence_ShiftHardEscalation_One()
    {
        Assert.Equal(1.0, ShiftReportRuleEvaluator.ComputeConfidence(
            hardMustEscalate: false, shiftMustEscalate: true, hasError: false, llmConfidence: 0.62));
    }

    // Сбой парсинга: сырой Confidence в ParseResponse равен 0, но это
    // fail-safe «человек обязан посмотреть», а не «уверенность 0%».
    [Fact]
    public void ComputeConfidence_HasError_One()
    {
        Assert.Equal(1.0, ShiftReportRuleEvaluator.ComputeConfidence(
            hardMustEscalate: false, shiftMustEscalate: false, hasError: true, llmConfidence: 0.0));
    }

    [Fact]
    public void ComputeConfidence_ModelVerdict_Passthrough()
    {
        Assert.Equal(0.62, ShiftReportRuleEvaluator.ComputeConfidence(
            hardMustEscalate: false, shiftMustEscalate: false, hasError: false, llmConfidence: 0.62));
    }
}
