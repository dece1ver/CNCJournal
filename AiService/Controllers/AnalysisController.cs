using AiService.Models;
using AiService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AiService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController(OllamaService ollama, ILogger<AnalysisController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AnalyzeResponse>> Analyze(
        [FromBody] AnalyzeRequest request,
        CancellationToken ct)
    {
        try
        {
            var hardRules = HardRuleEvaluator.Evaluate(request);

            var prompt = PromptBuilder.Build(request, hardRules);
            var raw = await ollama.GenerateAsync(prompt, ct);
            var llmResult = ParseResponse(raw);

            var notDowngraded = hardRules.SoftSignals
                .Where(s => !llmResult.DowngradedSignals.Contains(s))
                .ToList();

            var requiresReview = hardRules.MustEscalate || notDowngraded.Count > 0 || llmResult.RequiresReview;

            logger.LogDebug(
                "ДИАГНОСТИКА {Machine} {Date}: " +
                "hardRules.MustEscalate={MustEscalate}, hardRules.HardSignals=[{HardSignals}], " +
                "hardRules.SoftSignals=[{SoftSignals}], notDowngraded=[{NotDowngraded}], " +
                "llmResult.RequiresReview={LlmRR}, llmResult.HasError={LlmErr}, llmResult.Error={LlmErrMsg}, " +
                "llmResult.DowngradedSignals=[{LlmDowngraded}] → итоговый requiresReview={Final}",
                request.Machine, request.ShiftDate,
                hardRules.MustEscalate, string.Join(" | ", hardRules.HardSignals),
                string.Join(" | ", hardRules.SoftSignals), string.Join(" | ", notDowngraded),
                llmResult.RequiresReview, llmResult.HasError, llmResult.Error ?? "(нет)",
                string.Join(" | ", llmResult.DowngradedSignals), requiresReview);

            var result = new AnalyzeResponse
            {
                RequiresReview = requiresReview,
                Confidence = hardRules.MustEscalate
                    ? Math.Max(llmResult.Confidence, 0.85)
                    : llmResult.Confidence,
                Explanation = EnsureExplanation(llmResult, hardRules, notDowngraded),
                SuggestedReason = string.IsNullOrWhiteSpace(llmResult.SuggestedReason)
                    ? FallbackReason(hardRules, notDowngraded)
                    : llmResult.SuggestedReason,
                Error = llmResult.Error,
            };

            var allPartSignals = request.Parts.SelectMany(p => p.Signals);
            result.Signals = [.. llmResult.Signals
                .Concat(request.Signals)
                .Concat(allPartSignals)
                .Concat(hardRules.HardSignals)
                .Concat(notDowngraded)
                .Distinct()];

            logger.LogInformation(
                "Анализ: {Machine} {Date} → RequiresReview={R} (hard={H}, softNotDowngraded={S}), Confidence={C:F2}",
                request.Machine, request.ShiftDate, result.RequiresReview,
                hardRules.HardSignals.Count, notDowngraded.Count, result.Confidence);

            if (hardRules.SoftSignals.Count > 0)
            {
                logger.LogInformation(
                    "Soft-сигналы: {Total} всего, {Downgraded} понижено моделью: {List}",
                    hardRules.SoftSignals.Count,
                    hardRules.SoftSignals.Count - notDowngraded.Count,
                    string.Join(" | ", llmResult.DowngradedSignals));
            }

            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new AnalyzeResponse { Error = "Ollama не ответила за отведённое время" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при анализе {Machine} {Date}", request.Machine, request.ShiftDate);
            return StatusCode(500, new AnalyzeResponse { Error = ex.Message });
        }
    }

    private static string EnsureExplanation(
    AnalyzeResponse llmResult, HardRuleResult hardRules, List<string> notDowngraded)
    {
        if (hardRules.MustEscalate)
        {
            var hardBase = "Эскалация по жёстким правилам: "
                + string.Join("; ", hardRules.HardSignals) + ".";
            if (!string.IsNullOrWhiteSpace(llmResult.Explanation)
                && !llmResult.Explanation.Contains("отсутствует")
                && !llmResult.Explanation.Contains("нет объяснения")
                && !llmResult.Explanation.Contains("не объяснен"))
            {
                return hardBase + " " + llmResult.Explanation;
            }
            return hardBase;
        }

        if (notDowngraded.Count > 0)
            return "Эскалация по превышению машинного времени над нормативом: "
                + string.Join("; ", notDowngraded);

        if (!string.IsNullOrWhiteSpace(llmResult.Explanation))
            return llmResult.Explanation;

        return llmResult.HasError
            ? "Не удалось получить объяснение от модели (ошибка разбора ответа)."
            : "Явных отклонений не обнаружено.";
    }

    private static string FallbackReason(HardRuleResult hardRules, List<string> notDowngraded)
    {
        if (hardRules.MustEscalate) return hardRules.HardSignals.FirstOrDefault() ?? "Требует проверки";
        if (notDowngraded.Count > 0) return "Машинное время превышает норматив";
        return "Без замечаний";
    }

    /// <summary> Пробуем спарсить JSON из ответа модели, обрабатываем типичные огрехи </summary>
    private static AnalyzeResponse ParseResponse(string raw)
    {
        var json = ExtractJson(raw);

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new AnalyzeResponse
            {
                RequiresReview = root.TryGetProperty("requires_review", out var rr) && rr.ValueKind == JsonValueKind.True,
                Confidence = root.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number
                    ? cf.GetDouble()
                    : 0.5,
                Signals = root.TryGetProperty("signals", out var sg) && sg.ValueKind == JsonValueKind.Array
                    ? [.. sg.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0)]
                    : [],
                DowngradedSignals = root.TryGetProperty("downgraded_signals", out var ds) && ds.ValueKind == JsonValueKind.Array
                    ? [.. ds.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0)]
                    : [],
                Explanation = root.TryGetProperty("explanation", out var ex) ? ex.GetString() ?? "" : "",
                SuggestedReason = root.TryGetProperty("suggested_reason", out var sr) ? sr.GetString() ?? "" : "",
            };
        }
        catch (JsonException)
        {
            return new AnalyzeResponse
            {
                Error = $"Не удалось распарсить ответ модели: {raw[..Math.Min(200, raw.Length)]}",
                Confidence = 0,
            };
        }
    }

    private static string ExtractJson(string raw)
    {
        var text = raw.Trim();

        var thinkEnd = text.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0)
            text = text[(thinkEnd + "</think>".Length)..].Trim();

        if (text.StartsWith("```"))
        {
            var fenceStart = text.IndexOf('\n');
            if (fenceStart >= 0) text = text[(fenceStart + 1)..];
            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0) text = text[..fenceEnd];
            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}