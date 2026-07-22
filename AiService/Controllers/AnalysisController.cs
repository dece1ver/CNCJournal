using AiService.Models;
using AiService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace AiService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController(OllamaService ollama, PromptBuilder promptBuilder, RequestLog requestLog, ILogger<AnalysisController> logger) : ControllerBase
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
            RequestShaper.Shape(request);
            var mastering = MasteringAutoApprover.Apply(request);
            if (mastering.RemovedSignals.Count > 0)
                logger.LogInformation(
                    "Освоение подтверждено детерминированно ({Machine} {Date}), сняты сигналы: {Signals}",
                    request.Machine, request.ShiftDate, string.Join(" | ", mastering.RemovedSignals));

            var hardRules = HardRuleEvaluator.Evaluate(request);

            var promptBuild = promptBuilder.Build(request, hardRules);
            var thinkCapture = new StringBuilder();

            var (raw, thinking) = await ollama.GenerateAsync(promptBuild.Prompt, think: false, thinkingProgress: null, ct: ct, model: request.Model);
            var llmResult = ParseResponse(raw);
            MergeAutoExcludes(llmResult, mastering.AutoExcludes);
            var notDowngraded = SoftSignalMatcher.GetNotDowngraded(
                hardRules.SoftSignals, llmResult.DowngradedSignals);

            var (filteredSignals, reset, removedSignals) = FalsePositiveFilter.Apply(
                request, hardRules, notDowngraded, llmResult.Signals);
            llmResult.Signals = filteredSignals;
            LogFilteredSignals(request, removedSignals, reset);

            if (reset)
            {
                llmResult.RequiresReview = false;
                llmResult.Explanation = "";
                llmResult.SuggestedReason = "";
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
                SuggestExcludeFromReports = FilterExcludeSuggestions(llmResult.SuggestExcludeFromReports, request),
                DowngradedSignals = llmResult.DowngradedSignals,
                PromptVersion = promptBuild.Version,
            };

            result.Signals = [.. llmResult.Signals
                .Concat(request.Signals)
                .Concat(CollectPartSignals(request, hardRules, notDowngraded))
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

            await requestLog.WriteAsync(request, result, "analyze");
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

    /// <summary>
    /// Фоновая проверка ОДНОЙ записи сутко-станка: релевантно ли комментарии мастера
    /// объясняют присланные клиентом аномалии. Совещательный контур (remeLog,
    /// фича AiMasterCheck): компактный промпт, без thinking, ответ {ok, remark}.
    /// Никакие пайплайны дневного анализа (hard rules, фильтры) здесь не участвуют.
    /// </summary>
    [HttpPost("verify-part")]
    public async Task<ActionResult<VerifyPartResponse>> VerifyPart(
        [FromBody] VerifyPartRequest request,
        CancellationToken ct)
    {
        try
        {
            if (request.Anomalies.Count == 0)
                return Ok(new VerifyPartResponse { Ok = true });

            var promptBuild = promptBuilder.BuildMasterCheck(request);

            var (raw, _) = await ollama.GenerateAsync(
                promptBuild.Prompt, think: false, thinkingProgress: null, ct: ct, model: request.Model);

            var result = ParseVerifyResponse(raw);
            result.PromptVersion = promptBuild.Version;

            logger.LogInformation(
                "Verify-part: {Machine} {Date} «{Part}» → Ok={Ok}{Remark}",
                request.Machine, request.ShiftDate, request.Part.PartName, result.Ok,
                result.Ok ? "" : $", remark: {result.Remark}");

            await requestLog.WriteVerifyAsync(request, result);
            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new VerifyPartResponse { Ok = true, Error = "Ollama не ответила за отведённое время" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка verify-part {Machine} {Date}", request.Machine, request.ShiftDate);
            return StatusCode(500, new VerifyPartResponse { Ok = true, Error = ex.Message });
        }
    }

    /// <summary>
    /// Парсинг ответа verify-part. Совещательная безопасность: непарсибельный ответ
    /// или отсутствие поля ok трактуются как Ok=true (+Error) — сбой модели не должен
    /// выглядеть замечанием мастеру.
    /// </summary>
    private static VerifyPartResponse ParseVerifyResponse(string raw)
    {
        var json = ExtractJson(raw);
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var ok) ||
                (ok.ValueKind != JsonValueKind.True && ok.ValueKind != JsonValueKind.False))
            {
                return new VerifyPartResponse { Ok = true, Error = "Ответ модели без поля ok" };
            }

            var remark = root.TryGetProperty("remark", out var r) ? r.GetString() ?? "" : "";
            return new VerifyPartResponse
            {
                Ok = ok.ValueKind == JsonValueKind.True || string.IsNullOrWhiteSpace(remark),
                Remark = ok.ValueKind == JsonValueKind.True ? "" : remark.Trim(),
            };
        }
        catch (JsonException)
        {
            return new VerifyPartResponse
            {
                Ok = true,
                Error = $"Не удалось распарсить ответ модели: {raw[..Math.Min(200, raw.Length)]}",
            };
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

        RequestShaper.Shape(request);
        var mastering = MasteringAutoApprover.Apply(request);
        if (mastering.RemovedSignals.Count > 0)
            logger.LogInformation(
                "Освоение подтверждено детерминированно ({Machine} {Date}), сняты сигналы: {Signals}",
                request.Machine, request.ShiftDate, string.Join(" | ", mastering.RemovedSignals));

        var hardRules = HardRuleEvaluator.Evaluate(request);
        var promptBuild = promptBuilder.Build(request, hardRules);

        logger.LogDebug("Промпт построен, символов: {Len}", promptBuild.Prompt.Length);

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

            // Думать или нет решает ТОЛЬКО request.EnableThinking; stream-эндпоинт — это
            // транспорт (SSE: очередь/размышления/результат), а не признак thinking.
            // Клиент remeLog вызывает /stream при включённом thinking и передаёт флаг.
            var generateTask = ollama.GenerateCoreAsync(promptBuild.Prompt, request.EnableThinking, progress, ct, model: request.Model);

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
        MergeAutoExcludes(llmResult, mastering.AutoExcludes);
        CheckThinkingConsistency(request, thinking, llmResult);

        logger.LogDebug("ParseResponse: RequiresReview={R}, Error={E}, ExcludeFromReports={ExcludeCount} [{ExcludeList}]",
            llmResult.RequiresReview, llmResult.Error ?? "(нет)",
            llmResult.SuggestExcludeFromReports.Count, string.Join(" | ", llmResult.SuggestExcludeFromReports));

        var notDowngraded = SoftSignalMatcher.GetNotDowngraded(
            hardRules.SoftSignals, llmResult.DowngradedSignals);

        var (filteredSignals, reset, removedSignals) = FalsePositiveFilter.Apply(
            request, hardRules, notDowngraded, llmResult.Signals);
        llmResult.Signals = filteredSignals;
        LogFilteredSignals(request, removedSignals, reset);

        if (reset)
        {
            llmResult.RequiresReview = false;
            llmResult.Explanation = "";
            llmResult.SuggestedReason = "";
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
            SuggestExcludeFromReports = FilterExcludeSuggestions(llmResult.SuggestExcludeFromReports, request),
            DowngradedSignals = llmResult.DowngradedSignals,
            Error = llmResult.Error,
            PromptVersion = promptBuild.Version,
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
        .Concat(CollectPartSignals(request, hardRules, notDowngraded))
        .Concat(hardRules.HardSignals)
        .Concat(notDowngraded)
        .Distinct()];

        logger.LogInformation(
            "Stream-анализ завершён: {Machine} {Date} → RequiresReview={R}, Confidence={C:F2}",
            request.Machine, request.ShiftDate, result.RequiresReview, result.Confidence);

        await requestLog.WriteAsync(request, result, "stream");
        await Send("result", JsonSerializer.Serialize(result, _camelCase));
    }

    /// <summary>
    /// Добавляет авто-предложения исключения (детерминированно подтверждённое
    /// освоение с КПД наладки &lt;100%) к предложениям модели, не дублируя строки,
    /// которые модель предложила сама (ключ — PartName§Setup§Order).
    /// </summary>
    private static void MergeAutoExcludes(AnalyzeResponse llmResult, List<string> autoExcludes)
    {
        foreach (var entry in autoExcludes)
        {
            var key = RowKey(entry);
            if (!llmResult.SuggestExcludeFromReports.Any(x => RowKey(x) == key))
                llmResult.SuggestExcludeFromReports.Add(entry);
        }
    }

    private static string RowKey(string excludeEntry) =>
        string.Join('§', excludeEntry.Split('§').Take(3));

    // Маркеры вывода об эскалации в самом конце рассуждения (не в середине —
    // там модель может гипотетически рассматривать «если бы это требовало
    // эскалации»; итог обычно в последних предложениях).
    private static readonly string[] EscalationConclusionMarkers =
    [
        "требует эскалац",
        "требуется эскалац",
        "необходима эскалация",
        "это требует эскалации",
    ];

    /// <summary>
    /// Грубый детектор рассинхрона между рассуждением модели (&lt;think&gt;) и
    /// финальным JSON: если в хвосте рассуждения модель заключает «требуется
    /// эскалация», а requires_review в структурированном ответе всё равно false —
    /// это НЕ баг FalsePositiveFilter (сигналов может вообще не быть, как в кейсе
    /// 2026-06-30 Rontek HTC650M: response.signals=[] с самого начала, модель просто
    /// не перенесла свой вывод в JSON), а самостоятельная нестабильность модели при
    /// сжатии длинного рассуждения в короткий структурированный ответ. Только
    /// логирование — эвристика по ключевым словам ненадёжна для авто-исправления.
    /// </summary>
    private void CheckThinkingConsistency(AnalyzeRequest request, string? thinking, AnalyzeResponse llmResult)
    {
        if (string.IsNullOrWhiteSpace(thinking) || llmResult.RequiresReview) return;

        var tail = thinking[(thinking.Length * 2 / 3)..];
        if (!EscalationConclusionMarkers.Any(m => tail.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return;

        logger.LogWarning(
            "Возможный рассинхрон рассуждения и ответа ({Machine} {Date}): " +
            "requires_review=false в JSON, но в конце рассуждения модель писала об эскалации. " +
            "Хвост рассуждения: {Tail}",
            request.Machine, request.ShiftDate, tail[..Math.Min(400, tail.Length)]);
    }

    /// <summary>
    /// Логирует КАЖДЫЙ сигнал, снятый FalsePositiveFilter, с указанием причины —
    /// иначе постфактум невозможно отличить «фильтр верно погасил галлюцинацию»
    /// от «фильтр по ошибке съел реальную аномалию» (см. память
    /// ai-analysis-improvement-plan, кейс 2026-06-30 Rontek HTC650M: сигнал про
    /// «кулачки» ошибочно попал под б/н-фильтр из-за других б/н-деталей того же дня).
    /// </summary>
    private void LogFilteredSignals(
        AnalyzeRequest request, List<(string Signal, string Reason)> removed, bool reset)
    {
        if (removed.Count == 0) return;

        foreach (var (signal, reason) in removed)
            logger.LogInformation(
                "Пост-фильтр снял сигнал ({Machine} {Date}): [{Reason}] {Signal}",
                request.Machine, request.ShiftDate, reason, signal);

        if (reset)
            logger.LogInformation(
                "Пост-фильтр сбросил requiresReview: {Machine} {Date} — " +
                "после отсева не осталось оснований для эскалации",
                request.Machine, request.ShiftDate);
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
        {
            var softBase = "Эскалация: объяснение мастера не подтверждено — "
                + string.Join("; ", notDowngraded) + ".";
            return string.IsNullOrWhiteSpace(llmResult.Explanation)
                ? softBase
                : softBase + " " + llmResult.Explanation;
        }

        if (!string.IsNullOrWhiteSpace(llmResult.Explanation))
            return llmResult.Explanation;

        return llmResult.HasError
            ? "Не удалось получить объяснение от модели (ошибка разбора ответа)."
            : "Явных отклонений не обнаружено.";
    }

    private static string FallbackReason(HardRuleResult hardRules, List<string> notDowngraded)
    {
        if (hardRules.MustEscalate) return hardRules.HardSignals.FirstOrDefault() ?? "Требует проверки";
        if (notDowngraded.Count > 0)
        {
            var kinds = notDowngraded.Select(SoftSignalMatcher.Classify).ToHashSet();
            if (kinds.Count == 1)
            {
                return kinds.First() switch
                {
                    SoftSignalMatcher.SignalKind.MachiningTime => "Машинное время превышает норматив",
                    SoftSignalMatcher.SignalKind.OperatorComplaint => "Жалоба оператора на норматив не опровергнута",
                    _ => "Объяснение мастера не подтверждено",
                };
            }
            return "Объяснение мастера не подтверждено";
        }
        return "Без замечаний";
    }

    /// <summary>
    /// Не предлагаем исключать строки, которые и так не участвуют в расчёте премии:
    /// наладка не считается при б/н или КПД=0, изготовление — при б/и, КПД=0 или
    /// штучной партии (пороги регламента). Если у строки нет ни одной участвующей
    /// категории, предложение исключить её бессмысленно и только отвлекает аналитика.
    /// </summary>
    private static List<string> FilterExcludeSuggestions(List<string> entries, AnalyzeRequest request)
    {
        if (entries.Count == 0) return entries;

        return [.. entries.Where(e =>
        {
            var seg = e.Split('§');
            if (seg.Length < 3) return true; // нераспознанный формат — не трогаем

            var part = request.Parts.FirstOrDefault(p =>
                p.PartName == seg[0]
                && p.Setup.ToString() == seg[1]
                && p.Order == seg[2]);
            return part == null || AffectsReports(part);
        })];
    }

    private static bool AffectsReports(PartContext p)
    {
        var setupCounts = !p.NoSetupHappened && p.SetupRatio is > 0;
        var productionCounts = !p.NoProductionHappened
            && p.ProductionRatio is > 0
            && !p.IsSmallBatch;
        return setupCounts || productionCounts;
    }

    /// <summary>
    /// Клиентские сигналы деталей для ответа. Если soft-сигнал по детали понижен
    /// моделью, его клиентский дубль («>= штучного норматива» / «Оператор сообщает
    /// о некорректном нормативе») тоже не должен попасть в ответ — иначе аналитик
    /// видит сигнал, который система уже сочла объяснённым.
    /// </summary>
    private static IEnumerable<string> CollectPartSignals(
        AnalyzeRequest request, HardRuleResult hardRules, List<string> notDowngraded)
    {
        var downgraded = hardRules.SoftSignals.Except(notDowngraded).ToList();
        if (downgraded.Count == 0)
            return request.Parts.SelectMany(p => p.Signals);

        HashSet<string> PartsOfKind(SoftSignalMatcher.SignalKind kind) => downgraded
            .Where(s => SoftSignalMatcher.Classify(s) == kind)
            .Select(SoftSignalMatcher.ExtractPartName)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var machiningParts = PartsOfKind(SoftSignalMatcher.SignalKind.MachiningTime);
        var operatorParts = PartsOfKind(SoftSignalMatcher.SignalKind.OperatorComplaint);

        return request.Parts.SelectMany(p => p.Signals.Where(s =>
            !(machiningParts.Contains(p.PartName) && SoftSignalMatcher.IsMachiningTimeEcho(s))
            && !(operatorParts.Contains(p.PartName) && SoftSignalMatcher.IsOperatorComplaintEcho(s))));
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