using AiService.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiService.Services;

public record ChatAgentResult(string Reply, int ToolRounds, string? Error = null)
{
    /// <summary> Результаты поиска, добытые самим агентом через web_search
    /// (в дополнение к пред-поиску сервера): уходят в Sources ответа. </summary>
    public List<WebSearchResult> WebSources { get; init; } = [];
}

/// <summary>
/// Агентский цикл чата-ДОЗОРа: модель сама решает, нужны ли ей факты о смене,
/// и берёт их инструментами ChatTools (контекст в системный промпт НЕ кладётся),
/// а свежие факты из сети — инструментом web_search (доуточнение и проверка
/// собранных обозначений прямо по ходу диалога, а не только пред-поиском сервера).
/// Очередь Ollama занимается на каждый раунд отдельно (внутри ChatAsync) —
/// между раундами её держат другие клиенты, голодания нет.
/// Токены ответа стримятся живьём (tokenProgress) из КАЖДОГО раунда: tool-раунды
/// обычно пустые, финал печатается по мере генерации. Шаги инструментов —
/// в toolProgress («что дозор делает»: смотрит смену/ищет в сети).
/// </summary>
public class ChatAgentService(
    IAgentChatClient chat,
    WebSearchService webSearch,
    IAgentDataSource dataSource,
    DocsService docs,
    StockService stock,
    OperatorStatsService stats,
    IConfiguration config,
    ILogger<ChatAgentService> logger)
{
    public int MaxIterations => Math.Clamp(config.GetValue("Chat:AgentMaxIterations", 4), 1, 8);
    public TimeSpan Timeout => TimeSpan.FromSeconds(
        Math.Clamp(config.GetValue("Chat:AgentTimeoutSeconds", 120), 15, 600));
    public double Temperature => Math.Clamp(
        config.GetValue("Chat:Temperature", 0.2), 0.0, 2.0);
    /// <summary>
    /// Рассуждения модели (Chat:Thinking, дефолт true): перед вызовом инструментов
    /// модель думает — заметно надёжнее выбирает тулзы и реже отвечает вслепую.
    /// Цена — дольше ответ (thinking жрёт очередь и токены). В eLog живьём
    /// не показываются (клиент их игнорирует), на качество влияют всё равно.
    /// </summary>
    public bool Thinking => config.GetValue("Chat:Thinking", true);

    public async Task<ChatAgentResult> RunAsync(
        string system,
        List<ChatMessage> history,
        ElogChatContext? ctx,
        string? model,
        double? temperature,
        IProgress<string>? toolProgress,
        IProgress<string>? tokenProgress,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);

        var machine = string.IsNullOrWhiteSpace(ctx?.Machine) ? "(без контекста)" : ctx.Machine;
        // Якорь календаря: модель считает дни («во вторник», «вчера») от него.
        // Без якоря даты днями недели называет наугад (кейс «вторник 24.09»).
        var today = DateTime.Today;
        var todayLine = "\n\nСегодня: " + today.ToString("dd.MM.yyyy") + ", " +
            today.ToString("dddd", new System.Globalization.CultureInfo("ru-RU")) + ".";
        var messages = new List<ChatMessage> { new() { Role = "system", Content = system + todayLine } };
        messages.AddRange(history);

        var toolRounds = 0;
        var webSources = new List<WebSearchResult>();
        string lastContent = "";
        // Поручень автопроверки обозначений (см. NeedsDesignationVerify): максимум
        // два доп-раунда сверх лимита, дальше отдаём как есть с пометкой.
        var verifyRoundsLeft = 2;
        var verifiedNote = "";
        // Поручень прошлого: рассказ о прошлых сменах без вызова get_operator_history
        // (кейс «CNC-513, Шестерня А-47» из головы) — один принудительный раунд.
        var historyCalls = 0;
        // Статистика/проверка смены: числа обязаны прийти из инструмента, без них
        // ответ про цифры запрещён (см. блок СТАТИСТИКА в промпте).
        var statsCalls = 0;
        var historyForcedLeft = 1;
        // Поручень «нет данных без тула» и поручень чисел — по одному раунду.
        var noDataForcedLeft = 1;
        var countForcedLeft = 1;
        var promiseForcedLeft = 1;
        var correctionForcedLeft = 1;
        // Последний вопрос оператора нужен нуджам обоих поручней: без цитаты
        // модель уходит с исходной темы (кейс C.4).
        var lastQuestion = LastUserQuestion(messages);
        try
        {
            for (var i = 1; i <= MaxIterations + 2; i++)
            {
                var round = await chat.ChatAsync(
                    messages, ChatTools.Definitions, format: null,
                    think: Thinking, cts.Token, model: model,
                    temperature: temperature ?? Temperature,
                    thinkingProgress: null, tokenProgress: tokenProgress);
                lastContent = round.Content ?? "";

                if (round.ToolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(lastContent))
                        return new ChatAgentResult("", toolRounds,
                            "Модель вернула пустой ответ без вызова инструментов.")
                        { WebSources = webSources };
                    // Сырой блок результатов поиска в ответе — не ответ оператору:
                    // модель иногда цитирует его целиком (кейс — приветствие с
                    // пятью ссылками на CRM-сервисы). Режем с места, где начинается
                    // перечисление, иначе та же мусорь попадёт в UI и в лог.
                    lastContent = StripSearchDump(lastContent);
                    // Модель ввела обозначение пластин (DNMG150604), которого нет
                    // ни в поиске, ни в словах пользователя, и не проверила его:
                    // ссылки и цены из головы (кейсы 404/410). Один доп-раунд
                    // на принудительную проверку, максимум дважды.
                    var unverified = FindUnverifiedDesignations(lastContent, messages);
                    if (unverified.Count > 0 && verifyRoundsLeft > 0)
                    {
                        verifyRoundsLeft--;
                        toolProgress?.Report($"🌐 проверяю {unverified[0]}…");
                        messages.Add(new ChatMessage
                        {
                            Role = "assistant", Content = lastContent,
                        });
                        messages.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = "[Автопроверка] В твоём ответе есть обозначение " +
                                $"{string.Join(", ", unverified.Take(2))}, которого нет в результатах поиска. " +
                                "Проверь его вызовом web_search и исправь ответ и ссылки. " +
                                "Без вызова выдуманные ссылки запрещены.",
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): автопроверка {D}",
                            machine, string.Join(",", unverified.Take(2)));
                        continue;
                    }
                    if (unverified.Count > 0)
                        verifiedNote = " (обозначение не подтверждено поиском — проверьте у поставщика)";
                    // Рассказ о прошлом без вызова истории — принудительный раунд:
                    // иначе модель отвечает из головы (кейс CNC-513).
                    if (historyCalls == 0 && historyForcedLeft > 0
                        && ClaimsPastShifts(lastContent))
                    {
                        historyForcedLeft--;
                        toolProgress?.Report("⚙ проверяю прошлое…");
                        messages.Add(new ChatMessage
                        {
                            Role = "assistant", Content = lastContent,
                        });
                        messages.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = "[Автопроверка] Ты рассказываешь о прошлых сменах, " +
                                "не вызвав get_operator_history (имя — из контекста смены, " +
                                "date — конкретный день YYYY-MM-DD, если спрашивали про день). " +
                                "Вызови его сейчас и исправь ответ. " +
                                "Отвечать о прошлом без этого тула запрещено.",
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): автопроверка прошлого", machine);
                        continue;
                    }
                    // «Нет данных» без вызова инструмента — вывод на глаз (кейс
                    // кампании E.2: на «а что делал Крафт во вторник?» модель
                    // ответила «нет данных» по смене, вообще не открыв журнал).
                    // Поручень бьёт только по вопросам про прошлое: на вопрос про
                    // склад он однажды увёл модель в историю смены (регресс C.4).
                    if (historyCalls == 0 && noDataForcedLeft > 0
                        && AsksAboutPastShifts(lastQuestion) && ClaimsNoData(lastContent))
                    {
                        noDataForcedLeft--;
                        toolProgress?.Report("⚙ проверяю по журналу…");
                        messages.Add(new ChatMessage
                        {
                            Role = "assistant", Content = lastContent,
                        });
                        messages.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = "[Автопроверка] Ответь на исходный вопрос «" +
                                Trunc(lastQuestion, 120) + "». Ты сказал, что данных нет, " +
                                "не вызвав get_operator_history. Отсутствие данных — это то, " +
                                "что вернул инструмент, а не твой вывод. Вызови " +
                                "get_operator_history (имя — из контекста смены или из " +
                                "вопроса, date — конкретный день YYYY-MM-DD, если спрашивали " +
                                "про день) и ответь по его результату. Про другие темы " +
                                "не пиши.",
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): автопроверка «нет данных»", machine);
                        continue;
                    }
                    // Обещал вызов, а не вызвал (кейс кампании D.2 после правки
                    // промпта: «Вызову get_shift_info и get_active_part» — и тишина).
                    // Проверяем только когда за весь запрос не было ни одного вызова:
                    // тогда это чистая имитация, а не отсылка к уже сделанному.
                    if (toolRounds == 0 && promiseForcedLeft > 0 && AnnouncesToolCall(lastContent))
                    {
                        promiseForcedLeft--;
                        toolProgress?.Report("⚙ выполняю обещанную проверку…");
                        messages.Add(new ChatMessage
                        {
                            Role = "assistant", Content = lastContent,
                        });
                        messages.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = "[Автопроверка] Ответь на исходный вопрос «" +
                                Trunc(LastUserQuestion(messages), 120) + "». Ты пообещал " +
                                "проверку, но ни одного инструмента не вызвал — оператору " +
                                "уйдёт текст, а проверки не будет. Сделай вызов СЕЙЧАС и " +
                                "ответь по его результату. Обещать в тексте проверку " +
                                "запрещено, обещание без вызова — брак.",
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): автопроверка обещанного вызова", machine);
                        continue;
                    }
                    // Поправка без признания. Правило 7 требует начинать с признания,
                    // но текстом оно держится не всегда (кейс регресса D.3: на «всё
                    // не так, проверь ещё раз» модель ответила «Дозор бдит — смена под
                    // присмотром» — ни «ошибся», ни «прав»).
                    if (correctionForcedLeft > 0
                        && WebSearchService.IsCorrection(lastQuestion)
                        && !AcknowledgesMistake(lastContent))
                    {
                        correctionForcedLeft--;
                        toolProgress?.Report("⚙ исправляюсь…");
                        messages.Add(new ChatMessage
                        {
                            Role = "assistant", Content = lastContent,
                        });
                        messages.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = "[Автопроверка] Оператор указал, что ответ неверный, " +
                                "а ты начал с другого — признания в ошибке нет. Начни ответ с " +
                                "признания одним предложением («да, я ошибся» / «это неверно, " +
                                "проверяю»), затем перепроверь факты инструментами по теме " +
                                "исходного вопроса «" + Trunc(lastQuestion, 120) + "».",
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): автопроверка поправки без признания", machine);
                        continue;
                    }
                    // Количество «N шт», которого нет ни в вопросе, ни в результатах
                    // инструментов, — цифра из головы (кейс B.4: «Итого: 3 детали
                    // (7 штук)» при четырёх строках и 7.2 шт. в туле).
                    var invented = FindUnverifiedCounts(lastContent, messages);
                    if (invented.Count > 0 && countForcedLeft > 0)
                    {
                        countForcedLeft--;
                        toolProgress?.Report($"⚙ сверяю {invented[0]}…");
                        messages.Add(new ChatMessage
                        {
                            Role = "assistant", Content = lastContent,
                        });
                        messages.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = "[Автопроверка] Ответь на исходный вопрос «" +
                                Trunc(lastQuestion, 120) + "». В твоём ответе есть " +
                                $"количество {string.Join(", ", invented.Take(3))}, которого " +
                                "нет ни в вопросе, ни в результатах инструментов. " +
                                "Пересчитай по данным тула и исправь только эту цифру: " +
                                "свои числа подставлять нельзя, и про другие темы не пиши. " +
                                "Если считаешь иначе — покажи, откуда взял цифру.",
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): автопроверка чисел {N}",
                            machine, string.Join(",", invented.Take(3)));
                        continue;
                    }
                    logger.LogInformation(
                        "Чат-агент ({Machine}): финал за {I} раундов, tools {T}",
                        machine, i, toolRounds);
                    return new ChatAgentResult(lastContent.Trim() + verifiedNote, toolRounds)
                    { WebSources = webSources };
                }

                toolRounds += round.ToolCalls.Count;
                messages.Add(new ChatMessage
                {
                    Role = "assistant", Content = round.Content, ToolCalls = round.ToolCalls,
                });

                foreach (var call in round.ToolCalls)
                {
                    if (call.Name == "web_search")
                    {
                        var query = ExtractArg(call.Arguments, "query");
                        // Внутренние данные (остатки, наличие, что записано в
                        // журнале) в интернете не лежат: поиск по «остатки по цеху»
                        // возвращал лотки и газовые редукторы, и это уезжало
                        // оператору (кейс C.3). Цены и аналоги по-прежнему ищем.
                        if (WebSearchService.IsInternalData(query))
                        {
                            messages.Add(new ChatMessage
                            {
                                Role = "tool",
                                Content = "Поиск в сети по этому вопросу не имеет смысла: " +
                                    "это наши внутренние данные (склад, журнал, смена), " +
                                    "в интернете их нет. Скажи оператору честно, чего " +
                                    "не знаешь, и предложи уточнить.",
                            });
                            logger.LogInformation(
                                "Чат-агент ({Machine}): web_search «{Q}» пропущен (внутренние данные)",
                                machine, Trunc(query));
                            continue;
                        }
                        toolProgress?.Report($"🌐 ищу «{Trunc(query)}»…");
                        var found = await webSearch.SearchAsync(query, cts.Token);
                        webSources.AddRange(found);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool", Content = FormatWebResults(query, found),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): web_search «{Q}» → {N}",
                            machine, Trunc(query), found.Count);
                    }
                    else if (call.Name == "get_operator_info")
                    {
                        // Эксперимент: ФИО из БД (стадия 2, read-only SELECT).
                        // Без строки подключения источник — RequestAgentDataSource
                        // и честно отвечает пустотой, цикл не падает.
                        var name = ExtractArg(call.Arguments, "name");
                        var (opName, corrected) = CorrectOperatorArg(name, ctx, messages);
                        toolProgress?.Report($"⚙ ищу оператора «{Trunc(opName)}»…");
                        var found = await dataSource.FindOperatorsAsync(opName, cts.Token);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool",
                            Content = FormatOperators(opName, found)
                                + CorrectionNote(name, corrected),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): get_operator_info «{Q}» → {N}{Corr}",
                            machine, Trunc(opName), found.Count,
                            corrected ? " (фамилия исправлена)" : "");
                    }
                    else if (call.Name == "get_operator_history")
                    {
                        historyCalls++;
                        // Прошлые смены из Parts (read-only). Пусто — честное
                        // «данных нет», модель не выдумывает биографию.
                        var name = ExtractArg(call.Arguments, "name");
                        var days = ParseDays(ExtractArg(call.Arguments, "days"));
                        var day = ParseDate(ExtractArg(call.Arguments, "date"));
                        var (opName, corrected) = CorrectOperatorArg(name, ctx, messages);
                        toolProgress?.Report($"⚙ смотрю прошлые смены «{Trunc(opName)}»…");
                        var opHistory = await dataSource.GetOperatorHistoryAsync(opName, days, day, cts.Token);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool",
                            Content = FormatOperatorHistory(opName, days, opHistory)
                                + CorrectionNote(name, corrected),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): get_operator_history «{Q}» days={D} date={Date} → {N} строк{Corr}",
                            machine, Trunc(opName), days, day?.ToString("yyyy-MM-dd") ?? "-",
                            opHistory?.Lines.Count ?? 0,
                            corrected ? " (фамилия исправлена)" : "");
                    }
                    else if (call.Name == "get_shift_review")
                    {
                        statsCalls++;
                        var who = ctx?.OperatorName ?? "";
                        if (string.IsNullOrWhiteSpace(who))
                        {
                            messages.Add(new ChatMessage
                            {
                                Role = "tool",
                                Content = "Клиент не прислал оператора — проверить смену не по чему. " +
                                          "Скажи оператору, что не вижу его смену в программе.",
                            });
                        }
                        else
                        {
                            toolProgress?.Report($"⚙ проверяю смену «{Trunc(who)}»…");
                            var day = ParseDate(ExtractArg(call.Arguments, "date")) ?? ShiftDateOf(ctx);
                            var shift = ExtractArg(call.Arguments, "shift");
                            var review = await stats.LoadShiftReviewAsync(
                                who, ctx?.Machine, day, shift, cts.Token);
                            messages.Add(new ChatMessage
                            {
                                Role = "tool",
                                Content = OperatorStatsFormatter.FormatShiftReview(review, who),
                            });
                            logger.LogInformation(
                                "Чат-агент ({Machine}): get_shift_review {Op} date={Date} shift={Shift} → не отмечено {Un} мин",
                                machine, Trunc(who), day?.ToString("yyyy-MM-dd") ?? "-",
                                string.IsNullOrWhiteSpace(shift) ? "-" : shift,
                                review?.UnmarkedMinutes ?? 0);
                        }
                    }
                    else if (call.Name == "get_my_stats")
                    {
                        statsCalls++;
                        var who = ctx?.OperatorName ?? "";
                        if (string.IsNullOrWhiteSpace(who))
                        {
                            messages.Add(new ChatMessage
                            {
                                Role = "tool",
                                Content = "Клиент не прислал оператора — посчитать статистику не по чему. " +
                                          "Скажи оператору, что не вижу его данные в программе.",
                            });
                        }
                        else
                        {
                            var days = ParseDaysLoose(ExtractArg(call.Arguments, "days"));
                            var from = ParseDate(ExtractArg(call.Arguments, "from"));
                            var to = ParseDate(ExtractArg(call.Arguments, "to"));
                            // Станок берём из контекста клиента: мусорный аргумент
                            // модели (бывало «{}») сужал выборку до нуля строк.
                            // «все станки» — единственное намерение, которое
                            // расширяет выборку на все станки.
                            var wantAll = IsAllMachines(ExtractArg(call.Arguments, "machine"));
                            toolProgress?.Report($"⚙ считаю статистику «{Trunc(who)}»…");
                            var period = await stats.LoadPeriodStatsAsync(
                                who, wantAll ? null : ctx?.Machine, days, from, to, cts.Token);
                            messages.Add(new ChatMessage
                            {
                                Role = "tool",
                                Content = OperatorStatsFormatter.FormatPeriodStats(period, who),
                            });
                            logger.LogInformation(
                                "Чат-агент ({Machine}): get_my_stats {Op} days={D} from={From} to={To} → смен {S}",
                                machine, Trunc(who), days,
                                from?.ToString("yyyy-MM-dd") ?? "-",
                                to?.ToString("yyyy-MM-dd") ?? "-",
                                period?.WorkedShifts ?? 0);
                        }
                    }
                    else if (call.Name == "list_docs")
                    {
                        toolProgress?.Report("📄 смотрю документы…");
                        var list = docs.List(OperatorDocs);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool", Content = FormatDocList(list),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): list_docs → {N}", machine, list.Count);
                    }
                    else if (call.Name == "search_docs")
                    {
                        var query = ExtractArg(call.Arguments, "query");
                        toolProgress?.Report($"📄 ищу в документах «{Trunc(query)}»…");
                        var hits = docs.Search(query, allowed: OperatorDocs);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool", Content = FormatDocHits(query, hits),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): search_docs «{Q}» → {N}",
                            machine, Trunc(query), hits.Count);
                    }
                    else if (call.Name == "read_doc")
                    {
                        var name = ExtractArg(call.Arguments, "name");
                        var offset = ParseInt(ExtractArg(call.Arguments, "offset"));
                        var length = ParseInt(ExtractArg(call.Arguments, "length"));
                        toolProgress?.Report($"📄 читаю «{Trunc(name)}»…");
                        var (docName, text, total, found) = docs.Read(name, offset, length, OperatorDocs);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool", Content = FormatDocRead(name, docName, text, total, offset, found),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): read_doc «{Q}» → {Found}",
                            machine, Trunc(name), found);
                    }
                    else if (call.Name == "stock_search")
                    {
                        var query = ExtractArg(call.Arguments, "query");
                        toolProgress?.Report($"📦 ищу на складе «{Trunc(query)}»…");
                        var rows = stock.Search(query);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool", Content = FormatStock(query, rows, stock.SourceInfo),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): stock_search «{Q}» → {N}",
                            machine, Trunc(query), rows.Count);
                    }
                    else if (call.Name == "check_db")
                    {
                        toolProgress?.Report("⚙ проверяю доступ к базе…");
                        var status = await dataSource.GetDbStatusAsync(cts.Token);
                        messages.Add(new ChatMessage
                        {
                            Role = "tool", Content = FormatDbStatus(status),
                        });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): check_db → available={A}",
                            machine, status.Available);
                    }
                    else
                    {
                        toolProgress?.Report(DescribeCall(call.Name));
                        var result = ChatTools.Execute(ctx, call.Name, call.Arguments);
                        messages.Add(new ChatMessage { Role = "tool", Content = result });
                        logger.LogInformation(
                            "Чат-агент ({Machine}): tool {Tool} → {ResLen} симв.",
                            machine, call.Name, result.Length);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(lastContent))
            {
                if (FindUnverifiedDesignations(lastContent, messages).Count > 0)
                    lastContent += " (обозначение не подтверждено поиском — проверьте у поставщика)";
                return new ChatAgentResult(lastContent.Trim(), toolRounds)
                { WebSources = webSources };
            }
            return new ChatAgentResult("", toolRounds,
                $"Модель {MaxIterations} раунда подряд только звала инструменты без ответа.")
            { WebSources = webSources };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new ChatAgentResult("", toolRounds, "Думал слишком долго — попробуйте спросить короче.")
            { WebSources = webSources };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Чат-агент ({Machine}): сбой цикла", machine);
            return new ChatAgentResult("", toolRounds, $"Сбой: {ex.Message}")
            { WebSources = webSources };
        }
    }

    /// <summary>
    /// Документы, доступные оператору. Регламент мастера, план и статус внедрения —
    /// не его документы: в ответе они только сбивают с толку (и это уже было:
    /// ДОЗОР путал мастерские правила с операторскими). Агентский контур аналитики
    /// полный корпус по-прежнему видит.
    /// </summary>
    public static readonly IReadOnlyList<string> OperatorDocs = ["instrukciya_operatora.md"];

    private static string DescribeCall(string name) => name switch
    {
        "get_shift_info" => "⚙ смотрю смену и оператора…",
        "get_active_part" => "⚙ смотрю активную деталь…",
        _ => $"⚙ {name}…",
    };

    /// <summary>
    /// Утверждения о прошлом в ответе: дата/месяц + глагол прошедшего времени.
    /// Используется поручнем: рассказ о прошлом без get_operator_history —
    /// принудительный проверочный раунд. Чистая функция.
    /// </summary>
    public static bool ClaimsPastShifts(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return false;
        var t = reply.ToLowerInvariant();
        string[] months =
        [
            "января", "февраля", "марта", "апреля", "мая", "июня",
            "июля", "августа", "сентября", "октября", "ноября", "декабря",
        ];
        var hasDate = months.Any(m => t.Contains(m, StringComparison.Ordinal))
            || Regex.IsMatch(t, @"\d{1,2}[.\-]\d{1,2}[.\-]\d{2,4}");
        if (!hasDate) return false;
        string[] verbs =
        [
            "работал", "работала", "делал", "делала", "делали",
            "изготов", "обрабатывал", "обрабатывала", "действи",
        ];
        return verbs.Any(v => t.Contains(v, StringComparison.Ordinal));
    }

    /// <summary>
    /// Календарная подсказка: модель считает дни недели наугад («вторник» →
    /// 20.09-суббота), поэтому относительные даты резолвит сервер. Возвращает
    /// null, если относительного времени в вопросе нет. День недели — ближайший
    /// прошедший включая сегодня («во вторник» в субботу = позапрошлый вторник
    /// недели? нет: ближайший вторник ≤ сегодня). Чистая функция (дата параметром).
    /// </summary>
    public static string? CalendarHint(string text, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        var parts = new List<string>();
        if (t.Contains("позавчера"))
            parts.Add("«позавчера» = " + today.AddDays(-2).ToString("dd.MM.yyyy"));
        else if (t.Contains("вчера"))
            parts.Add("«вчера» = " + today.AddDays(-1).ToString("dd.MM.yyyy"));
        else if (t.Contains("сегодня"))
            parts.Add("«сегодня» = " + today.ToString("dd.MM.yyyy"));
        (string[] Forms, DayOfWeek Dow)[] days =
        [
            (["понедельник", "понедельника"], DayOfWeek.Monday),
            (["вторник", "вторника"], DayOfWeek.Tuesday),
            (["среду", "среде", "среда"], DayOfWeek.Wednesday),
            (["четверг", "четверга"], DayOfWeek.Thursday),
            (["пятницу", "пятница"], DayOfWeek.Friday),
            (["субботу", "суббота"], DayOfWeek.Saturday),
            (["воскресенье", "воскресенья"], DayOfWeek.Sunday),
        ];
        foreach (var (forms, dow) in days)
        {
            if (!forms.Any(f => t.Contains(f, StringComparison.Ordinal))) continue;
            var d = today;
            while (d.DayOfWeek != dow) d = d.AddDays(-1);
            // Найденный день — сегодняшний: «во вторник» в день вторника двусмысленно
            // (сегодня или прошлый вторник), и молча выбранный день — это подмена
            // вопроса. Даём оба варианта и прямо требуем уточнить (см. chat_system).
            if (d == today)
                parts.Add($"«{forms[0]}» = {d:dd.MM.yyyy} (это сегодня); " +
                          $"прошлый такой же — {d.AddDays(-7):dd.MM.yyyy}, уточни какой имеется в виду");
            else
                parts.Add($"«{forms[0]}» = {d:dd.MM.yyyy}");
        }
        if (parts.Count == 0) return null;
        return "[Календарь: сегодня " + today.ToString("dd.MM.yyyy") + "; " +
               string.Join("; ", parts) + ". Даты дней недели не вычисляй — бери отсюда.]";
    }

    /// <summary>
    /// Обещает ли ответ вызов инструмента, ничего не вызвав: «вызову
    /// get_shift_info», «проверю по базе», «сначала посмотрю смену». Это ровно та
    /// имитация проверки, что запрещена промптом, но правило текстом не
    /// остановило (кейс D.2). Ловится детерминированно. Чистая функция.
    /// </summary>
    public static bool AnnouncesToolCall(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return false;
        var t = reply.ToLowerInvariant();
        string[] promises =
        [
            "вызову ", "вызвать ", "сделаю вызов", "проверю по базе", "сверюсь с базой",
            "сверю по базе", "посмотрю смену", "посмотрю в базе", "запрошу в базе",
            "проверю инструментом", "проверю инструмент", "уточню инструментом",
            "сейчас проверю", "сейчас посмотрю", "сейчас сверюсь", "сначала проверю",
            "сначала посмотрю", "затем перепроверю", "обращусь к базе",
        ];
        return promises.Any(p => t.Contains(p, StringComparison.Ordinal));
    }

    /// <summary>
    /// Признал ли ответ свою ошибку. Поправка оператора обязана начинаться с
    /// признания (правило 7 промпта), иначе он читает прежний ответ как верный.
    /// Чистая функция.
    /// </summary>
    public static bool AcknowledgesMistake(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return false;
        var t = reply.ToLowerInvariant();
        string[] marks =
        [
            "ошиб", "ошибочн", "неверн", "неправ", "поправ", "призна", "извин",
            "не так", "мимо", "нерядом",
        ];
        return marks.Any(m => t.Contains(m, StringComparison.Ordinal));
    }

    /// <summary> Последний вопрос оператора — цитатой в нудж, чтобы модель не ушла    /// с исходной темы (кейс C.4: проверка чисел уводила в историю смены). </summary>
    private static string LastUserQuestion(IReadOnlyList<ChatMessage> messages) =>
        messages.LastOrDefault(m =>
            (m.Role ?? "").Trim().Equals("user", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(m.Content)
            && !m.Content.StartsWith("[Автопроверка]", StringComparison.Ordinal))?.Content
        ?? "(вопрос оператора)";

    /// <summary>
    /// Спросил ли оператор про прошлое. Поручни прошлого бьют только по таким
    /// вопросам: на вопрос про склад или документы «нет данных» — это нормальный
    /// ответ, и требование звать историю увозило модель не туда.
    /// </summary>
    public static bool AsksAboutPastShifts(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return false;
        var t = question.ToLowerInvariant();
        string[] other =
        [
            "склад", "остатк", "наличие", "есть ли", "цена", "стоимость", "курс",
            "документ", "регламент", "инструкц", "правил", "документац", "новости",
        ];
        if (other.Any(k => t.Contains(k, StringComparison.Ordinal))) return false;
        string[] past =
        [
            "делал", "делала", "делали", "работал", "работала", "работали", "работал",
            "смен", "вчера", "позавчера", "вторник", "понедельник", "сред", "среда",
            "четверг", "пятниц", "суббот", "воскресень", "истори", "прошл",
            "неделю", "неделе", "месяц", "тогда", "раньше", "было", "успевал",
        ];
        return past.Any(k => t.Contains(k, StringComparison.Ordinal));
    }

    /// <summary>
    /// Утверждение, что данных нет, по конкретной сущности: «нет данных о Крафт»,
    /// «записей не найдено». Без вызова инструмента это вывод на глаз (кейс E.2).
    /// Отличать от честной пустоты тула не нужно: туда попасть нельзя без вызова.
    /// Чистая функция.
    /// </summary>
    public static bool ClaimsNoData(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return false;
        var t = reply.ToLowerInvariant();
        string[] patterns =
        [
            "нет данных", "данных нет", "нет записей", "записей нет", "нет сведений",
            "не нашёл", "не нашел", "не обнаружил", "не найдено", "ничего не найдено",
        ];
        return patterns.Any(p => t.Contains(p, StringComparison.Ordinal));
    }

    /// <summary> Количество в ответе: «5 шт», «12 штук», «3 детали», «2 записи». </summary>
    private static readonly Regex CountRx = new(
        @"(?<!\d)(\d{1,4}(?:[.,]\d)?)\s*(шт\.?|штук\w*|детал\w*|запис\w*|ш\.?)(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Количества ответа, которых нет ни в вопросе оператора, ни в результатах
    /// инструментов: цифра из головы. Считаются только числа с единицей измерения
    /// («шт», «деталей»…) — иначе под правило попадали бы даты, артикулы и «до 800
    /// символов» из самого промпта. Ответы ассистента в истории опорой не
    /// считаются: там может лежать та же выдумка. Чистая функция.
    /// </summary>
    public static List<string> FindUnverifiedCounts(
        string reply, IReadOnlyList<ChatMessage> messages)
    {
        var inReply = CountRx.Matches(reply ?? "")
            .Cast<Match>()
            .Select(m => NormCount(m.Groups[1].Value))
            .Distinct()
            .ToList();
        if (inReply.Count == 0) return [];
        var supported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in messages)
        {
            var role = (m.Role ?? "").Trim();
            if (!role.Equals("user", StringComparison.OrdinalIgnoreCase)
                && !role.Equals("tool", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match hit in CountRx.Matches(m.Content ?? ""))
                supported.Add(NormCount(hit.Groups[1].Value));
            // Числа в сырых данных тулов идут без единиц («— 5 шт» есть, но и
            // «изготовлено: 30 шт.» тоже), поэтому дополнительно принимаем любые
            // числа из результатов инструментов: единица измерения в них может
            // отличаться, а происхождение цифры — то же самое.
            if (role.Equals("tool", StringComparison.OrdinalIgnoreCase))
                foreach (Match any in NumberRx.Matches(m.Content ?? ""))
                    supported.Add(NormCount(any.Value));
        }
        foreach (Match any in NumberRx.Matches(JoinUserText(messages)))
            supported.Add(NormCount(any.Value));
        return [.. inReply.Where(c => !supported.Contains(c))];
    }

    private static string JoinUserText(IReadOnlyList<ChatMessage> messages) =>
        string.Join("\n", messages
            .Where(m => (m.Role ?? "").Trim().Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Content ?? ""));

    /// <summary> «5», «5,0» и «5.0» — одно и то же количество. </summary>
    private static string NormCount(string raw) =>
        double.TryParse(raw.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : raw;

    private static readonly Regex NumberRx = new(
        @"(?<![\d.,])\d+(?:[.,]\d+)?(?![\d.,])", RegexOptions.Compiled);

    /// <summary>
    /// Обозначения пластин вида CNMG120408 / DNMG150604 / VCMT160404.
    /// </summary>
    private static readonly Regex DesignationRx = new(
        @"\b[CDVTSR](?:NMG|NM|MG|MMT|MP|GT)\d{4,6}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Обозначения из ответа, которых нет ни в словах пользователя, ни в результатах
    /// инструментов/поиска — т.е. введённые самой моделью (собранные по таблице
    /// или выдуманные). Опорой считаются только user и tool:
    /// ответы ассистента из истории и примеры из системного промпта опорой НЕ
    /// считаются — там может лежать та же выдумка. Пусто — всё подтверждено.
    /// </summary>
    public static List<string> FindUnverifiedDesignations(
        string reply, IReadOnlyList<ChatMessage> messages)
    {
        var inReply = DesignationRx.Matches(reply ?? "")
            .Cast<Match>()
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .ToList();
        if (inReply.Count == 0) return [];
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in messages)
        {
            var role = (m.Role ?? "").Trim();
            if (!role.Equals("user", StringComparison.OrdinalIgnoreCase)
                && !role.Equals("tool", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match hit in DesignationRx.Matches(m.Content ?? ""))
                supported.Add(hit.Value);
        }
        return [.. inReply.Where(d => !supported.Contains(d))];
    }

    private static string Trunc(string s, int max = 60) =>
        s.Length <= max ? s : s[..max] + "…";

    private static int ParseDays(string raw) =>
        int.TryParse(raw, out var days) ? Math.Clamp(days, 7, 365) : 30;

    /// <summary> Просил ли оператор все станки. Всё остальное (в том числе мусор
    /// вида «{}» или «Mazak») трактуется как «мой станок» — он и так известен
    /// из контекста клиента.</summary>
    public static bool IsAllMachines(string? raw)
    {
        var t = (raw ?? "").Trim().ToLowerInvariant();
        return t is "все" or "все станки" or "всех станков" or "all" or "all machines";
    }

    /// <summary> Глубина периода для статистики: от 1 дня (в отличие от истории,
    /// где минимум неделю — там окно нужно для контекста). </summary>
    private static int ParseDaysLoose(string raw) =>
        int.TryParse(raw, out var days) ? Math.Clamp(days, 1, 365) : 30;

    /// <summary> Дата смены из контекста клиента; если не пришла — сегодня. </summary>
    private static DateOnly? ShiftDateOf(ElogChatContext? ctx) =>
        ParseDate(ctx?.ShiftDate ?? "") ?? DateOnly.FromDateTime(DateTime.Today);

    /// <summary> Строгий разбор дня YYYY-MM-DD; мусор — null (тогда окно days). </summary>
    public static DateOnly? ParseDate(string raw) =>
        DateOnly.TryParseExact((raw ?? "").Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date : null;

    private static int ParseInt(string raw) =>
        int.TryParse(raw, out var v) ? v : 0;

    /// <summary>
    /// Страховка от подстановки фамилии из примеров («Крафт А. С.» из описаний
    /// вместо фамилии из смены — кейс: история Крафта, выданная за историю Громова).
    /// Чужая фамилия без упоминания в вопросах пользователя = ошибка копирования:
    /// молча заменяем на фамилию из контекста смены (пользователь спросить
    /// про другого может только назвав его — тогда фамилия есть в вопросах
    /// и замена не срабатывает). Возвращает (имя для запроса, была ли замена).
    /// </summary>
    public static (string Name, bool Corrected) CorrectOperatorArg(
        string argName, ElogChatContext? ctx, IReadOnlyList<ChatMessage> messages)
    {
        static string? FirstToken(string? s)
        {
            var t = (s ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return t.Length == 0 ? null : t[0].Trim().Trim('.');
        }
        var req = FirstToken(argName);
        var ctxName = (ctx?.OperatorName ?? "").Trim();
        var ctxLast = FirstToken(ctxName);
        if (string.IsNullOrEmpty(req) || string.IsNullOrEmpty(ctxLast)) return (argName, false);
        if (req.Equals(ctxLast, StringComparison.OrdinalIgnoreCase)) return (argName, false);
        // Модель почти всегда передаёт фамилию из снимка смены («Хомутов А. Б.»).
        // Раньше такая запись считалась «чужой» (её нет в тексте вопроса) и в
        // результат тула попадала пометка «вызов был с чужой фамилией» — шум и
        // лишнее подозрение в каждом ответе. Считаем исправлением только реальную
        // замену: остаток аргумента после фамилии не совпадает с контекстом.
        if (argName.Trim().Equals(ctxName, StringComparison.OrdinalIgnoreCase))
            return (argName, false);
        if (argName.Trim().StartsWith(ctxLast + " ", StringComparison.OrdinalIgnoreCase)
            || argName.Trim().StartsWith(ctxLast + ".", StringComparison.OrdinalIgnoreCase))
        {
            var rest = argName.Trim()[ctxLast.Length..].Trim();
            if (rest.Length == 0
                || (ctxName.Length > ctxLast.Length
                    && ctxName[ctxLast.Length..].Trim().Equals(rest, StringComparison.OrdinalIgnoreCase)))
                return (argName, false);
        }
        // Служебное слово или одна буква: модель вписывает в name слова вопроса
        // («оператор CNC-513») или инициал («А.» от «Хомутов А. Б.»). Ни то, ни
        // другое фамилией быть не может, и их присутствие в вопросе не считается
        // упоминанием человека. Кейс: на «а я?» ушёл запрос get_operator_info
        // с name="А." — и в ответ модель перечислила случайных операторов из базы.
        if (GenericOperatorWords.Contains(req) || req.Trim().Length <= 1)
            return (ctxName, true);
        // Проверяются только user-сообщения: в системе примеры есть всегда.
        // Сравнение по границе слова, а не подстрокой, иначе фамилия внутри
        // другого слова («Крафт» в «крафтовый») считалась бы упомянутой.
        var mentioned = messages.Any(m =>
            m.Role.Trim().Equals("user", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(m.Content ?? "", $@"(?<![\p{{L}}]){Regex.Escape(req)}(?![\p{{L}}])",
                RegexOptions.IgnoreCase));
        if (mentioned) return (argName, false);
        return (ctxName, true);
    }

    /// <summary>
    /// Служебные слова, которыми модель подменяет фамилию оператора: роль или
    /// объект журнала. Фамилией быть не могут, искать по ним бессмысленно. </summary>
    private static readonly HashSet<string> GenericOperatorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "оператор", "оператора", "оператору", "операторы", "мастер", "мастера",
        "смена", "смены", "смену", "станок", "станка", "станку", "деталь",
        "детали", "журнал", "запись", "записи", "цех", "цеха", "сотрудник",
        "работник", "человек", "кто", "он", "я",
    };

    /// <summary> Заголовок, с которого начинается перечисление результатов поиска. </summary>
    private static readonly Regex SearchDumpHeaderRx = new(
        @"^\s*(Результаты поиска|результаты поиска)", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary> Строка списка результатов: «[1] Заголовок — https://…». </summary>
    private static readonly Regex SearchDumpItemRx = new(
        @"^\s*\[\d+\]\s+\S.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Убирает из ответа перечисление результатов поиска: это данные тула, а не
    /// ответ оператору. Режем от заголовка «Результаты поиска…» либо от первой
    /// строки списка «[1] …» (модель печатает список и без заголовка, и без
    /// ссылок — «[1] Лоток Treston (kiit.ru)») — до конца текста. Ссылку внутри
    /// обычной фразы не трогаем: для этого нужны две и более строки списка.
    /// Чистая функция.
    /// </summary>
    public static string StripSearchDump(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return reply;
        var text = reply!;
        var start = SearchDumpHeaderRx.Match(text);
        if (!start.Success)
        {
            var items = SearchDumpItemRx.Matches(text);
            // Заголовка нет — режем, только если строк списка хотя бы две:
            // одиночная «[1]» может быть частью ответа.
            if (items.Count < 2) return text;
            start = items[0];
        }
        return text[..start.Index].TrimEnd();
    }

    private static string CorrectionNote(string original, bool corrected) =>
        corrected
            ? $"\nПримечание: вызов был с чужой фамилией «{original.Trim()}» — " +
              "выполнено с фамилией из контекста смены, отвечай по этим данным."
            : "";

    /// <summary>
    /// Аргумент tool-вызова: норма {"prop": "..."}, иначе сырой текст
    /// (модель иногда кладёт строку целиком). Пусто — сигнал переформулировать.
    /// </summary>
    public static string ExtractArg(string argsJson, string propName)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propName, out var q)
                && q.ValueKind == JsonValueKind.String)
                return (q.GetString() ?? "").Trim();
        }
        catch (JsonException) { }
        return argsJson.Trim().Trim('"');
    }

    /// <summary> Запрос из аргументов web_search (см. <see cref="ExtractArg"/>). </summary>
    public static string ExtractWebQuery(string argsJson) => ExtractArg(argsJson, "query");

    /// <summary>
    /// Найденные операторы языком модели: полные ФИО первыми (по ним обращаться —
    /// по Имени Отчеству), разряд и активность. Пусто — честное «нет в базе»,
    /// имя не выдумывать. Несколько — все кандидаты, пусть модель уточнит.
    /// </summary>
    public static string FormatOperators(string name, IReadOnlyList<OperatorInfo> found)
    {
        if (found.Count == 0)
            return $"Оператор «{name}» в базе cnc_operators не найден " +
                   "(или БД недоступна). Отвечай по контексту смены, ФИО не выдумывай.";
        var lines = found.Take(3).Select(o =>
        {
            var fio = $"{o.LastName} {o.FirstName} {o.Patronymic}".Trim();
            var qual = o.Qualification > 0 ? $", разряд {o.Qualification}" : "";
            var active = o.IsActive ? "" : " (неактивен)";
            return $" • {fio}{qual}{active}";
        });
        var tail = found.Count > 3 ? $"\nИ ещё {found.Count - 3}: уточни фамилию/инициалы." : "";
        return $"Операторы по запросу «{name}» (обращайся по Имени Отчеству):\n" +
               string.Join("\n", lines) + tail;
    }

    /// <summary>
    /// Прошлые смены оператора языком модели: построчно дата/станок/деталь/штуки
    /// + итог. Пусто — честное «данных нет», выдумывать биографию запрещено.
    /// </summary>
    public static string FormatOperatorHistory(
        string name, int days, OperatorHistoryDto? history)
    {
        if (history == null || history.Lines.Count == 0)
            return $"В журнале за {days} дн. записей работы «{name}» нет. " +
                   "Скажи, что данных о прошлых сменах нет, ничего не выдумывай.";
        var singleDay = history.Lines.Select(l => l.ShiftDate.Date).Distinct().Count() == 1;
        var lines = history.Lines.Select(l =>
            $" • {l.ShiftDate:dd.MM.yyyy} | {l.Machine} | «{l.PartName}»" +
            (string.IsNullOrWhiteSpace(l.Order) ? "" : $" ({l.Order}, уст. {l.Setup})") +
            $" — {l.FinishedCount:0} шт");
        var total = history.Lines.Sum(l => l.FinishedCount);
        var scope = singleDay
            ? "Отвечай ТОЛЬКО про этот день — другие периоды не упоминай."
            : "";
        var approx = history.Approximate
            ? "Точного совпадения по инициалам нет — это однофамильцы: уточни у оператора, он ли это, не выдавай за факт."
            : "";
        // Усечение выдачи — самая частая причина «нет данных» там, где они есть:
        // список ограничен, и модель без этой строки объявляет отсутствующими дни,
        // которые просто не влезли. Запрет на вывод об отсутствии обязателен.
        var cut = history.Truncated
            ? $"\nПоказаны последние {history.Lines.Count} записей, за период их БОЛЬШЕ — " +
              "список обрезан. НЕ делай вывод, что в не показанные дни работы не было: " +
              "спроси конкретный день или попроси другой диапазон."
            : "";
        return $"Прошлые смены «{history.OperatorName}» (за {days} дн., новые сверху):\n" +
               string.Join("\n", lines) +
               $"\nИтого строк: {history.Lines.Count}, изготовлено: {total:0} шт." +
               (scope.Length > 0 ? "\n" + scope : "") +
               (approx.Length > 0 ? "\n" + approx : "") +
               cut;
    }

    /// <summary>
    /// Результаты поиска языком модели: нумерованный список для цитирования [n]
    /// в ответе. Пусто — честный «ничего не нашёл», а не тишина.
    /// </summary>
    public static string FormatWebResults(string query, IReadOnlyList<WebSearchResult> found)
    {
        if (found.Count == 0)
            return $"Поиск в сети по запросу «{query}» ничего не нашёл. " +
                   "Скажи об этом оператору и предложи переформулировать или уточнить детали.";
        var lines = found.Select((r, i) =>
            $"[{i + 1}] {r.Title} — {r.Url}\n    {r.Snippet}");
        return $"Результаты поиска в сети по запросу «{query}» " +
               "(отвечай строго по ним, прикладывай ссылки [n]):\n" +
               string.Join("\n", lines);
    }

    /// <summary>
    /// Имя для первой строки приветствия: ФИО из базы (активный первый),
    /// иначе фамилия из смены, иначе «оператор». Просить модель «заполнить
    /// форму» слабее, чем «скопируй дословно» — инициалы она сокращает сама.
    /// </summary>
    public static string GreetingDisplayName(
        string contextName, IReadOnlyList<OperatorInfo> found)
    {
        var op = found.FirstOrDefault(o => o.IsActive) ?? found.FirstOrDefault();
        if (op != null)
            return $"{op.LastName} {op.FirstName} {op.Patronymic}".Trim();
        var t = (contextName ?? "").Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return t.Length == 0 ? "оператор" : t[0].Trim().Trim('.');
    }

    /// <summary>
    /// Факт об операторе для автостарт-триггера (см. ChatController): сервер
    /// резолвит ФИО детерминированно, потому что модель дважды доказала, что
    /// инициалы расшифровывать нельзя — «Крафт А. С.» превращался в выдуманного
    /// «Афанасий Семёнович». Активный кандидат первый, иначе первый.
    /// </summary>
    public static string GreetingOperatorFact(
        string contextName, IReadOnlyList<OperatorInfo> found)
    {
        var op = found.FirstOrDefault(o => o.IsActive) ?? found.FirstOrDefault();
        if (op == null)
            return "\n\nФакт из базы: оператор не найден — обращайся только " +
                   "по фамилии из смены, имя и отчество не выдумывай.";
        var fio = $"{op.LastName} {op.FirstName} {op.Patronymic}".Trim();
        var qual = op.Qualification > 0 ? $", разряд {op.Qualification}" : "";
        return "\n\nФакт из базы (имя пиши ТОЧНО так, расшифровка инициалов запрещена): " +
               $"{fio}{qual}.";
    }

    /// <summary>
    /// Чистка markdown из ответа модели: окно eLog — plain TextBlock, сырые `**`
    /// и `[текст](url)` видны как есть (кейс «**695 рублей**», «[iscar.com](...)»).
    /// Ссылки поиска всё равно лежат в Sources, в тексте остаётся читаемое.
    /// </summary>
    public static string CleanMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // [текст](url) → текст (url); сначала ссылки, потом жирность.
        text = Regex.Replace(text, @"\[(.+?)\]\((.+?)\)", "$1 ($2)");
        text = text.Replace("**", "");
        return text;
    }

    /// <summary>
    /// Каталог документов языком модели: имя + заголовок. Пусто — честное
    /// «документов нет», правила из головы не пересказывать.
    /// </summary>
    public static string FormatDocList(IReadOnlyList<DocInfo> list)
    {
        if (list.Count == 0)
            return "Рабочих документов нет (каталог пуст или недоступен). " +
                   "Отвечай по общим знаниям и честно скажи, что регламента под рукой нет.";
        var lines = list.Select(d => $" • {d.Name} — «{d.Title}»");
        return "Доступные документы:\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// Найденные фрагменты: имя документа + позиция для read_doc + текст.
    /// Пусто — «в документах нет», а не тишина.
    /// </summary>
    public static string FormatDocHits(string query, IReadOnlyList<DocHit> hits)
    {
        if (hits.Count == 0)
            return $"В документах по запросу «{query}» ничего нет. " +
                   "Скажи об этом и предложи переформулировать; из головы правила не выдумывай.";
        var lines = hits.Select((h, i) =>
            $"[{i + 1}] {h.Name}" +
            (string.IsNullOrWhiteSpace(h.Section) ? "" : $" (раздел «{h.Section}»)") +
            $" (позиция {h.OffsetChars}):\n{h.Snippet}");
        return $"Фрагменты из документов по запросу «{query}» " +
               "(отвечай строго по ним, указывай имя документа):\n" +
               string.Join("\n", lines);
    }

    /// <summary>
    /// Прочитанный кусок: имя, диапазон, текст. Мимо — честное «нет такого».
    /// </summary>
    public static string FormatDocRead(
        string asked, string docName, string text, int total, int offset, bool found)
    {
        if (!found)
            return $"Документа «{asked}» нет. Сначала вызови list_docs и возьми имя оттуда.";
        var tail = offset + text.Length < total
            ? $"\n(показано {offset}-{offset + text.Length} из {total}; продолжай read_doc с offset={offset + text.Length})"
            : $"\n(конец документа, всего {total})";
        return $"Документ «{docName}»:\n{text}" + tail;
    }

    /// <summary>
    /// Строки остатков языком модели: номенклатура + количество + источник выгрузки.
    /// Пусто — честное «на складе нет». Остатков цеха в данных нет никогда.
    /// </summary>
    public static string FormatStock(
        string query, IReadOnlyList<StockRow> rows, string sourceInfo)
    {
        if (rows.Count == 0)
            return $"На складе по запросу «{query}» ничего нет. " +
                   "Так и скажи; отсутствие не додумывай, аналоги из сети предложи через web_search.";
        var head = string.IsNullOrWhiteSpace(sourceInfo) ? "" : $" ({sourceInfo})";
        var lines = rows.Select(r =>
            $" • {r.Name} — {r.Qty:0.##} {r.Unit}".Trim());
        return $"Склад{head} по запросу «{query}»:\n" +
               string.Join("\n", lines) +
               "\nОстатков цеха в данных нет — цифры по цеху не выдумывай.";
    }

    /// <summary>
    /// Статус доступа к БД языком модели: цифры-доказательства или честная причина.
    /// </summary>
    public static string FormatDbStatus(DbStatusDto status)
    {
        if (!status.Available)
            return "Доступа к базе данных нет" +
                   (string.IsNullOrWhiteSpace(status.Error) ? "" : $" ({status.Error})") +
                   ". Отвечай по контексту смены и другим инструментам; " +
                   "про БД говори «не вижу», а не «нет вообще».";
        return $"Доступ к базе данных есть: строк в журнале (Parts) — {status.PartsRows}, " +
               $"операторов в справочнике — {status.OperatorsCount}.";
    }

    /// <summary>
    /// Справочник типов простоя: закрытый список, выбирать только из него.
    /// Пусто — справочник недоступен, тип не выдумывать, спросить мастера.
    /// </summary>
    /// <summary>
    /// Справочник причин простоя целой смены (мастерский, cnc_downtime_reasons)
    /// в чат оператора НЕ выводится: оператор такие типы не выбирает. Метод оставлен
    /// только для диагностических нужд и намеренно не вызывается агентом — раньше он
    /// кормил модель мастерским списком, и она советовала «Ремонт оборудования» там,
    /// где оператор такого варианта в ЭЖ не видит.
    /// </summary>
    public static string FormatDowntimeReasons(IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0)
            return "Справочник причин простоя ЦЕЛОЙ смены недоступен. Это список мастера; " +
                   "оператор такие типы не выбирает и называть их не нужно.";
        return "Причины простоя целой смены (справочник мастера, cnc_downtime_reasons) — " +
               "оператор их не отмечает, не предлагай их как вариант:\n" +
               string.Join("\n", reasons.Select(r => $" • {r}"));
    }
}
