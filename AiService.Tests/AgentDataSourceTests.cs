using AiService.Models;
using AiService.Services;
using System.Text.Json;

namespace AiService.Tests;

/// <summary>
/// Слой данных стадии 2: парсер «Фамилия И.О.», свёртка истории (зеркало клиента),
/// приоритет БД→запрос, оператор через стаб, past_decisions в JSON.
/// </summary>
public class AgentDataSourceTests
{
    private sealed class StubSource(
        PartsHistoryDto? history = null,
        IReadOnlyList<OperatorInfo>? operators = null) : IAgentDataSource
    {
        public Task<PartsHistoryDto?> GetPartHistoryAsync(
            AnalyzeRequest request, string partName, int? setup, CancellationToken ct)
            => Task.FromResult(history);

        public Task<IReadOnlyList<OperatorInfo>> FindOperatorsAsync(string query, CancellationToken ct)
            => Task.FromResult(operators ?? []);

        public Task<OperatorHistoryDto?> GetOperatorHistoryAsync(
            string operatorQuery, int daysBack, DateOnly? date, CancellationToken ct)
            => Task.FromResult<OperatorHistoryDto?>(null);

        public Task<DbStatusDto> GetDbStatusAsync(CancellationToken ct)
            => Task.FromResult(new DbStatusDto(false, "stub", 0, 0));

        public Task<IReadOnlyList<string>> GetDowntimeReasonsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private static AnalyzeRequest Request(params PartContext[] parts) => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-23",
        Parts = [.. parts],
    };

    private static PartContext Part() => new()
    {
        PartName = "Корпус",
        Order = "З1",
        Setup = 1,
    };

    // ── Парсер «Фамилия И.О.» ──

    [Theory]
    [InlineData("Крафт А.С.", "Крафт", "А", "С")]
    [InlineData("Крафт А С", "Крафт", "А", "С")]
    [InlineData("Крафт АС", "Крафт", "А", "С")]
    [InlineData("Крафт А.", "Крафт", "А", null)]
    [InlineData("Крафт", "Крафт", null, null)]
    // Полное ФИО модель передаёт после get_operator_info. Раньше инициалы
    // склеивались из букв запроса и отчество выходило «л» (вторая буква слова
    // «Алексей») — запись отбрасывалась как «однофамилец», и модель отвечала
    // «данных нет» при существующей записи (кейс «что делал Крафт во вторник»).
    [InlineData("Крафт Алексей Сергеевич", "Крафт", "А", "С")]
    [InlineData("Крафт  Алексей   Сергеевич", "Крафт", "А", "С")]
    [InlineData("Крафт Алексей", "Крафт", "А", null)]
    [InlineData("Хомутов Андрей Борисович", "Хомутов", "А", "Б")]
    public void OperatorQueryParser_Forms(string query, string last, string? fi, string? pi)
    {
        var parsed = OperatorQueryParser.Parse(query);
        Assert.NotNull(parsed);
        Assert.Equal(last, parsed.LastName);
        Assert.Equal(fi, parsed.FirstInitial);
        Assert.Equal(pi, parsed.PatronymicInitial);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OperatorQueryParser_Empty_Null(string? query)
    {
        Assert.Null(OperatorQueryParser.Parse(query));
    }

    [Fact]
    public void OperatorQueryParser_EscapeLike()
    {
        Assert.Equal("100[%]", OperatorQueryParser.EscapeLike("100%"));
    }

    // ── Свёртка истории ──

    [Fact]
    public void Summarize_GroupsAveragesFormats()
    {
        // Зеркало SummarizePartsHistory клиента: группировка по датам DESC,
        // средний по валидным (>0, округление 2), null → б/н/б/и,
        // решение по умолчанию «не проверено».
        var rows = new List<HistoryRow>
        {
            new(new DateTime(2026, 9, 20), 10, 0.5, 1.0, ""),
            new(new DateTime(2026, 9, 20), 2, double.NaN, 0.9, ""),
            new(new DateTime(2026, 9, 19), 0, double.NaN, double.NaN, ""),
        };
        var reviews = new Dictionary<DateTime, ReviewRow>
        {
            [new DateTime(2026, 9, 20)] = new(new DateTime(2026, 9, 20), "ok", null, null, null),
        };
        var dto = HistorySummarizer.Summarize(rows, reviews);

        Assert.Equal(3, dto.RecordsFound);
        Assert.Equal(2, dto.Lines.Count);
        Assert.Equal("2026-09-20", dto.Lines[0].ShiftDate);
        Assert.Equal("95%", dto.Lines[0].ProductionRatio);
        Assert.Equal("50%", dto.Lines[0].SetupRatio);
        Assert.Equal(12, dto.Lines[0].FinishedCount);
        Assert.Equal("ok", dto.Lines[0].AnalystDecision);
        Assert.Equal("б/и", dto.Lines[1].ProductionRatio);
        Assert.Equal("б/н", dto.Lines[1].SetupRatio);
        Assert.Equal("не проверено", dto.Lines[1].AnalystDecision);
        Assert.False(dto.Lines[0].HasUnexplainedLowEfficiency);
    }

    [Fact]
    public void Summarize_LowEfficiencyFlag()
    {
        // КПД 0.5 без причины → флаг необъяснённой низкой эффективности.
        var rows = new List<HistoryRow>
        {
            new(new DateTime(2026, 9, 20), 5, 1.0, 0.5, ""),
        };
        var dto = HistorySummarizer.Summarize(rows, new Dictionary<DateTime, ReviewRow>());
        Assert.True(dto.Lines[0].HasUnexplainedLowEfficiency);
    }

    [Fact]
    public void HistoryRatios_MirrorClientFormulas()
    {
        // SetupRatio = план/факт; ProductionRatio: готово>0 при готовоФакт==0 → NaN.
        Assert.Equal(86.0 / 70, HistorySummarizer.SetupRatio(86, 70));
        Assert.True(double.IsNaN(HistorySummarizer.ProductionRatio(1, true, 30, 100)));
        Assert.Equal(5 * 30.0 / 100, HistorySummarizer.ProductionRatio(5, false, 30, 100));
    }

    // ── Приоритет БД→запрос ──

    [Fact]
    public async Task PartHistory_DbFirst()
    {
        // Источник вернул DTO — построчный legacy-путь не используется
        // (в строке дня истории нет вообще).
        var dto = new PartsHistoryDto
        {
            RecordsFound = 1,
            Lines =
            [
                new PartsHistoryLineDto
                {
                    ShiftDate = "2026-09-20",
                    ProductionRatio = "100%",
                    SetupRatio = "б/н",
                    FinishedCount = 12,
                    AnalystDecision = "ok",
                },
            ],
        };
        var r = await AgentTools.Execute(Request(Part()), new StubSource(history: dto),
            "get_part_history", """{"partName":"Корпус","setup":1}""");
        using var doc = JsonDocument.Parse(r);
        Assert.Equal(1, doc.RootElement.GetProperty("прошлыхСмен").GetInt32());
        Assert.Equal("ok", doc.RootElement.GetProperty("строки")[0].GetProperty("решение").GetString());
    }

    [Fact]
    public async Task PartHistory_DbEmpty_LegacyPath()
    {
        // Источник пуст — работает старый путь по PartsHistoryDto из запроса.
        var part = Part();
        part.PartsHistory = new PartsHistoryDto { RecordsFound = 0 };
        var r = await AgentTools.Execute(Request(part), new StubSource(history: null),
            "get_part_history", """{"partName":"Корпус"}""");
        Assert.Contains("ПОДТВЕРЖДЕНО", r);
    }

    // ── Оператор ──

    [Fact]
    public async Task OperatorInfo_Found()
    {
        var ops = new List<OperatorInfo> { new("Крафт", "Андрей", "Сергеевич", 5, true) };
        var r = await AgentTools.Execute(Request(), new StubSource(operators: ops),
            "get_operator_info", """{"query":"Крафт А.С."}""");
        using var doc = JsonDocument.Parse(r);
        Assert.Equal("Крафт", doc.RootElement[0].GetProperty("фамилия").GetString());
        Assert.Equal(5, doc.RootElement[0].GetProperty("разряд").GetInt32());
        Assert.True(doc.RootElement[0].GetProperty("активен").GetBoolean());
    }

    [Fact]
    public async Task OperatorInfo_NotFound()
    {
        var r = await AgentTools.Execute(Request(), new StubSource(),
            "get_operator_info", """{"query":"Неведомский"}""");
        Assert.Contains("не найден", r);
    }

    [Fact]
    public async Task OperatorInfo_RequestSource_Empty()
    {
        // Без БД операторов неоткуда взять — честный текст, не выдумка.
        var r = await AgentTools.Execute(Request(), RequestAgentDataSource.Instance,
            "get_operator_info", """{"query":"Крафт А.С."}""");
        Assert.Contains("не найден", r);
    }

    // ── past_decisions в JSON ──

    [Fact]
    public async Task PastDecisions_JsonArray()
    {
        var part = Part();
        part.PartsHistory = new PartsHistoryDto
        {
            RecordsFound = 1,
            Lines =
            [
                new PartsHistoryLineDto
                {
                    ShiftDate = "2026-09-20",
                    ProductionRatio = "100%",
                    SetupRatio = "б/н",
                    FinishedCount = 12,
                    AnalystDecision = "ok",
                    AnalystComment = "норма",
                },
            ],
        };
        var r = await AgentTools.Execute(Request(part), RequestAgentDataSource.Instance,
            "get_past_decisions", """{"limit":5}""");
        using var doc = JsonDocument.Parse(r);
        Assert.Equal("2026-09-20", doc.RootElement[0].GetProperty("дата").GetString());
        Assert.Equal("ok", doc.RootElement[0].GetProperty("решение").GetString());
        Assert.Equal("норма", doc.RootElement[0].GetProperty("комментарий").GetString());
    }
}
