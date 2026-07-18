using AiService.Models;
using AiService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace AiService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController(OllamaService ollama, PromptBuilder promptBuilder, ILogger<AnalysisController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [HttpGet("health")]
    public async Task<ActionResult> Health()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)";
        logger.LogDebug("Health check from {IP}", ip);

        var ollamaOk = await ollama.CheckHealthAsync();
        logger.LogDebug("Health: IP={IP}, ollama={Ollama}", ip, ollamaOk);

        return Ok(new { status = "ok", ollama = ollamaOk });
    }

    [HttpGet("queue-length")]
    public ActionResult QueueLength()
    {
        return Ok(new { queueLength = ollama.QueueLength });
    }

    [HttpPost]
    public async Task<ActionResult<AnalyzeResponse>> Analyze(
        [FromBody] AnalyzeRequest request,
        CancellationToken ct)
    {
        try
        {
            var hardRules = HardRuleEvaluator.Evaluate(request);

            var prompt = promptBuilder.Build(request, hardRules);
            var thinkCapture = new StringBuilder();

            var (raw, thinking) = await ollama.GenerateAsync(prompt, think: false, thinkingProgress: null, ct: ct, model: request.Model);
            var llmResult = ParseResponse(raw);
            var notDowngraded = hardRules.SoftSignals
                .Where(s => !llmResult.DowngradedSignals.Contains(s))
                .ToList();

            var (filteredSignals, reset) = FalsePositiveFilter.Apply(
                request, hardRules, notDowngraded, llmResult.Signals);
            llmResult.Signals = filteredSignals;

            if (reset)
            {
                llmResult.RequiresReview = false;
                llmResult.Explanation = "";
                llmResult.SuggestedReason = "";
                logger.LogInformation(
                    "Пост-фильтр сбросил requiresReview: {Machine} {Date} — " +
                    "все сигналы LLM были галлюцинациями про б/н/б/и",
                    request.Machine, request.ShiftDate);
            }

            var requiresReview = reset ? false
                : hardRules.MustEscalate || notDowngraded.Count > 0 || llmResult.RequiresReview;

            logger.LogDebug(
                "ДИАГНОСТИКА {Machine} {Date}: " +
                "hardRules.MustEscalate={MustEscalate}, hardRules.HardSignals=[{HardSignals}], " +
                "hardRules.SoftSignals=[{SoftSignals}], notDowngraded=[{NotDowngraded}], " +
                "llmResult.RequiresReview={LlmRR}, llmResult.HasError={LlmErr}, llmResult.Error={LlmErrMsg}, " +
                "llmResult.DowngradedSignals=[{LlmDowngraded}], llmResult.ExcludeFromReports={ExcludeCount} [{ExcludeList}] → итоговый requiresReview={Final}",
                request.Machine, request.ShiftDate,
                hardRules.MustEscalate, string.Join(" | ", hardRules.HardSignals),
                string.Join(" | ", hardRules.SoftSignals), string.Join(" | ", notDowngraded),
                llmResult.RequiresReview, llmResult.HasError, llmResult.Error ?? "(нет)",
                string.Join(" | ", llmResult.DowngradedSignals),
                llmResult.SuggestExcludeFromReports.Count, string.Join(" | ", llmResult.SuggestExcludeFromReports),
                requiresReview);

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
                SuggestExcludeFromReports = llmResult.SuggestExcludeFromReports,
                PromptVersion = promptBuilder.PromptVersion,
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

            if (hardRules.MustEscalate && result.SuggestExcludeFromReports.Count > 0)
            {
                var hardPartNames = hardRules.HardSignals
                    .Select(s =>
                    {
                        // Сигналы вида "[PartName] ..."
                        if (s.StartsWith('['))
                        {
                            var end = s.IndexOf(']');
                            return end > 1 ? s[1..end] : null;
                        }
                        return null;
                    })
                    .Where(n => n != null)
                    .ToHashSet()!;

                result.SuggestExcludeFromReports = [.. result.SuggestExcludeFromReports
                    .Where(entry =>
                    {
                        var name = entry.Split('§')[0];
                        return !hardPartNames.Contains(name);
                    })];
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

    [HttpPost("stream")]
    public async Task AnalyzeWithStream(
    [FromBody] AnalyzeRequest request,
    CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        var bufferingFeature = HttpContext.Features
            .Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        async Task Send(string evt, string data)
        {
            await Response.WriteAsync($"event: {evt}\ndata: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        logger.LogInformation("Stream-анализ начат: {Machine} {Date}",
            request.Machine, request.ShiftDate);

        var hardRules = HardRuleEvaluator.Evaluate(request);
        var prompt = promptBuilder.Build(request, hardRules);

        logger.LogDebug("Промпт построен, символов: {Len}", prompt.Length);

        var channel = System.Threading.Channels.Channel.CreateBounded<string>(
            new System.Threading.Channels.BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            });

        var lastThought = "";
        var progress = new Progress<string>(thought =>
        {
            if (thought == lastThought) return;
            lastThought = thought;
            logger.LogDebug("Think: {T}", thought[..Math.Min(50, thought.Length)]);
            channel.Writer.TryWrite(thought);
        });

        string raw;
        string? thinking;
        int queuePosition;

        try
        {
            queuePosition = await ollama.EnterQueueAsync(ct);
            logger.LogInformation(
                "Вошли в очередь: {QueuePos} для {Machine} {Date}",
                queuePosition, request.Machine, request.ShiftDate);

            if (queuePosition > 1)
                await Send("queue", JsonSerializer.Serialize(new { position = queuePosition }));

            logger.LogInformation("Отправка в Ollama...");

            var generateTask = ollama.GenerateCoreAsync(prompt, true, progress, ct, model: request.Model);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var drainTask = Task.Run(async () =>
            {
                await foreach (var thought in channel.Reader.ReadAllAsync(cts.Token))
                {
                    logger.LogDebug("Отправка thinking клиенту");
                    await Send("thinking", JsonSerializer.Serialize(thought));
                }
            }, ct);

            (raw, thinking) = await generateTask;
            logger.LogInformation(
                "Ollama завершила. response={RLen}, thinking={TLen}",
                raw.Length, thinking?.Length ?? 0);

            channel.Writer.Complete();
            await drainTask;
            logger.LogInformation("Drain завершён");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Отменён: {Machine} {Date}", request.Machine, request.ShiftDate);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка: {Machine} {Date}", request.Machine, request.ShiftDate);
            await Send("error", JsonSerializer.Serialize(ex.Message));
            return;
        }
        finally
        {
            ollama.LeaveQueue();
        }

        var llmResult = ParseResponse(raw);

        logger.LogDebug("ParseResponse: RequiresReview={R}, Error={E}, ExcludeFromReports={ExcludeCount} [{ExcludeList}]",
            llmResult.RequiresReview, llmResult.Error ?? "(нет)",
            llmResult.SuggestExcludeFromReports.Count, string.Join(" | ", llmResult.SuggestExcludeFromReports));

        var notDowngraded = hardRules.SoftSignals
            .Where(s => !llmResult.DowngradedSignals.Contains(s))
            .ToList();

        var (filteredSignals, reset) = FalsePositiveFilter.Apply(
            request, hardRules, notDowngraded, llmResult.Signals);
        llmResult.Signals = filteredSignals;

        if (reset)
        {
            llmResult.RequiresReview = false;
            llmResult.Explanation = "";
            llmResult.SuggestedReason = "";
            logger.LogInformation(
                "Пост-фильтр сбросил requiresReview: {Machine} {Date} — " +
                "все сигналы LLM были галлюцинациями про б/н/б/и",
                request.Machine, request.ShiftDate);
        }

        var requiresReview = reset ? false
            : hardRules.MustEscalate || notDowngraded.Count > 0 || llmResult.RequiresReview;

        var result = new AnalyzeResponse
        {
            RequiresReview = requiresReview,
            Confidence = hardRules.MustEscalate
                ? Math.Max(llmResult.Confidence, 0.85) : llmResult.Confidence,
            Explanation = EnsureExplanation(llmResult, hardRules, notDowngraded),
            SuggestedReason = string.IsNullOrWhiteSpace(llmResult.SuggestedReason)
                ? FallbackReason(hardRules, notDowngraded) : llmResult.SuggestedReason,
            ThinkingProcess = thinking,
            SuggestExcludeFromReports = llmResult.SuggestExcludeFromReports,
            Error = llmResult.Error,
            PromptVersion = promptBuilder.PromptVersion,
        };

        if (hardRules.MustEscalate && result.SuggestExcludeFromReports.Count > 0)
        {
            var hardPartNames = hardRules.HardSignals
                .Where(s => s.StartsWith('['))
                .Select(s => s[1..s.IndexOf(']')])
                .ToHashSet();
            result.SuggestExcludeFromReports = [.. result.SuggestExcludeFromReports
            .Where(e => !hardPartNames.Contains(e.Split('§')[0]))];
        }

        result.Signals = [.. llmResult.Signals
        .Concat(request.Signals)
        .Concat(request.Parts.SelectMany(p => p.Signals))
        .Concat(hardRules.HardSignals)
        .Concat(notDowngraded)
        .Distinct()];

        logger.LogInformation(
            "Stream-анализ завершён: {Machine} {Date} → RequiresReview={R}, Confidence={C:F2}",
            request.Machine, request.ShiftDate, result.RequiresReview, result.Confidence);

        await Send("result", JsonSerializer.Serialize(result, _camelCase));
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
                SuggestExcludeFromReports = root.TryGetProperty("suggest_exclude_from_reports", out var se)
                    && se.ValueKind == JsonValueKind.Array
                    ? [.. se.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0)]
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