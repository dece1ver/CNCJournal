using AiService.Models;
using AiService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;

namespace AiService.Controllers;

/// <summary>
/// Чат-ДОЗОР eLog (кнопка ИИ-помощника на главном окне).
/// Модель НЕ получает контекст смены в системном промпте: кто её оператор
/// и что он делает, она узнаёт САМА инструментами ChatTools через агентский
/// цикл ChatAgentService (максимум Chat:AgentMaxIterations раундов).
/// POST /api/chat — один ответ целиком; POST /api/chat/stream — SSE
/// (события token/tool/result/error). Веб-поиск — детерминированный retrieval
/// ДО модели (см. WebSearchService), сниппеты приклеиваются к вопросу.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController(
    ChatAgentService agent,
    PromptBuilder promptBuilder,
    RequestLog requestLog,
    WebSearchService webSearch,
    IAgentDataSource dataSource,
    IConfiguration config,
    ILogger<ChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Chat(
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (request.Messages.Count == 0)
                return BadRequest(new ChatResponse { Error = "Пустой диалог: нет сообщений" });

            await AugmentGreetingAsync(request, ct);
            // Порядок важен: сначала веб-решение по ОРИГИНАЛУ вопроса, и только
            // потом календарная подсказка. Иначе «2026» из подсказки сам включает
            // NeedsWeb и пред-поиск стреляет по любому вопросу про прошлое.
            var webResults = await CollectWebAsync(request, ct);
            AppendCalendarHint(request);
            var build = BuildChatMessages(request, webResults);

            var outcome = await agent.RunAsync(
                build.System, build.Messages, request.Context,
                model: request.Model, temperature: request.Temperature,
                toolProgress: null, tokenProgress: null, ct: ct,
                think: request.Think);

            if (outcome.Error != null)
            {
                // Ответ не собрался (модель молчала или упала) — это не сбой сервиса,
                // а исход модели: отдаём 200 с текстом, иначе клиент покажет оператору
                // «получить ответ не удалось» вместо внятной просьбы повторить.
                logger.LogWarning(
                    "Чат ({Machine}): ответ не собран — {Reason}",
                    request.Context?.Machine ?? "(chat)", outcome.Error);
                return Ok(new ChatResponse { Reply = outcome.Error, PromptVersion = build.Version });
            }

            var response = ToResponse(request, build.Version, webResults, outcome);
            logger.LogInformation(
                "Чат ({Machine}): {N} сообщений, tools {T}, web={Web} → {ReplyLen} символов за {Ms}мс",
                request.Context?.Machine ?? "(без контекста)", request.Messages.Count,
                outcome.ToolRounds, response.WebUsed, response.Reply.Length, sw.ElapsedMilliseconds);

            await requestLog.WriteChatAsync(request, response);
            return Ok(response);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Таймаут внутри агента уже превращается в Error выше; сюда попадаем
            // только при обрыве соединения — фиксируем как сбой.
            await requestLog.WriteFailureAsync(
                request.Context?.Machine ?? "(chat)", "chat", "chat",
                request.Model, think: false, sw.ElapsedMilliseconds, ex);
            return StatusCode(504, new ChatResponse { Error = "Ollama не ответила за отведённое время" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка чата");
            await requestLog.WriteFailureAsync(
                request.Context?.Machine ?? "(chat)", "chat", "chat",
                request.Model, think: false, sw.ElapsedMilliseconds, ex);
            return StatusCode(500, new ChatResponse { Error = ex.Message });
        }
    }

    [HttpPost("stream")]
    public async Task ChatStream(
        [FromBody] ChatRequest request,
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

        var sw = Stopwatch.StartNew();
        var machine = request.Context?.Machine ?? "(без контекста)";
        logger.LogInformation("Чат-стрим начат: {Machine}, {N} сообщений", machine, request.Messages.Count);

        if (request.Messages.Count == 0)
        {
            await Send("error", JsonSerializer.Serialize("Пустой диалог: нет сообщений"));
            return;
        }

        await AugmentGreetingAsync(request, ct);
        // Порядок важен (как в Chat): веб-решение по оригиналу вопроса,
        // календарная подсказка — после, иначе «2026» включает NeedsWeb.
        var webResults = await CollectWebAsync(request, ct);
        AppendCalendarHint(request);
        var build = BuildChatMessages(request, webResults);

        // Живые шаги инструментов — SSE-событиями: UI показывает их в статусе
        // («⚙ смотрю смену…»). Синхронный прогресс: Progress<T> постит в пул,
        // финалы терялись (см. AnalysisController).
        var toolChannel = System.Threading.Channels.Channel.CreateBounded<string>(
            new System.Threading.Channels.BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            });
        var toolProgress = new SyncProgress(t => toolChannel.Writer.TryWrite(t));
        var drainToolsTask = Task.Run(async () =>
        {
            await foreach (var step in toolChannel.Reader.ReadAllAsync(ct))
                await Send("tool", JsonSerializer.Serialize(step));
        }, ct);

        ChatAgentResult outcome;
        try
        {
            outcome = await agent.RunAsync(
                build.System, build.Messages, request.Context,
                model: request.Model, temperature: request.Temperature,
                toolProgress: toolProgress, tokenProgress: null, ct: ct,
                think: request.Think);
        }
        catch (OperationCanceledException ex)
        {
            toolChannel.Writer.Complete();
            await drainToolsTask;
            logger.LogWarning("Чат-стрим отменён: {Machine}", machine);
            await requestLog.WriteFailureAsync(
                machine, "chat", "chat/stream", request.Model, think: false, sw.ElapsedMilliseconds, ex);
            return;
        }
        catch (Exception ex)
        {
            toolChannel.Writer.Complete();
            await drainToolsTask;
            logger.LogError(ex, "Ошибка чат-стрима: {Machine}", machine);
            await Send("error", JsonSerializer.Serialize(ex.Message));
            return;
        }

        toolChannel.Writer.Complete();
        await drainToolsTask;

        if (outcome.Error != null)
        {
            await Send("error", JsonSerializer.Serialize(outcome.Error));
            return;
        }

        var reply = outcome.Reply;
        if (string.IsNullOrWhiteSpace(reply))
            reply = "Не получилось сформулировать ответ — попробуйте переформулировать вопрос.";

        var response = ToResponse(request, build.Version, webResults, outcome, reply);
        logger.LogInformation(
            "Чат-стрим завершён: {Machine}, tools {T}, web={Web} → {ReplyLen} символов за {Ms}мс",
            machine, outcome.ToolRounds, response.WebUsed, reply.Length, sw.ElapsedMilliseconds);

        await requestLog.WriteChatAsync(request, response);
        // Текст — одним событием и уже финальный: раунды агента не стримятся
        // (зачинка «сейчас посмотрю…» — не ответ), очистка markdown и блок
        // источников применяются здесь. Иначе клиент печатал бы черновик,
        // а потом result его затирал.
        await Send("token", JsonSerializer.Serialize(response.Reply, _camelCase));
        await Send("result", JsonSerializer.Serialize(response, _camelCase));
    }

    /// <summary>
    /// Детерминированные факты в автостарт-триггер: первое user-сообщение вида
    /// «[Автостарт] …» (открытие чата в eLog) дописывается ФИО из базы И снимком
    /// смены (станок, смена, активная деталь). Причина — модель отвечает на
    /// приветствие с tools=0 и выдумывает станок/смену/деталь (кейс «CNC-205,
    /// вечерняя, Шестерня 123» при Mazak QTS350/День/«опять тест»). Имя без базы
    /// не расшифровывается («Крафт А. С.» → «Афанасий Семёнович»), это правится
    /// только кодом. Агентский путь дальше штатный: инструменты для уточнений.
    /// </summary>
    private async Task AugmentGreetingAsync(ChatRequest request, CancellationToken ct)
    {
        var first = request.Messages.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.Content)
            && !(m.Role ?? "").Trim().Equals("assistant", StringComparison.OrdinalIgnoreCase));
        if (first == null || !first.Content.StartsWith("[Автостарт]")) return;
        var contextName = request.Context?.OperatorName;
        if (string.IsNullOrWhiteSpace(contextName)) return;
        var found = await dataSource.FindOperatorsAsync(contextName.Trim(), ct);
        first.Content += ChatAgentService.GreetingOperatorFact(contextName, found);
        // Снимок смены текстом: даже если модель не вызовет инструменты,
        // приветствие соберётся из фактов, а не из головы.
        first.Content += "\n\n" + PromptBuilder.RenderElogContext(request.Context);
        // Имя «заполнить по форме» модель сокращает до инициалов — требуем
        // дословную первую строку с уже резолвленным именем.
        first.Content += "\n\nНачни ответ ДОСЛОВНО с этой строки (не перефразировать): " +
            $"«Здравствуйте, {ChatAgentService.GreetingDisplayName(contextName, found)}!»";
        // Выбрана ≠ активна: без этой строки модель говорит «вы работаете
        // в смену День», хотя смена только выбрана в комбобоксе. Клиентский
        // триггер приветствия переведён на нейтральную формулировку, чтобы два
        // указания не спорили друг с другом.
        if (request.Context is { ShiftStarted: false })
            first.Content += "\n\nСмена НЕ начата: не пиши «вы работаете», «вы на смене» " +
                "и «ваша смена идёт». Скажи, что смена выбрана, но не начата, " +
                "и предложи начать смену.";
        logger.LogInformation("Чат ({Machine}): автостарт, операторов в базе {N}",
            request.Context?.Machine ?? "(без контекста)", found.Count);
    }

    /// <summary>
    /// Календарная подсказка к последнему вопросу: относительные дни
    /// («вчера», «во вторник») резолвит сервер — модель считает наугад.
    /// </summary>
    private static void AppendCalendarHint(ChatRequest request)
    {
        var last = request.Messages.LastOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.Content)
            && !(m.Role ?? "").Trim().Equals("assistant", StringComparison.OrdinalIgnoreCase));
        if (last == null) return;
        var hint = ChatAgentService.CalendarHint(last.Content, DateTime.Today);
        if (!string.IsNullOrEmpty(hint))
            last.Content += "\n\n" + hint;
    }

    /// <summary>
    /// Веб-поиск по последнему вопросу пользователя: явная галка клиента
    /// AllowWebSearch ИЛИ серверная эвристика NeedsWeb — если поиск включён
    /// в конфиге. Fail-open: недоступность поиска (см. WebSearchService) —
    /// просто пустой список, агент отвечает без сети.
    /// </summary>
    private async Task<IReadOnlyList<WebSearchResult>> CollectWebAsync(
        ChatRequest request, CancellationToken ct)
    {
        if (!webSearch.Enabled) return [];
        // Запрос для поиска — с учётом контекста: «а так?» ищется вместе
        // с предыдущим вопросом, иначе в сниппетах мусор (см. BuildSearchQuery).
        var query = WebSearchService.BuildSearchQuery(request);
        if (string.IsNullOrWhiteSpace(query)) return [];
        // Короткий вопрос («а я?», «а так?» без истории) — искать нечего,
        // SearXNG вернёт мусор. Такие решает агент инструментами.
        if (query.Trim().Length < 10)
        {
            logger.LogInformation("Чат ({Machine}): слишком короткий запрос, пред-поиск пропущен",
                request.Context?.Machine ?? "(без контекста)");
            return [];
        }
        // Поправка модели («наврал», «неправда») — про наши данные, сеть не поможет,
        // а мусорные сниппеты уводят ответ в сторону. Исправление — инструментами.
        // Личный вопрос смены («что я делал», «на каком станке») — только инструменты:
        // пред-поиск даёт мусор, по которому модель отвечает вместо вызова тулзов.
        // Вопрос про наличие на складе — только stock_search: ссылки магазинов
        // не доказывают наличие у нас (кейс AEKR).
        if (WebSearchService.IsCorrection(query)
            || WebSearchService.IsShiftPersonal(query)
            || WebSearchService.IsStockQuestion(query))
        {
            logger.LogInformation("Чат ({Machine}): пропуск пред-поиска («{Q}»)",
                request.Context?.Machine ?? "(без контекста)",
                query[..Math.Min(60, query.Length)]);
            return [];
        }
        // Триггер автоприветствия — служебный: его задаёт клиент, а не оператор.
        // Он собирается из инструментов и снимка смены, и поиск по нему в сети
        // давал в приветствии ссылки на посторонние сайты.
        var isAutostart = request.Messages.Any(m =>
            !string.IsNullOrWhiteSpace(m.Content)
            && m.Content.StartsWith("[Автостарт]", StringComparison.Ordinal));
        // Вопрос о работе программы и учёта (К1/К2, наладка, нормативы, порядок
        // запуска и завершения) — это наша инструкция, а не интернет: пред-поиск
        // тратил 10 с и не давал результатов (кейс «как правильно отметить
        // смену?», 01.10). Такие вопросы агент решает через search_docs.
        // Оцениваем по последнему вопросу, а не по собранному запросу: тот
        // включает историю, где любая старая фраза про смену дала бы ложное
        // «это вопрос про программу».
        var lastUserQuestion = request.Messages.LastOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.Content)
            && !(m.Role ?? "").Trim().Equals("assistant", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? "";
        var isProgramQuestion = ChatAgentService.AsksAboutProgramDocs(lastUserQuestion);
        if (!WebSearchService.ShouldPreSearch(
                query, request.AllowWebSearch, isAutostart, isProgramQuestion))
        {
            logger.LogInformation(
                "Чат ({Machine}): пред-поиск пропущен ({Reason}) «{Q}»",
                request.Context?.Machine ?? "(без контекста)",
                isAutostart ? "автостарт" : isProgramQuestion ? "вопрос о программе" : "эвристика",
                query[..Math.Min(60, query.Length)]);
            return [];
        }
        var results = await webSearch.SearchAsync(query, ct);
        logger.LogInformation("Чат ({Machine}): веб-поиск «{Q}» → {N} (галка={Flag})",
            request.Context?.Machine ?? "(без контекста)",
            query[..Math.Min(80, query.Length)],
            results.Count, request.AllowWebSearch);
        return results;
    }

    private PromptBuilder.ChatBuildResult BuildChatMessages(
        ChatRequest request, IReadOnlyList<WebSearchResult> webResults) =>
        promptBuilder.BuildChatMessages(
            request, webResults,
            maxHistoryMessages: config.GetValue("Chat:MaxHistoryMessages", 20),
            maxChars: config.GetValue("Chat:MaxContextChars", 12000));

    private static ChatResponse ToResponse(
        ChatRequest request, string promptVersion,
        IReadOnlyList<WebSearchResult> webResults, ChatAgentResult outcome,
        string? replyOverride = null)
    {
        // Источники пред-поиска сервера + добытые самим агентом (web_search),
        // дубли по URL схлопываем: в UI один «Источники (N)».
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<ChatSourceDto>();
        foreach (var r in webResults.Concat(outcome.WebSources))
        {
            if (string.IsNullOrWhiteSpace(r.Url) || !seen.Add(r.Url.Trim())) continue;
            sources.Add(new ChatSourceDto { Title = r.Title, Url = r.Url, Snippet = r.Snippet });
        }
        return new()
        {
            // Окно eLog — plain TextBlock: markdown чистим здесь, а не у клиента,
            // чтобы логи и оба транспорта видели один и тот же текст.
            Reply = ChatAgentService.CleanMarkdown(replyOverride ?? outcome.Reply).Trim(),
            Sources = sources,
            Model = request.Model,
            PromptVersion = promptVersion,
            WebUsed = sources.Count > 0,
            ToolRounds = outcome.ToolRounds,
        };
    }

    /// <summary> Синхронный IProgress: колбэк в потоке рапорта, без пула
    /// (у Progress&lt;T&gt; финальные события терялись — см. AnalysisController). </summary>
    private sealed class SyncProgress(Action<string> onNext) : IProgress<string>
    {
        public void Report(string value) => onNext(value);
    }
}
