using AiService.Models;
using AiService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiService.Tests;

/// <summary>
/// Чат-ДОЗОР eLog: системный промпт без данных (модель берёт контекст
/// инструментами), сборка сообщений, инструменты смены, цикл агента.
/// Без сети и Ollama — только чистая логика (клиент — заглушка).
/// </summary>
public class ChatTests
{
    private static PromptBuilder Builder() => new(NullLogger<PromptBuilder>.Instance);

    private static ElogChatContext Context() => new()
    {
        Machine = "SKT21",
        Shift = "День",
        OperatorName = "Иванов Иван Иванович",
        ShiftStarted = true,
        PartsCount = 1,
        ActivePart = new ChatActivePartDto
        {
            Name = "Гильза",
            Number = "АБВ-123",
            Order = "УЧ-456",
            Setup = 1,
            StartSetupTime = "26.09.2026 08:00",
            OperatorComment = "долгая наладка",
            Downtimes =
            [
                new ChatDowntimeDto
                {
                    Name = "Поиск инструмента",
                    Start = "26.09.2026 09:00",
                    End = "",
                    Comment = "ищу фрезу",
                },
            ],
        },
    };

    private static ChatRequest Request(List<ChatMessageDto>? messages = null) => new()
    {
        Messages = messages ?? [new ChatMessageDto { Role = "user", Content = "Что делать при простое?" }],
        Context = Context(),
    };

    // ── Сборка сообщений ──

    [Fact]
    public void BuildChatMessages_SystemHasNoShiftData()
    {
        var res = Builder().BuildChatMessages(Request(), [], maxHistoryMessages: 20, maxChars: 12000);

        Assert.NotEqual("unknown", res.Version);
        // ДОЗОР представляется, но данных смены в системе нет — их модель возьмёт tools.
        Assert.Contains("дозор", res.System.ToLowerInvariant());
        Assert.DoesNotContain("SKT21", res.System);
        Assert.DoesNotContain("Гильза", res.System);
        Assert.Single(res.Messages);
        Assert.Equal("user", res.Messages[0].Role);
        Assert.Contains("Что делать при простое?", res.Messages[0].Content);
    }

    [Fact]
    public void BuildChatMessages_SystemDemandsToolsForOperatorQuestions()
    {
        // Регрессия на кейс «как меня зовут?» → «мне неизвестно» без вызова tools:
        // система обязана требовать инструмент вместо ответа вслепую.
        var res = Builder().BuildChatMessages(Request(), [], maxHistoryMessages: 20, maxChars: 12000);

        Assert.Contains("get_shift_info", res.System);
        Assert.Contains("как меня зовут", res.System.ToLowerInvariant());
        // Квалификация — в get_operator_info, а не в рассуждениях про дипломы.
        Assert.Contains("квалификац", res.System.ToLowerInvariant());
        Assert.Contains("get_operator_info", res.System);
        // Запрет приписывать оператору находки из сети (кейс «553 отклика с хабра»).
        Assert.Contains("приписывай", res.System.ToLowerInvariant());
        // Развлекательные запросы — короткий ответ «Иди работай».
        Assert.Contains("Иди работай", res.System);
        // Запрет обвинять оператора (кейс «вы неправильно указали»).
        Assert.Contains("не обвиняй оператора", res.System.ToLowerInvariant());
        // Выбрана ≠ активна.
        Assert.Contains("Выбрана", res.System);
        // Примеры обезличены (схема словами, не «Фамилия И. О.»): конкретные ФИО
        // модель копирует в вызовы вместо фамилии из смены — так делать нечего.
        Assert.DoesNotContain("Крафт", res.System);
        Assert.DoesNotContain("Фамилия И. О.", res.System);
    }

    [Fact]
    public void BuildChatMessages_WebAppendedToLastUserMessage()
    {
        var web = new List<WebSearchResult>
        {
            new("Фреза Sandvik — каталог", "https://example.com/fреза", "твёрдость 55 HRC"),
        };
        var req = Request([
            new ChatMessageDto { Role = "user", Content = "привет" },
            new ChatMessageDto { Role = "assistant", Content = "здравствуйте" },
            new ChatMessageDto { Role = "user", Content = "сколько стоит фреза" },
        ]);
        var res = Builder().BuildChatMessages(req, web, maxHistoryMessages: 20, maxChars: 100_000);

        Assert.Equal(3, res.Messages.Count);
        Assert.Equal("assistant", res.Messages[1].Role);
        Assert.DoesNotContain("example.com", res.Messages[0].Content);
        Assert.DoesNotContain("example.com", res.Messages[1].Content);
        Assert.Contains("example.com", res.Messages[2].Content);
        Assert.Contains("[1]", res.Messages[2].Content);
    }

    [Fact]
    public void BuildChatMessages_TrimsOldHistoryToFit()
    {
        var messages = Enumerable.Range(1, 10)
            .Select(i => new ChatMessageDto { Role = "user", Content = $"вопрос номер {i}" })
            .ToList();
        var res = Builder().BuildChatMessages(Request(messages), [], maxHistoryMessages: 4, maxChars: 100_000);

        Assert.Equal(4, res.Messages.Count);
        Assert.Equal(
            ["вопрос номер 7", "вопрос номер 8", "вопрос номер 9", "вопрос номер 10"],
            res.Messages.Select(m => m.Content));
    }

    // ── Инструменты ──

    [Fact]
    public void ChatTools_ShiftInfo_HasOperatorAndMachine()
    {
        var json = ChatTools.Execute(Context(), "get_shift_info", "{}");

        Assert.Contains("SKT21", json);
        Assert.Contains("Иванов", json);
        Assert.Contains("День", json);
    }

    [Fact]
    public void ChatTools_ActivePart_HasPartAndDowntime()
    {
        var text = ChatTools.Execute(Context(), "get_active_part", "{}");

        Assert.Contains("Гильза", text);
        Assert.Contains("Поиск инструмента", text);
        Assert.Contains("ищу фрезу", text);
        Assert.Contains("идёт сейчас", text);
    }

    [Fact]
    public void ChatTools_NullContext_AsksToClarify()
    {
        Assert.Contains("не прислан", ChatTools.Execute(null, "get_shift_info", "{}"));
        Assert.Contains("Активной детали нет", ChatTools.Execute(null, "get_active_part", "{}"));
    }

    [Fact]
    public void ChatTools_UnknownTool_ListsAvailable()
    {
        var text = ChatTools.Execute(Context(), "browse_internet", "{}");

        Assert.Contains("Неизвестный инструмент", text);
        Assert.Contains("get_shift_info", text);
    }

    [Fact]
    public void BuildSearchQuery_ShortFollowUp_GluedToPrevious()
    {
        // Регрессия на кейс «какой курс доллара?» → «а так?» → поиск «а так?»
        // вернул пунктуацию: короткое уточнение ищется вместе с вопросом.
        var req = Request([
            new ChatMessageDto { Role = "user", Content = "какой курс доллара" },
            new ChatMessageDto { Role = "assistant", Content = "не знаю" },
            new ChatMessageDto { Role = "user", Content = "а так?" },
        ]);

        var query = WebSearchService.BuildSearchQuery(req);

        Assert.Contains("курс доллара", query);
        Assert.Contains("а так?", query);
    }

    [Fact]
    public void BuildSearchQuery_LongQuestion_AsIs()
    {
        var req = Request([
            new ChatMessageDto { Role = "user", Content = "первый вопрос про наладку" },
            new ChatMessageDto { Role = "assistant", Content = "ответ" },
            new ChatMessageDto { Role = "user", Content = "сколько стоит концевая фреза диаметром десять миллиметров" },
        ]);

        Assert.Equal(
            "сколько стоит концевая фреза диаметром десять миллиметров",
            WebSearchService.BuildSearchQuery(req));
    }

    [Fact]
    public void BuildSearchQuery_ShortTail_GluedToSubstantive()
    {
        // Регрессия на кейс «а я?» → «поподробней» → поиск «поподробней» вернул
        // орфографию: склейка идёт назад до первого содержательного вопроса.
        var req = Request([
            new ChatMessageDto { Role = "user", Content = "какой курс доллара сегодня" },
            new ChatMessageDto { Role = "assistant", Content = "не знаю" },
            new ChatMessageDto { Role = "user", Content = "а я?" },
            new ChatMessageDto { Role = "assistant", Content = "оператор" },
            new ChatMessageDto { Role = "user", Content = "поподробней" },
        ]);

        var query = WebSearchService.BuildSearchQuery(req);

        Assert.Contains("курс доллара", query);
        Assert.Contains("поподробней", query);
    }

    [Fact]
    public void BuildSearchQuery_NoMessages_Empty()
    {
        Assert.Equal("", WebSearchService.BuildSearchQuery(new ChatRequest()));
    }

    [Theory]
    [InlineData("есть ли у нас сверло д25", true)]
    [InlineData("а фрезы диаметром 4 мм есть?", true)]
    [InlineData("какой остаток пластин", true)]
    [InlineData("какой остаток пластин", true)]
    [InlineData("что есть из фрез на складе", true)]
    [InlineData("сколько стоит фреза", false)]
    [InlineData("станок встал, что делать", false)]
    [InlineData("", false)]
    public void IsStockQuestion_Forms(string text, bool expected)
    {
        Assert.Equal(expected, WebSearchService.IsStockQuestion(text));
    }

    [Theory]
    [InlineData("смена не запущена, станок другой, всё наврал", true)]
    [InlineData("ты всё наврал", true)]
    [InlineData("это неправда", true)]
    [InlineData("ты ошибся", true)]
    [InlineData("сколько стоит фреза", false)]
    [InlineData("станок встал, что делать", false)]
    [InlineData("", false)]
    public void IsCorrection_Forms(string text, bool expected)
    {
        Assert.Equal(expected, WebSearchService.IsCorrection(text));
    }

    [Theory]
    [InlineData("что я делал в прошлый вторник", true)]
    [InlineData("а я?", false)]
    [InlineData("на каком станке я работал", true)]
    [InlineData("как меня зовут", true)]
    [InlineData("сколько стоит фреза", false)]
    [InlineData("какой курс доллара", false)]
    [InlineData("станок встал, что делать", false)]
    [InlineData("", false)]
    public void IsShiftPersonal_Forms(string text, bool expected)
    {
        Assert.Equal(expected, WebSearchService.IsShiftPersonal(text));
    }

    [Theory]
    [InlineData("Крафт А. С.", "Крафт Алексей Сергеевич")]
    [InlineData("Неизвестный Ы.", "Неизвестный")]
    [InlineData("", "оператор")]
    public void GreetingDisplayName_Forms(string contextName, string expected)
    {
        var found = contextName.StartsWith("Крафт")
            ? new List<OperatorInfo> { new("Крафт", "Алексей", "Сергеевич", 3, true) }
            : new List<OperatorInfo>();
        Assert.Equal(expected, ChatAgentService.GreetingDisplayName(contextName, found));
    }

    [Theory]
    [InlineData("сколько стоит фреза 10мм", true)]
    [InlineData("найди документацию на Fanuc 0i", true)]
    [InlineData("поищи в сети аналоги СОЖ", true)]
    [InlineData("какой курс доллара", true)]
    [InlineData("курс евро знаешь", true)]
    [InlineData("сколько примерно стоит пластина", true)]
    [InlineData("почём пластины", true)]
    [InlineData("почем нынче пластины", true)]
    [InlineData("как отметить простой в журнале", false)]
    [InlineData("почему долго идёт наладка", false)]
    [InlineData("", false)]
    public void NeedsWeb_Heuristic(string text, bool expected)
    {
        Assert.Equal(expected, WebSearchService.NeedsWeb(text));
    }

    // ── Цикл агента ──

    private sealed class StubChat(Queue<ChatResult> script) : IAgentChatClient
    {
        public List<IReadOnlyList<ChatMessage>> SeenMessages { get; } = [];
        public List<IReadOnlyList<ChatTool>> SeenTools { get; } = [];

        public Task<ChatResult> ChatAsync(
            IReadOnlyList<ChatMessage> messages, IReadOnlyList<ChatTool> tools,
            string? format, bool think, CancellationToken ct,
            string? model = null, double? temperature = null,
            IProgress<string>? thinkingProgress = null,
            IProgress<string>? tokenProgress = null)
        {
            SeenMessages.Add(messages);
            SeenTools.Add(tools);
            return Task.FromResult(script.Dequeue());
        }
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
    {
        var dict = values.ToDictionary(v => v.Key, v => (string?)v.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
    }

    private static ChatAgentService Agent(
        StubChat stub, IConfiguration? config = null, IAgentDataSource? source = null,
        DocsService? docs = null, StockService? stock = null)
    {
        var cfg = config ?? Config();
        return new(stub,
            new WebSearchService(cfg, NullLogger<WebSearchService>.Instance),
            source ?? RequestAgentDataSource.Instance,
            docs ?? new DocsService(cfg, NullLogger<DocsService>.Instance),
            stock ?? new StockService(cfg, NullLogger<StockService>.Instance),
            cfg, NullLogger<ChatAgentService>.Instance);
    }

    private sealed class StubDataSource(
        IReadOnlyList<OperatorInfo>? operators = null,
        OperatorHistoryDto? operatorHistory = null,
        DbStatusDto? dbStatus = null,
        IReadOnlyList<string>? downtimeReasons = null) : IAgentDataSource
    {
        public Task<PartsHistoryDto?> GetPartHistoryAsync(
            AnalyzeRequest request, string partName, int? setup, CancellationToken ct)
            => Task.FromResult<PartsHistoryDto?>(null);

        public Task<IReadOnlyList<OperatorInfo>> FindOperatorsAsync(string query, CancellationToken ct)
            => Task.FromResult(operators ?? []);

        public Task<OperatorHistoryDto?> GetOperatorHistoryAsync(
            string operatorQuery, int daysBack, DateOnly? date, CancellationToken ct)
            => Task.FromResult(operatorHistory);

        public Task<DbStatusDto> GetDbStatusAsync(CancellationToken ct)
            => Task.FromResult(dbStatus ?? new DbStatusDto(false, "stub", 0, 0));

        public Task<IReadOnlyList<string>> GetDowntimeReasonsAsync(CancellationToken ct)
            => Task.FromResult(downtimeReasons ?? []);
    }

    [Fact]
    public async Task Agent_AnswersDirectly_WithoutTools()
    {
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult { Content = "Отмечайте простой кнопкой." },
        ]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Отмечайте простой кнопкой.", res.Reply);
        Assert.Equal(0, res.ToolRounds);
        // Инструменты предлагаются, система первая, данных смены в ней нет.
        Assert.Equal(11, Assert.Single(stub.SeenTools).Count);
        Assert.Equal("system", stub.SeenMessages[0][0].Role);
        Assert.DoesNotContain("SKT21", stub.SeenMessages[0][0].Content ?? "");
    }

    [Fact]
    public async Task Agent_TakesContextViaTools()
    {
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "get_active_part", Arguments = "{}" }],
            },
            new ChatResult { Content = "У вас идёт поиск инструмента." },
        ]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("У вас идёт поиск инструмента.", res.Reply);
        Assert.Equal(1, res.ToolRounds);
        // Результат tool виден модели во втором раунде.
        var second = stub.SeenMessages[1];
        var toolMsg = Assert.Single(second, m => m.Role == "tool");
        Assert.Contains("Гильза", toolMsg.Content);
    }

    [Fact]
    public async Task Agent_EmptyFinal_IsError()
    {
        var stub = new StubChat(new Queue<ChatResult>([new ChatResult { Content = "  " }]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.NotNull(res.Error);
        Assert.Equal("", res.Reply);
    }

    [Fact]
    public void ChatTools_Definitions_IncludeDocs()
    {
        Assert.Contains(ChatTools.Definitions, t => t.Name == "list_docs");
        Assert.Contains(ChatTools.Definitions, t => t.Name == "search_docs");
        Assert.Contains(ChatTools.Definitions, t => t.Name == "read_doc");
        Assert.Contains(ChatTools.Definitions, t => t.Name == "stock_search");
    }

    private static (StockService Svc, string Dir) TempStock(string csvBody)
    {
        var dir = Path.Combine(Path.GetTempPath(), "aichat-stock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stock.csv"), csvBody);
        var cfg = Config(("Stock:File", Path.Combine(dir, "stock.csv")));
        return (new StockService(cfg, NullLogger<StockService>.Instance), dir);
    }

    [Fact]
    public void StockService_SearchAndMissing()
    {
        var (svc, dir) = TempStock(
            "# source=test; warehouse=Склад; asof=2025-07-17\n" +
            "articul;name;unit;qty\n" +
            ";880-D2400L25-02 Сверло корпусное;шт;1\n" +
            ";880-D2700L32-03 Сверло корпусное;шт;1\n" +
            ";CNMG120408 Пластина;шт;0\n" +
            ";554040Z4.0-SIRON-A Фреза 4;шт;2\n");
        try
        {
            Assert.Equal(4, svc.RowCount);
            Assert.Contains("2025-07-17", svc.SourceInfo);

            var hits = svc.Search("сверло 880 24");
            Assert.NotEmpty(hits);
            Assert.Contains(hits, r => r.Name.Contains("880-D2400"));

            // Цифры-токены («4») не отбрасываются: фреза 4 мм находится.
            var mills = svc.Search("фрезы диаметром 4 мм есть");
            Assert.Contains(mills, r => r.Name.Contains("554040Z4.0"));

            Assert.Empty(svc.Search("абракадабра"));

            var cfg = Config(("Stock:File",
                Path.Combine(Path.GetTempPath(), "aichat-stock-nothing-" + Guid.NewGuid().ToString("N"), "stock.csv")));
            Assert.Empty(new StockService(cfg, NullLogger<StockService>.Instance).Search("сверло"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Agent_StockSearch_UsesRows()
    {
        var (stock, dir) = TempStock(
            "# source=test\narticul;name;unit;qty\n;880-D2400L25-02 Сверло корпусное;шт;1\n");
        try
        {
            var stub = new StubChat(new Queue<ChatResult>([
                new ChatResult
                {
                    Content = "",
                    ToolCalls = [new AgentToolCall { Id = "c1", Name = "stock_search", Arguments = """{"query":"сверло 880"}""" }],
                },
                new ChatResult { Content = "Есть D2400, одна штука." },
            ]));
            var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

            var res = await Agent(stub, stock: stock).RunAsync(
                build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

            Assert.Null(res.Error);
            Assert.Equal("Есть D2400, одна штука.", res.Reply);
            Assert.Equal(1, res.ToolRounds);
            var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
            Assert.Contains("880-D2400", toolMsg.Content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FormatStock_Empty_HonestNone()
    {
        var text = ChatAgentService.FormatStock("абракадабра", [], "");

        Assert.Contains("ничего нет", text);
    }

    [Fact]
    public void FormatDbStatus_On_ShowsCounts()
    {
        var text = ChatAgentService.FormatDbStatus(new DbStatusDto(true, null, 150000, 72));

        Assert.Contains("есть", text);
        Assert.Contains("150000", text);
        Assert.Contains("72", text);
    }

    [Fact]
    public void FormatDbStatus_Off_HonestReason()
    {
        var text = ChatAgentService.FormatDbStatus(new DbStatusDto(false, "Нет строки подключения", 0, 0));

        Assert.Contains("нет", text.ToLowerInvariant());
        Assert.Contains("Нет строки подключения", text);
    }

    [Fact]
    public void FormatDowntimeReasons_ClosedList()
    {
        var text = ChatAgentService.FormatDowntimeReasons(["Другое", "Ремонт оборудования"]);

        Assert.Contains("Другое", text);
        Assert.Contains("закрытый список", text);
    }

    [Fact]
    public void FormatDowntimeReasons_Empty_NoInvention()
    {
        var text = ChatAgentService.FormatDowntimeReasons([]);

        Assert.Contains("не выдумывай", text);
    }

    [Fact]
    public async Task Agent_CheckDb_UsesStatus()
    {
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "check_db", Arguments = "{}" }],
            },
            new ChatResult { Content = "База на месте." },
        ]));
        var source = new StubDataSource(
            dbStatus: new DbStatusDto(true, null, 150000, 72));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub, source: source).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("База на месте.", res.Reply);
        Assert.Equal(1, res.ToolRounds);
        var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
        Assert.Contains("150000", toolMsg.Content);
    }

    [Fact]
    public async Task Agent_DowntimeReasons_UsesList()
    {
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "list_downtime_reasons", Arguments = "{}" }],
            },
            new ChatResult { Content = "Отмечай Другое." },
        ]));
        var source = new StubDataSource(downtimeReasons: ["Другое", "Ремонт оборудования"]);
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub, source: source).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Отмечай Другое.", res.Reply);
        var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
        Assert.Contains("Ремонт оборудования", toolMsg.Content);
    }

    private static (DocsService Svc, string Dir) TempDocs(params (string Name, string Text)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "aichat-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, text) in files)
            File.WriteAllText(Path.Combine(dir, name), text);
        var cfg = Config(("Docs:Directory", dir));
        return (new DocsService(cfg, NullLogger<DocsService>.Instance), dir);
    }

    [Fact]
    public void DocsService_ListSearchRead()
    {
        var (svc, dir) = TempDocs(
            ("reglament.md", "# Регламент ЭЖ\n\nПростой отмечается кнопкой в журнале.\n"),
            ("plan.md", "# План\n\nПро внедрение ассистента.\n"));
        try
        {
            var list = svc.List();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, d => d.Name == "reglament.md" && d.Title.Contains("Регламент"));

            var hits = svc.Search("кнопкой журнале");
            Assert.Single(hits);
            Assert.Equal("reglament.md", hits[0].Name);
            Assert.Contains("Простой", hits[0].Snippet);

            var (name, text, total, found) = svc.Read("reglament", 0, 0);
            Assert.True(found);
            Assert.Equal("reglament.md", name);
            Assert.Contains("Простой", text);
            Assert.True(total > 0);

            var missing = svc.Read("нетакого", 0, 0);
            Assert.False(missing.Found);

            Assert.Empty(svc.Search("абракадабра"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DocsService_Morphology_StemmedMatch()
    {
        // «отметка простоя» находит «отмечается ... простои»: общий префикс ≥5.
        var (svc, dir) = TempDocs(
            ("reglament.md", "# Регламент\n\nПростои отмечаются кнопкой.\n"));
        try
        {
            var hits = svc.Search("отметка простоя");

            Assert.Single(hits);
            Assert.Contains("Простои", hits[0].Snippet);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("простои", "простой", true)]
    [InlineData("отмечать", "отметка", true)]
    [InlineData("журнале", "журнал", true)]
    [InlineData("станок", "деталь", false)]
    [InlineData("про", "простой", false)]
    public void DocsService_WordsMatch_Forms(string word, string term, bool expected)
    {
        Assert.Equal(expected, DocsService.WordsMatch(word, term));
    }

    [Fact]
    public void DocsService_SectionAt_NumberedSections()
    {
        var text = "# Регламент\n\n1\\. Назначение\n\nТекст назначения.\n\n4\\. Заполнение мастером\n\nТекст про отметку.\n";
        var atMaster = text.IndexOf("Текст про отметку", StringComparison.Ordinal);
        var atPurpose = text.IndexOf("Текст назначения", StringComparison.Ordinal);

        Assert.Equal("Заполнение мастером", DocsService.SectionAt(text, atMaster));
        Assert.Equal("Назначение", DocsService.SectionAt(text, atPurpose));
        Assert.Equal("Регламент", DocsService.SectionAt(text, 0));
    }

    [Fact]
    public void DocsService_MissingDir_Empty()
    {
        var cfg = Config(("Docs:Directory",
            Path.Combine(Path.GetTempPath(), "aichat-docs-nothing-" + Guid.NewGuid().ToString("N"))));
        var svc = new DocsService(cfg, NullLogger<DocsService>.Instance);

        Assert.Empty(svc.List());
        Assert.Empty(svc.Search("простой"));
    }

    [Fact]
    public async Task Agent_SearchDocs_UsesSnippet()
    {
        var (docs, dir) = TempDocs(
            ("reglament.md", "# Регламент ЭЖ\n\nПростой отмечается кнопкой в журнале.\n"));
        try
        {
            var stub = new StubChat(new Queue<ChatResult>([
                new ChatResult
                {
                    Content = "",
                    ToolCalls = [new AgentToolCall { Id = "c1", Name = "search_docs", Arguments = """{"query":"кнопкой журнале"}""" }],
                },
                new ChatResult { Content = "Жми кнопку простоя." },
            ]));
            var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

            var res = await Agent(stub, docs: docs).RunAsync(
                build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

            Assert.Null(res.Error);
            Assert.Equal("Жми кнопку простоя.", res.Reply);
            Assert.Equal(1, res.ToolRounds);
            var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
            Assert.Contains("reglament.md", toolMsg.Content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("""{"query":"DNMG150604 цена"}""", "DNMG150604 цена")]
    [InlineData("DNMG цена", "DNMG цена")]
    [InlineData("", "")]
    public void ExtractWebQuery_Forms(string args, string expected)
    {
        Assert.Equal(expected, ChatAgentService.ExtractWebQuery(args));
    }

    [Fact]
    public void GreetingOperatorFact_Single_ExactFio()
    {
        var text = ChatAgentService.GreetingOperatorFact("Крафт А. С.", [
            new OperatorInfo("Крафт", "Алексей", "Сергеевич", 3, true),
        ]);

        Assert.Contains("Крафт Алексей Сергеевич", text);
        Assert.Contains("разряд 3", text);
        Assert.Contains("расшифровка инициалов запрещена", text);
    }

    [Fact]
    public void GreetingOperatorFact_NotFound_SurnameOnly()
    {
        var text = ChatAgentService.GreetingOperatorFact("Неизвестный Ы.", []);

        Assert.Contains("не найден", text);
        Assert.Contains("не выдумывай", text);
    }

    [Fact]
    public void GreetingOperatorFact_PrefersActive()
    {
        var text = ChatAgentService.GreetingOperatorFact("Иванов", [
            new OperatorInfo("Иванов", "Игорь", "Игоревич", 0, false),
            new OperatorInfo("Иванов", "Иван", "Иванович", 0, true),
        ]);

        Assert.Contains("Иван Иванович", text);
    }

    [Fact]
    public void FormatOperatorHistory_LinesAndTotal()
    {
        var dto = new OperatorHistoryDto("Крафт Алексей Сергеевич", [
            new OperatorHistoryLine(new DateTime(2026, 9, 24), "Goodway GS-1500", "Корпус клапана", "УЧ1", 1, 108),
            new OperatorHistoryLine(new DateTime(2026, 9, 23), "Goodway GS-1500", "Корпус клапана", "УЧ1", 1, 129),
        ]);

        var text = ChatAgentService.FormatOperatorHistory("Крафт А. С.", 30, dto);

        Assert.Contains("24.09.2026", text);
        Assert.Contains("Goodway GS-1500", text);
        Assert.Contains("Корпус клапана", text);
        Assert.Contains("изготовлено: 237 шт", text);
    }

    [Theory]
    [InlineData("2026-09-22", 2026, 9, 22)]
    [InlineData(" 2026-09-22 ", 2026, 9, 22)]
    public void ParseDate_Forms(string raw, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), ChatAgentService.ParseDate(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("вчера")]
    [InlineData("22.09.2026")]
    public void ParseDate_Garbage_Null(string raw)
    {
        Assert.Null(ChatAgentService.ParseDate(raw));
    }

    [Fact]
    public void FormatOperatorHistory_SingleDay_OnlyThisDay()
    {
        var dto = new OperatorHistoryDto("Крафт Алексей Сергеевич", [
            new OperatorHistoryLine(new DateTime(2026, 9, 22), "Goodway GS-1500", "Корпус", "УЧ1", 1, 63),
            new OperatorHistoryLine(new DateTime(2026, 9, 22), "Goodway GS-1500", "Корпус", "УЧ1", 1, 11),
        ]);

        var text = ChatAgentService.FormatOperatorHistory("Крафт А. С.", 30, dto);

        Assert.Contains("22.09.2026", text);
        Assert.Contains("ТОЛЬКО про этот день", text);
    }

    [Fact]
    public void FormatOperatorHistory_Approximate_Warns()
    {
        var dto = new OperatorHistoryDto("Хомутов Андрей Борисович", [
            new OperatorHistoryLine(new DateTime(2026, 9, 22), "Mazak QTS350", "Гильза", "УЧ1", 1, 1),
        ], true);

        var text = ChatAgentService.FormatOperatorHistory("Хомутов И. О.", 30, dto);

        Assert.Contains("однофамильцы", text);
        Assert.Contains("Гильза", text);
    }

    [Fact]
    public void FormatOperatorHistory_Empty_HonestNoData()
    {
        var text = ChatAgentService.FormatOperatorHistory("Неизвестный", 30, null);

        Assert.Contains("нет", text);
        Assert.Contains("Неизвестный", text);
    }

    [Theory]
    [InlineData("Крафт Алексей Сергеевич", "А", "С", true)]
    [InlineData("Крафт  Алексей   Сергеевич", "А", "С", true)]
    [InlineData("Алексеев Иван", "И", null, true)]
    [InlineData("Алексеев Иван", "И", "А", false)]
    [InlineData("Алексеев Иван Александрович", "Р", null, false)]
    [InlineData("", "А", "С", false)]
    public void OperatorInitialsMatch_Forms(
        string stored, string? fi, string? pi, bool expected)
    {
        Assert.Equal(expected, SqlAgentDataSource.OperatorInitialsMatch(stored, fi, pi));
    }

    [Fact]
    public void CorrectOperatorArg_MatchOrMentioned_Unchanged()
    {
        var ctx = new ElogChatContext { OperatorName = "Громов А. А." };
        var noMessages = new List<ChatMessage>();

        // Та же фамилия — как есть.
        Assert.Equal(("Громов А. А.", false),
            ChatAgentService.CorrectOperatorArg("Громов А. А.", ctx, noMessages));
        // Пустые — нечего сверять.
        Assert.Equal(("", false),
            ChatAgentService.CorrectOperatorArg("", ctx, noMessages));
        Assert.Equal(("Крафт А. С.", false),
            ChatAgentService.CorrectOperatorArg(
                "Крафт А. С.", new ElogChatContext(), noMessages));
        // Чужая фамилия, но пользователь сам о ней спросил, — легитимно.
        var asked = new List<ChatMessage>
        {
            new() { Role = "user", Content = "что Крафт делал на прошлой неделе?" },
        };
        Assert.Equal(("Крафт А. С.", false),
            ChatAgentService.CorrectOperatorArg("Крафт А. С.", ctx, asked));
    }

    [Fact]
    public void CorrectOperatorArg_CopiedExample_FixedToShift()
    {
        // Регрессия: история Крафта, выданная за историю Громова, —
        // молча чиним на фамилию из смены.
        var ctx = new ElogChatContext { OperatorName = "Громов А. А." };
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "чем я занимался на прошлой неделе?" },
        };

        var (name, corrected) =
            ChatAgentService.CorrectOperatorArg("Крафт А. С.", ctx, messages);

        Assert.True(corrected);
        Assert.Equal("Громов А. А.", name);
    }

    [Fact]
    public async Task Agent_OperatorHistory_UsesRowsFromDb()
    {
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "get_operator_history", Arguments = """{"name":"Крафт А. С.","days":30}""" }],
            },
            new ChatResult { Content = "Раньше делали Корпус клапана." },
        ]));
        var source = new StubDataSource(operatorHistory: new OperatorHistoryDto(
            "Крафт Алексей Сергеевич",
            [new OperatorHistoryLine(new DateTime(2026, 9, 24), "Goodway GS-1500", "Корпус клапана", "УЧ1", 1, 108)]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub, source: source).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Раньше делали Корпус клапана.", res.Reply);
        Assert.Equal(1, res.ToolRounds);
        var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
        Assert.Contains("Корпус клапана", toolMsg.Content);
        Assert.Contains("изготовлено: 108 шт", toolMsg.Content);
    }

    [Fact]
    public async Task Agent_OperatorHistory_WrongSurname_CorrectedToShift()
    {
        // Тул вызван с чужой фамилией (в смене Иванов, запрошен Крафт):
        // сервер молча исправляет на фамилию из смены, модель видит пометку.
        // Стаб запрос игнорирует (возвращает фиксированный DTO), поэтому проверяем
        // заголовок с исправленной фамилией и пометку, а не строки.
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "get_operator_history", Arguments = """{"name":"Крафт А. С.","days":30}""" }],
            },
            new ChatResult { Content = "Вот история." },
        ]));
        var source = new StubDataSource(operatorHistory: new OperatorHistoryDto(
            "Иванов Иван Иванович",
            [new OperatorHistoryLine(new DateTime(2026, 9, 24), "Станок1", "Деталь", "З1", 1, 5)]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub, source: source).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Вот история.", res.Reply);
        var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
        Assert.Contains("Иванов Иван Иванович", toolMsg.Content);
        Assert.Contains("чужой фамилией", toolMsg.Content);
    }

    [Fact]
    public void FormatOperators_Empty_HonestNoFind()
    {
        var text = ChatAgentService.FormatOperators("Неизвестный", []);

        Assert.Contains("не найден", text);
        Assert.Contains("Неизвестный", text);
    }

    [Fact]
    public void FormatOperators_Single_FullFioFirst()
    {
        var text = ChatAgentService.FormatOperators("Крафт А. С.", [
            new OperatorInfo("Крафт", "Алексей", "Сергеевич", 5, true),
        ]);

        Assert.Contains("Крафт Алексей Сергеевич", text);
        Assert.Contains("разряд 5", text);
    }

    [Fact]
    public void FormatOperators_Many_TailNote()
    {
        var text = ChatAgentService.FormatOperators("Иванов", [
            new OperatorInfo("Иванов", "Иван", "Иванович", 0, true),
            new OperatorInfo("Иванов", "Игорь", "Игоревич", 0, true),
            new OperatorInfo("Иванов", "Илья", "Ильич", 0, false),
            new OperatorInfo("Ивановский", "Иван", "Иванович", 0, true),
        ]);

        Assert.Contains("И ещё 1", text);
        Assert.Contains("неактивен", text);
    }

    [Fact]
    public async Task Agent_OperatorInfo_UsesFioFromDb()
    {
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "get_operator_info", Arguments = """{"name":"Крафт А. С."}""" }],
            },
            new ChatResult { Content = "Здравствуйте, Алексей Сергеевич!" },
        ]));
        var source = new StubDataSource([
            new OperatorInfo("Крафт", "Алексей", "Сергеевич", 5, true),
        ]);
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub, source: source).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Здравствуйте, Алексей Сергеевич!", res.Reply);
        Assert.Equal(1, res.ToolRounds);
        var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
        Assert.Contains("Крафт Алексей Сергеевич", toolMsg.Content);
    }

    [Fact]
    public async Task Agent_OperatorInfo_NoDb_EmptyGraceful()
    {
        // Без БД источник отвечает пустотой (RequestAgentDataSource): цикл цел,
        // модель отвечает по контексту смены.
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "get_operator_info", Arguments = """{"name":"Крафт А. С."}""" }],
            },
            new ChatResult { Content = "Здравствуйте!" },
        ]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Здравствуйте!", res.Reply);
        var toolMsg = Assert.Single(stub.SeenMessages[^1], m => m.Role == "tool");
        Assert.Contains("не найден", toolMsg.Content);
    }

    [Fact]
    public void FormatWebResults_Empty_HonestNoFind()
    {
        var text = ChatAgentService.FormatWebResults("абракадабра", []);

        Assert.Contains("ничего не нашёл", text);
        Assert.Contains("абракадабра", text);
    }

    [Fact]
    public void FormatWebResults_NumberedForCitations()
    {
        var text = ChatAgentService.FormatWebResults("фреза", [
            new WebSearchResult("Каталог", "https://example.com/f", "сниппет"),
        ]);

        Assert.Contains("[1]", text);
        Assert.Contains("https://example.com/f", text);
    }

    [Fact]
    public async Task Agent_WebSearchFailure_FailOpenCollectsNothing()
    {
        // Поиск недоступен (закрытый порт): агент получает «ничего не нашёл»,
        // цикл не падает, WebSources пусты. Без сети, только fail-open.
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "web_search", Arguments = """{"query":"DNMG150604"}""" }],
            },
            new ChatResult { Content = "Похоже на DNMG, проверьте у поставщика." },
        ]));
        var cfg = Config(("WebSearch:Enabled", "true"), ("WebSearch:Provider", "searxng"),
            ("WebSearch:BaseUrl", "http://127.0.0.1:9"), ("WebSearch:TimeoutSeconds", "3"));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub, cfg).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Похоже на DNMG, проверьте у поставщика.", res.Reply);
        Assert.Equal(1, res.ToolRounds);
        Assert.Empty(res.WebSources);
        var toolMsg = Assert.Single(stub.SeenMessages[1], m => m.Role == "tool");
        Assert.Contains("ничего не нашёл", toolMsg.Content);
    }

    [Fact]
    public void CleanMarkdown_StripsBoldAndUnpacksLinks()
    {
        // Регрессия: eLog — plain TextBlock, сырой markdown виден как есть.
        var clean = ChatAgentService.CleanMarkdown(
            "от **695 рублей** до **12 900**, см. [каталог](https://example.com/f)");

        Assert.Equal(
            "от 695 рублей до 12 900, см. каталог (https://example.com/f)",
            clean);
    }

    [Fact]
    public void CleanMarkdown_PlainText_Untouched()
    {
        Assert.Equal("просто ответ без разметки", ChatAgentService.CleanMarkdown("просто ответ без разметки"));
        Assert.Equal("", ChatAgentService.CleanMarkdown(""));
    }

    [Fact]
    public void FindUnverifiedDesignations_ModelIntroduced_Found()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "таблица" },
            new() { Role = "user", Content = "сколько стоит CNMG120408" },
        };

        var res = ChatAgentService.FindUnverifiedDesignations(
            "Берите DNMG150604, CNMG120408 тоже есть.", messages);

        Assert.Equal(["DNMG150604"], res);
    }

    [Fact]
    public void FindUnverifiedDesignations_AllSupported_Empty()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "таблица" },
            new() { Role = "user", Content = "сколько стоит CNMG120408" },
            new() { Role = "tool", Content = "[1] CNMG120408 — https://example.com" },
            // Ответы ассистента опорой не считаются (там может лежать выдумка).
            new() { Role = "assistant", Content = "Берите DNMG150604." },
        };

        Assert.Empty(ChatAgentService.FindUnverifiedDesignations(
            "CNMG120408 в наличии.", messages));
        Assert.Equal(["DNMG150604"], ChatAgentService.FindUnverifiedDesignations(
            "Берите DNMG150604.", messages));
    }

    [Theory]
    [InlineData("20 сентября работал на CNC-513, изготовил 8 штук", true)]
    [InlineData("24.09.2026 обрабатывал Кольцо", true)]
    [InlineData("у меня нет данных о прошлых сменах", false)]
    [InlineData("Здравствуйте! Чем помочь?", false)]
    [InlineData("наладка начата 26.09.2026 в 21:00", false)]
    [InlineData("", false)]
    public void ClaimsPastShifts_Forms(string reply, bool expected)
    {
        Assert.Equal(expected, ChatAgentService.ClaimsPastShifts(reply));
    }

    [Fact]
    public void CalendarHint_TuesdayResolved()
    {
        // Суббота 26.09.2026: «во вторник» = 22.09.2026.
        var hint = ChatAgentService.CalendarHint(
            "что делал во вторник?", new DateTime(2026, 9, 26));

        Assert.NotNull(hint);
        Assert.Contains("22.09.2026", hint);
        Assert.Contains("26.09.2026", hint);
    }

    [Fact]
    public void CalendarHint_NoRelative_Null()
    {
        Assert.Null(ChatAgentService.CalendarHint(
            "сколько стоит фреза", new DateTime(2026, 9, 26)));
        Assert.Null(ChatAgentService.CalendarHint("", new DateTime(2026, 9, 26)));
    }

    [Fact]
    public async Task Agent_PastClaimsWithoutHistory_ForcesVerifyRound()
    {
        // Регрессия: рассказ о прошлом (CNC-513 из головы) без вызова истории —
        // принудительный раунд с нуджем, затем тул и чистый финал.
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult { Content = "20 сентября работал на CNC-513." },
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "get_operator_history", Arguments = """{"name":"Крафт А. С."}""" }],
            },
            new ChatResult { Content = "22.09 делали Корпус." },
        ]));
        var source = new StubDataSource(operatorHistory: new OperatorHistoryDto(
            "Крафт Алексей Сергеевич",
            [new OperatorHistoryLine(new DateTime(2026, 9, 22), "Goodway", "Корпус", "УЧ1", 1, 5)]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub, source: source).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("22.09 делали Корпус.", res.Reply);
        Assert.Equal(1, res.ToolRounds);
        Assert.Equal(3, stub.SeenMessages.Count);
        var nudge = stub.SeenMessages[^1].First(
            m => m.Role == "user" && m.Content.Contains("[Автопроверка]"));
        Assert.Contains("get_operator_history", nudge.Content);
    }

    [Fact]
    public async Task Agent_UnverifiedDesignation_ForcesVerifyRound()
    {
        var stub = new StubChat(new Queue<ChatResult>([
            new ChatResult { Content = "Берите DNMG150604." },
            new ChatResult
            {
                Content = "",
                ToolCalls = [new AgentToolCall { Id = "c1", Name = "web_search", Arguments = """{"query":"DNMG150604"}""" }],
            },
            new ChatResult { Content = "Проверил: DNMG150604 существует." },
        ]));
        var build = Builder().BuildChatMessages(Request(), [], 20, 12000);

        var res = await Agent(stub).RunAsync(
            build.System, build.Messages, Context(), null, null, null, null, CancellationToken.None);

        Assert.Null(res.Error);
        Assert.Equal("Проверил: DNMG150604 существует.", res.Reply);
        Assert.Equal(1, res.ToolRounds);
        // Три раунда: финал → нудж автопроверки → web_search → финал.
        // Стаб хранит ссылку на живой список, поэтому проверяем финальное
        // состояние: нудж виден модели user-сообщением с [Автопроверка].
        Assert.Equal(3, stub.SeenMessages.Count);
        var nudge = Assert.Single(stub.SeenMessages[^1],
            m => m.Role == "user" && m.Content.Contains("[Автопроверка]"));
        Assert.Contains("DNMG150604", nudge.Content);
    }

    [Fact]
    public void RenderElogContext_NoActivePart()
    {
        var ctx = new ElogChatContext
        {
            Machine = "Goodway",
            Shift = "Ночь",
            OperatorName = "Петров Петр Петрович",
            ShiftStarted = false,
            PartsCount = 0,
            ActivePart = null,
        };
        var text = PromptBuilder.RenderElogContext(ctx);

        Assert.Contains("Goodway", text);
        Assert.Contains("НЕ начата", text);
        Assert.Contains("Активной детали нет", text);
        // Выбрана ≠ активна: формулировка однозначная.
        Assert.Contains("ВЫБРАНА", text);
    }
}
