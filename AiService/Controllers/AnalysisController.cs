using AiService.Models;
using AiService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
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
        var sw = Stopwatch.StartNew();
        string? promptVersion = null;
        try
        {
            RequestShaper.Shape(request);
            var mastering = MasteringAutoApprover.Apply(request);
            if (mastering.RemovedSignals.Count > 0)
                logger.LogInformation(
                    "Освоение подтверждено детерминированно ({Machine} {Date}), сняты сигналы: {Signals}",
                    request.Machine, request.ShiftDate, string.Join(" | ", mastering.RemovedSignals));

            var hardRules = HardRuleEvaluator.Evaluate(request);
            var shiftRules = ShiftReportRuleEvaluator.Evaluate(request);

            var promptBuild = promptBuilder.Build(request, hardRules, shiftRules);
            promptVersion = promptBuild.Version;
            var thinkCapture = new StringBuilder();

            var (raw, thinking) = await ollama.GenerateAsync(promptBuild.Prompt, think: false, thinkingProgress: null, ct: ct, model: request.Model);
            var llmResult = ParseResponse(raw);
            // Отчётные эхо из общего signals — в shift-вопросы, до пост-фильтра.
            var (dataSignals, shiftEchoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(llmResult.Signals);
            llmResult.Signals = dataSignals;
            var modelShiftIssues = ShiftReportRuleEvaluator.CleanModelIssues(
                llmResult.ShiftReportIssues.Concat(shiftEchoes));
            // Требования комментария при самодостаточной причине простоя режутся
            // здесь же — до всех downstream-решений (reset, requiresReview, merge).
            (modelShiftIssues, var droppedCommentDemands) =
                ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(request, modelShiftIssues);
            (modelShiftIssues, var droppedReasonDemands) =
                ShiftReportRuleEvaluator.DropUnneededReasonDemands(request, modelShiftIssues);
            // Выдумки про отчёт, которого нет («устарел», «мастер не указан»),
            // режутся здесь же — триггер ReportExists тот же, что у иконок главной.
            (modelShiftIssues, var droppedNoReport) =
                ShiftReportRuleEvaluator.DropIssuesWithoutReport(request, modelShiftIssues);
            MergeAutoExcludes(llmResult, mastering.AutoExcludes);
            var notDowngraded = SoftSignalMatcher.GetNotDowngraded(
                hardRules.SoftSignals, llmResult.DowngradedSignals);

            var (filteredSignals, reset, removedSignals) = FalsePositiveFilter.Apply(
                request, hardRules, notDowngraded, llmResult.Signals);
            llmResult.Signals = filteredSignals;
            removedSignals.AddRange(droppedCommentDemands
                .Select(d => (d, "причина простоя самодостаточна, комментарий не требуется")));
            removedSignals.AddRange(droppedReasonDemands
                .Select(d => (d, "причина простоя не требуется (порог 10%) или уже указана")));
            removedSignals.AddRange(droppedNoReport
                .Select(d => (d, "отчёта нет — проверять нечего")));
            LogFilteredSignals(request, removedSignals, reset);

            // Сброс пост-фильтра не действует при вопросах к отчёту мастера:
            // детерминированные hard и несогласия модели (S1) фильтр не рассматривает.
            var resetEffective = reset && !shiftRules.MustEscalate && modelShiftIssues.Count == 0;
            if (resetEffective)
            {
                llmResult.RequiresReview = false;
                llmResult.Explanation = "";
                llmResult.SuggestedReason = "";
            }

            // llmResult.HasError (сбой парсинга/вырожденная генерация модели — см.
            // память ai-analysis-improvement-plan, кейс 2026-06-30 Rontek HTC650M:
            // модель зациклилась на повторе одной фразы, съела весь NumPredict до
            // JSON) ВСЕГДА форсирует эскалацию — сбой системы должен привлечь
            // внимание аналитика, а не молча выглядеть как «Всё в порядке».
            var requiresReview = resetEffective ? false
                : hardRules.MustEscalate || shiftRules.MustEscalate || modelShiftIssues.Count > 0 || notDowngraded.Count > 0
                    || llmResult.RequiresReview || llmResult.HasError;

            logger.LogDebug(
                "ДИАГНОСТИКА {Machine} {Date}: " +
                "hardRules.MustEscalate={MustEscalate}, hardRules.HardSignals=[{HardSignals}], " +
                "hardRules.SoftSignals=[{SoftSignals}], notDowngraded=[{NotDowngraded}], " +
                "shiftRules.HardSignals=[{ShiftHard}], shiftRules.SoftSignals=[{ShiftSoft}], " +
                "llmResult.RequiresReview={LlmRR}, llmResult.HasError={LlmErr}, llmResult.Error={LlmErrMsg}, " +
                "llmResult.DowngradedSignals=[{LlmDowngraded}], llmResult.ExcludeFromReports={ExcludeCount} [{ExcludeList}] → итоговый requiresReview={Final}",
                request.Machine, request.ShiftDate,
                hardRules.MustEscalate, string.Join(" | ", hardRules.HardSignals),
                string.Join(" | ", hardRules.SoftSignals), string.Join(" | ", notDowngraded),
                string.Join(" | ", shiftRules.HardSignals), string.Join(" | ", shiftRules.SoftSignals),
                llmResult.RequiresReview, llmResult.HasError, llmResult.Error ?? "(нет)",
                string.Join(" | ", llmResult.DowngradedSignals),
                llmResult.SuggestExcludeFromReports.Count, string.Join(" | ", llmResult.SuggestExcludeFromReports),
                requiresReview);

            var result = new AnalyzeResponse
            {
                RequiresReview = requiresReview,
                Confidence = ShiftReportRuleEvaluator.ComputeConfidence(
                    hardRules.MustEscalate, shiftRules.MustEscalate, llmResult.HasError, llmResult.Confidence),
                Explanation = EnsureExplanation(llmResult, hardRules, shiftRules, modelShiftIssues, notDowngraded),
                SuggestedReason = string.IsNullOrWhiteSpace(llmResult.SuggestedReason)
                    ? FallbackReason(hardRules, shiftRules, modelShiftIssues, notDowngraded, llmResult.HasError)
                    : llmResult.SuggestedReason,
                Error = llmResult.Error,
                SuggestExcludeFromReports = FilterExcludeSuggestions(llmResult.SuggestExcludeFromReports, request),
                DowngradedSignals = llmResult.DowngradedSignals,
                PromptVersion = promptBuild.Version,
            };

            result.ShiftReportIssues = ShiftReportRuleEvaluator.MergeIssues(shiftRules, modelShiftIssues);
            result.ShiftReportSummary = ShiftReportRuleEvaluator.Summarize(request, shiftRules);
            // В общий Signals — только выжившие модельные вопросы: срезанные дедупом
            // дубли иначе остаются видимыми диалогу (Except их не находит).
            var shiftSurvivors = ShiftReportRuleEvaluator.ModelSurvivors(shiftRules, result.ShiftReportIssues);

            result.Signals = [.. llmResult.Signals
                .Concat(request.Signals)
                .Concat(CollectPartSignals(request, hardRules, notDowngraded))
                .Concat(hardRules.HardSignals)
                .Concat(shiftRules.HardSignals)
                .Concat(shiftRules.SoftSignals)
                .Concat(shiftSurvivors)
                .Concat(notDowngraded)
                .Distinct()];

            // Вердикт модели зажигает «Данные», только если она назвала проблемы
            // по данным (почищенные сигналы непусты). Формула эскалации не меняется.
            var llmDataCaused = ShiftReportRuleEvaluator.LlmDataCaused(llmResult.RequiresReview, llmResult.Signals);
            var (dataCaused, shiftCaused) = ShiftReportRuleEvaluator.EscalationCauses(
                hardRules, notDowngraded, llmDataCaused, llmResult.HasError,
                resetEffective, shiftRules, modelShiftIssues);
            result.EscalatedByData = dataCaused;
            result.EscalatedByShiftReport = shiftCaused;

            if (ShiftReportRuleEvaluator.ShiftOnlyExplanation(dataCaused, shiftCaused) is { } cleanExplanation)
                result.Explanation = cleanExplanation;

            // Чистый отчёт мастера фиксируем прямо в объяснении: блок «Отчёт мастера»
            // в UI виден всегда, и пакетное окно (там только Explanation) тоже показывает
            // факт проверки. Только когда клиент реально прислал отчёты — иначе враньё.
            if (result.ShiftReportIssues.Count == 0 && request.ShiftReports.Count > 0)
                result.Explanation = (result.Explanation + " Отчёт мастера проверен, замечаний нет.").Trim();

            result.FlaggedParts = CollectFlaggedPartKeys(hardRules, notDowngraded, request, llmResult.Signals);

            logger.LogInformation(
                "Анализ: {Machine} {Date} → RequiresReview={R} (hard={H}, softNotDowngraded={S}, shiftHard={SH}, shiftSoft={SS}), Confidence={C:F2}, FlaggedParts={F}",
                request.Machine, request.ShiftDate, result.RequiresReview,
                hardRules.HardSignals.Count, notDowngraded.Count,
                shiftRules.HardSignals.Count, shiftRules.SoftSignals.Count,
                result.Confidence, result.FlaggedParts.Count);

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
        catch (DegenerateGenerationException ex)
        {
            // Петля в рассуждении (в т.ч. после ретрая): fail-safe эскалация,
            // как при HasError — сбой системы должен привлечь внимание аналитика.
            logger.LogWarning(ex, "Вырожденная генерация {Machine} {Date}", request.Machine, request.ShiftDate);
            var degResult = DegenerateEscalation(ex, promptVersion);
            await requestLog.WriteAsync(request, degResult, "analyze");
            return Ok(degResult);
        }
        catch (TaskCanceledException ex)
        {
            await requestLog.WriteFailureAsync(request.Machine, request.ShiftDate, "analyze", request.Model, think: false, sw.ElapsedMilliseconds, ex);
            return StatusCode(504, new AnalyzeResponse { Error = "Ollama не ответила за отведённое время" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при анализе {Machine} {Date}", request.Machine, request.ShiftDate);
            await requestLog.WriteFailureAsync(request.Machine, request.ShiftDate, "analyze", request.Model, think: false, sw.ElapsedMilliseconds, ex);
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
        var sw = Stopwatch.StartNew();
        try
        {
            if (request.Anomalies.Count == 0)
                return Ok(new VerifyPartResponse { Ok = true });

            // Аномалии, целиком покрытые самодостаточными причинами («Освоение»,
            // «Отсутствие/Некорректные нормативов»), не требуют обращения к модели —
            // см. MasteringAutoApprover.IsAnomalyFieldSelfSufficient.
            if (request.Anomalies.All(a => MasteringAutoApprover.IsAnomalyFieldSelfSufficient(a.Field, request.Part)))
            {
                var autoResult = new VerifyPartResponse { Ok = true, PromptVersion = "auto-approved" };
                logger.LogInformation(
                    "Verify-part: {Machine} {Date} «{Part}» → Ok=true (авто, причина самодостаточна)",
                    request.Machine, request.ShiftDate, request.Part.PartName);
                await requestLog.WriteVerifyAsync(request, autoResult);
                return Ok(autoResult);
            }

            var promptBuild = promptBuilder.BuildMasterCheck(request);

            // Температура 0.05: подсказка мастеру обязана быть детерминированной —
            // одинаковый вход даёт одинаковый вердикт (дневной анализ остаётся на 0.1).
            var (raw, _) = await ollama.GenerateAsync(
                promptBuild.Prompt, think: false, thinkingProgress: null, ct: ct, model: request.Model,
                temperature: 0.05);

            var result = ParseVerifyResponse(raw);
            result.PromptVersion = promptBuild.Version;

            logger.LogInformation(
                "Verify-part: {Machine} {Date} «{Part}» → Ok={Ok}{Remark}",
                request.Machine, request.ShiftDate, request.Part.PartName, result.Ok,
                result.Ok ? "" : $", remark: {result.Remark}");

            await requestLog.WriteVerifyAsync(request, result);
            return Ok(result);
        }
        catch (TaskCanceledException ex)
        {
            await requestLog.WriteFailureAsync(request.Machine, request.ShiftDate, "verify-part", request.Model, think: false, sw.ElapsedMilliseconds, ex);
            return StatusCode(504, new VerifyPartResponse { Ok = true, Error = "Ollama не ответила за отведённое время" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка verify-part {Machine} {Date}", request.Machine, request.ShiftDate);
            await requestLog.WriteFailureAsync(request.Machine, request.ShiftDate, "verify-part", request.Model, think: false, sw.ElapsedMilliseconds, ex);
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
        var shiftRules = ShiftReportRuleEvaluator.Evaluate(request);
        var promptBuild = promptBuilder.Build(request, hardRules, shiftRules);

        logger.LogDebug("Промпт построен, символов: {Len}", promptBuild.Prompt.Length);

        var channel = System.Threading.Channels.Channel.CreateBounded<string>(
            new System.Threading.Channels.BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            });

        // Дедупликации по равенству дельт здесь нет сознательно: повторяющаяся
        // дельта (например, одиночный пробел между словами) — валидная часть
        // потока, её пропуск склеивал слова в UI. Дубликатов от Ollama не бывает.
        var progress = new Progress<string>(thought =>
        {
            logger.LogDebug("Think: {T}", thought[..Math.Min(50, thought.Length)]);
            channel.Writer.TryWrite(thought);
        });

        string raw;
        string? thinking;
        int queuePosition;
        var sw = Stopwatch.StartNew();

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
        catch (DegenerateGenerationException ex)
        {
            logger.LogWarning(ex, "Вырожденная генерация {Machine} {Date}", request.Machine, request.ShiftDate);
            var degResult = DegenerateEscalation(ex, promptBuild.Version);
            await requestLog.WriteAsync(request, degResult, "stream");
            await Send("result", JsonSerializer.Serialize(degResult, _camelCase));
            return;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning("Отменён: {Machine} {Date}", request.Machine, request.ShiftDate);
            await requestLog.WriteFailureAsync(request.Machine, request.ShiftDate, "stream", request.Model, request.EnableThinking, sw.ElapsedMilliseconds, ex);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка: {Machine} {Date}", request.Machine, request.ShiftDate);
            await requestLog.WriteFailureAsync(request.Machine, request.ShiftDate, "stream", request.Model, request.EnableThinking, sw.ElapsedMilliseconds, ex);
            await Send("error", JsonSerializer.Serialize(ex.Message));
            return;
        }
        finally
        {
            ollama.LeaveQueue();
        }

        var llmResult = ParseResponse(raw);
        // Отчётные эхо из общего signals — в shift-вопросы, до пост-фильтра.
        var (dataSignals, shiftEchoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(llmResult.Signals);
        llmResult.Signals = dataSignals;
        var modelShiftIssues = ShiftReportRuleEvaluator.CleanModelIssues(
            llmResult.ShiftReportIssues.Concat(shiftEchoes));
        // Требования комментария при самодостаточной причине простоя режутся
        // здесь же — до всех downstream-решений (reset, requiresReview, merge).
        (modelShiftIssues, var droppedCommentDemands) =
            ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(request, modelShiftIssues);
            (modelShiftIssues, var droppedReasonDemands) =
                ShiftReportRuleEvaluator.DropUnneededReasonDemands(request, modelShiftIssues);
            // Выдумки про отчёт, которого нет («устарел», «мастер не указан»),
            // режутся здесь же — триггер ReportExists тот же, что у иконок главной.
            (modelShiftIssues, var droppedNoReport) =
                ShiftReportRuleEvaluator.DropIssuesWithoutReport(request, modelShiftIssues);
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
        removedSignals.AddRange(droppedCommentDemands
            .Select(d => (d, "причина простоя самодостаточна, комментарий не требуется")));
            removedSignals.AddRange(droppedReasonDemands
                .Select(d => (d, "причина простоя не требуется (порог 10%) или уже указана")));
            removedSignals.AddRange(droppedNoReport
                .Select(d => (d, "отчёта нет — проверять нечего")));
            LogFilteredSignals(request, removedSignals, reset);

            // Сброс пост-фильтра не действует при вопросах к отчёту мастера — см. Analyze().
        var resetEffective = reset && !shiftRules.MustEscalate && modelShiftIssues.Count == 0;
        if (resetEffective)
        {
            llmResult.RequiresReview = false;
            llmResult.Explanation = "";
            llmResult.SuggestedReason = "";
        }

        // llmResult.HasError форсирует эскалацию — см. комментарий в Analyze().
        var requiresReview = resetEffective ? false
            : hardRules.MustEscalate || shiftRules.MustEscalate || modelShiftIssues.Count > 0 || notDowngraded.Count > 0
                || llmResult.RequiresReview || llmResult.HasError;

        var result = new AnalyzeResponse
        {
            RequiresReview = requiresReview,
            Confidence = ShiftReportRuleEvaluator.ComputeConfidence(
                hardRules.MustEscalate, shiftRules.MustEscalate, llmResult.HasError, llmResult.Confidence),
            Explanation = EnsureExplanation(llmResult, hardRules, shiftRules, modelShiftIssues, notDowngraded),
            SuggestedReason = string.IsNullOrWhiteSpace(llmResult.SuggestedReason)
                ? FallbackReason(hardRules, shiftRules, modelShiftIssues, notDowngraded, llmResult.HasError) : llmResult.SuggestedReason,
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

        result.ShiftReportIssues = ShiftReportRuleEvaluator.MergeIssues(shiftRules, modelShiftIssues);
        result.ShiftReportSummary = ShiftReportRuleEvaluator.Summarize(request, shiftRules);
        // В общий Signals — только выжившие модельные вопросы (см. Analyze()).
        var shiftSurvivors = ShiftReportRuleEvaluator.ModelSurvivors(shiftRules, result.ShiftReportIssues);

        result.Signals = [.. llmResult.Signals
        .Concat(request.Signals)
        .Concat(CollectPartSignals(request, hardRules, notDowngraded))
        .Concat(hardRules.HardSignals)
        .Concat(shiftRules.HardSignals)
        .Concat(shiftRules.SoftSignals)
        .Concat(shiftSurvivors)
        .Concat(notDowngraded)
        .Distinct()];

        var llmDataCaused = ShiftReportRuleEvaluator.LlmDataCaused(llmResult.RequiresReview, llmResult.Signals);
        var (dataCaused, shiftCaused) = ShiftReportRuleEvaluator.EscalationCauses(
            hardRules, notDowngraded, llmDataCaused, llmResult.HasError,
            resetEffective, shiftRules, modelShiftIssues);
        result.EscalatedByData = dataCaused;
        result.EscalatedByShiftReport = shiftCaused;

        if (ShiftReportRuleEvaluator.ShiftOnlyExplanation(dataCaused, shiftCaused) is { } cleanExplanation)
            result.Explanation = cleanExplanation;

        if (result.ShiftReportIssues.Count == 0 && request.ShiftReports.Count > 0)
            result.Explanation = (result.Explanation + " Отчёт мастера проверен, замечаний нет.").Trim();

        result.FlaggedParts = CollectFlaggedPartKeys(hardRules, notDowngraded, request, llmResult.Signals);

        logger.LogInformation(
            "Stream-анализ завершён: {Machine} {Date} → RequiresReview={R}, Confidence={C:F2}, FlaggedParts={F}, ShiftIssues={SI}",
            request.Machine, request.ShiftDate, result.RequiresReview, result.Confidence,
            result.FlaggedParts.Count, result.ShiftReportIssues.Count);

        await requestLog.WriteAsync(request, result, "stream");
        await Send("result", JsonSerializer.Serialize(result, _camelCase));
    }

    /// <summary>
    /// Fail-safe вердикт при вырожденной генерации (петля в reasoning, в т.ч.
    /// после ретрая): день уходит на проверку, как при HasError. Confidence 1.0 —
    /// система уверена, что человеку надо посмотреть, а не в содержании вердикта.
    /// </summary>
    private static AnalyzeResponse DegenerateEscalation(DegenerateGenerationException ex, string? promptVersion) => new()
    {
        RequiresReview = true,
        Confidence = 1.0,
        Explanation = "Автоматическая эскалация: модель зациклилась при рассуждении.",
        SuggestedReason = "Сбой анализа — требуется ручная проверка",
        Error = ex.Message,
        PromptVersion = promptVersion,
    };

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

    /// <summary>
    /// Объяснение — только про данные записей. Детали по суточному отчёту живут
    /// в отдельном UI-блоке (ShiftReportIssues/ShiftReportSummary), сюда добавляется
    /// лишь одна фраза-ссылка, чтобы вердикт «требует проверки» не висел без пояснения.
    /// </summary>
    private static string EnsureExplanation(
    AnalyzeResponse llmResult, HardRuleResult hardRules, ShiftReportRuleResult shiftRules, List<string> modelShiftIssues, List<string> notDowngraded)
    {
        string explanation;
        if (hardRules.MustEscalate)
        {
            // Только счётчик: сами правила уже лежат в signals буллетами ниже,
            // дублировать их текстом не нужно.
            var hardBase = $"Эскалация по жёстким правилам ({hardRules.HardSignals.Count}).";
            explanation = !string.IsNullOrWhiteSpace(llmResult.Explanation)
                && !llmResult.Explanation.Contains("отсутствует")
                && !llmResult.Explanation.Contains("нет объяснения")
                && !llmResult.Explanation.Contains("не объяснен")
                && !ExplanationHygiene.EnumeratesRules(llmResult.Explanation)
                ? hardBase + " " + llmResult.Explanation
                : hardBase;
        }
        else if (notDowngraded.Count > 0)
        {
            var softBase = "Эскалация: объяснение мастера не подтверждено "
                + $"({notDowngraded.Count}).";
            explanation = !string.IsNullOrWhiteSpace(llmResult.Explanation)
                && !ExplanationHygiene.EnumeratesRules(llmResult.Explanation)
                ? softBase + " " + llmResult.Explanation
                : softBase;
        }
        else if (!string.IsNullOrWhiteSpace(llmResult.Explanation))
        {
            explanation = llmResult.Explanation;
        }
        else
        {
            explanation = llmResult.HasError
                ? "Не удалось получить объяснение от модели (ошибка разбора ответа)."
                : "Явных отклонений не обнаружено.";
        }

        if ((shiftRules.MustEscalate || modelShiftIssues.Count > 0)
            && !explanation.Contains("отчёту мастера"))
            explanation = (explanation + " Есть вопросы к суточному отчёту мастера.").Trim();

        return explanation;
    }

    private static string FallbackReason(HardRuleResult hardRules, ShiftReportRuleResult shiftRules, List<string> modelShiftIssues, List<string> notDowngraded, bool hasError = false)
    {
        if (hardRules.MustEscalate) return hardRules.HardSignals.FirstOrDefault() ?? "Требует проверки";
        if (shiftRules.MustEscalate) return shiftRules.HardSignals.FirstOrDefault() ?? "Требует проверки";
        if (hasError) return "Сбой анализа — требуется ручная проверка";
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
        if (modelShiftIssues.Count > 0) return modelShiftIssues[0];
        return "Без замечаний";
    }

    /// <summary>
    /// Не предлагаем исключать строки, которые и так не участвуют в расчёте премии:
    /// наладка не считается при б/н или КПД=0, изготовление — при б/и, КПД=0 или
    /// штучной партии (пороги регламента). Если у строки нет ни одной участвующей
    /// категории, предложение исключить её бессмысленно и только отвлекает аналитика.
    /// Триггерные причины («освоение», «работа ученика») без подтверждающих отметок —
    /// выдумка модели: галка по умолчанию стоит, неверное основание превратилось бы
    /// в неверное исключение из К1 (см. <see cref="ExcludeTriggerValidator"/>).
    /// </summary>
    private static List<string> FilterExcludeSuggestions(List<string> entries, AnalyzeRequest request)
    {
        if (entries.Count == 0) return entries;

        return [.. entries.Where(e =>
        {
            var seg = e.Split('§');
            if (seg.Length < 3) return true; // нераспознанный формат — не трогаем

            var part = ExcludeTriggerValidator.FindPart(request.Parts, seg[0], seg[1], seg[2]);
            if (part == null) return true; // консервативно: клиент покажет непривязанной
            if (!AffectsReports(part)) return false;
            var reason = string.Join("§", seg.Skip(3));
            return ExcludeTriggerValidator.HasGrounds(part, reason);
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

    /// <summary>
    /// Ключи строк, которые ИИ предлагает отметить проблемными (флаги СГТ):
    /// все детали с hard-сигналами + детали, чьи soft-сигналы модель не понизила,
    /// + детали, явно названные в уцелевших сигналах модели (иначе вердикт
    /// «про Гильзу» не подсвечивает ни одну строку — кейс 22.09.2026 Mazak).
    /// Пониженные soft-сигналы считаются объяснёнными — их детали не флагуются.
    /// Несопоставленные имена (галлюцинации) игнорируются; одно имя на
    /// нескольких установках флагуются все строки — лишнее аналитик снимет.
    /// </summary>
    public static List<string> CollectFlaggedPartKeys(
        HardRuleResult hardRules, List<string> notDowngraded,
        AnalyzeRequest request, IEnumerable<string> modelSignals)
    {
        var surviving = notDowngraded.ToHashSet();
        var namedKeys = modelSignals
            .SelectMany(s =>
            {
                var lower = s.ToLowerInvariant();
                return request.Parts
                    .Where(p => p.PartName.Trim().Length > 0
                        && lower.Contains(p.PartName.Trim().ToLowerInvariant()))
                    .Select(HardRuleEvaluator.PartKey);
            });
        return [.. hardRules.HardFlaggedPartKeys
            .Concat(hardRules.SoftFlagged
                .Where(t => surviving.Contains(t.Signal))
                .Select(t => t.PartKey))
            .Concat(namedKeys)
            .Distinct()];
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
                ShiftReportIssues = root.TryGetProperty("shift_report_issues", out var sri) && sri.ValueKind == JsonValueKind.Array
                    ? [.. sri.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0)]
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