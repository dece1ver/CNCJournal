using AiService.Models;
using AiService.Services;
using System.Text.Json;

namespace AiService.Tests;

/// <summary>
/// Инструменты агентского контура (стадия 1, in-memory поверх AnalyzeRequest).
/// Проверяется: каталог причин (известная/неизвестная/пустая), история детали,
/// прошлые решения, нормативы дня, отчёт мастера + обрезка длинных результатов.
/// </summary>
public class AgentToolsTests
{
    private static AnalyzeRequest Request(params PartContext[] parts) => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-23",
        Parts = [.. parts],
    };

    private static PartContext BnPart() => new()
    {
        PartName = "Корпус",
        Order = "З1",
        Setup = 1,
        SetupTimePlan = 114,
        SetupTimeFact = 0,
        NoSetupHappened = true,
        ProductionRatio = 1.0,
        FinishedCount = 10,
        SingleProductionTimePlan = 5,
        PartsHistory = new PartsHistoryDto
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
        },
    };

    [Fact]
    public void Definitions_KnownReasons()
    {
        Assert.Equal(5, AgentTools.Definitions.Count);
        Assert.Contains(AgentTools.Definitions, d => d.Name == "get_reason_semantics");
        Assert.Contains(AgentTools.Definitions, d => d.Name == "get_part_history");
    }

    [Fact]
    public void ReasonSemantics_KnownReason_ReturnsRule()
    {
        var r = AgentTools.Execute(Request(), "get_reason_semantics",
            """{"reason":"Освоение","category":"setup"}""");
        Assert.Contains("ЛЮБУЮ аномалию наладки", r);
        Assert.DoesNotContain("ВНИМАНИЕ", r);
    }

    [Fact]
    public void ReasonSemantics_WrongCategory_Warns()
    {
        // Причина наладки не объясняет изготовление — привязку определяет поле.
        var r = AgentTools.Execute(Request(), "get_reason_semantics",
            """{"reason":"Освоение","category":"machining"}""");
        Assert.Contains("ВНИМАНИЕ", r);
    }

    [Fact]
    public void ReasonSemantics_DowntimeReason_FramedSeparately()
    {
        // Причина простоя — другой справочник: не «неизвестна», а «неприменима к деталям».
        // Иначе модель читает «неизвестна каталогу» как «подозрительна» (пилот 24.09.2026).
        var req = Request(BnPart());
        req.DowntimeReasons.Add("Отсутствие оператора");
        var r = AgentTools.Execute(req, "get_reason_semantics",
            """{"reason":"Отсутствие оператора","category":"setup"}""");
        Assert.Contains("ПРОСТОЯ", r);
        Assert.Contains("самодостаточна", r);
        Assert.DoesNotContain("неизвестна каталогу", r);
    }

    [Fact]
    public void ReasonSemantics_NonSerialMachining_NotJudged()
    {
        // Несерийный станок: причины изготовления не разбираем (кроме hard-фактов).
        var req = Request(BnPart());
        req.IsSerialMachine = false;
        var r = AgentTools.Execute(req, "get_reason_semantics",
            """{"reason":"Штучная/длительная работа","category":"machining"}""");
        Assert.Contains("НЕ оценивается", r);

        // Hard-факт («не по техпроцессу») — разбирается как обычно.
        var r2 = AgentTools.Execute(req, "get_reason_semantics",
            """{"reason":"Изготовление не по техпроцессу","category":"machining"}""");
        Assert.DoesNotContain("НЕ оценивается", r2);
    }

    [Theory]
    [InlineData("Деталь§1§З1§замена сверла", true)]
    [InlineData("Деталь§З1", true)] // короткий legacy — не трогаем
    [InlineData("Деталь§Уст.1§З1§замена сверла", false)] // «Уст.» вместо цифры
    [InlineData("Деталь§1§З1§Другое", false)] // голое название причины
    [InlineData("Деталь§1§З1§", false)] // пустая причина
    public void ExcludeEntry_FormatGate(string entry, bool expected)
    {
        // Пилот 25.09 (QTS350 23.09): «§Уст.1§» обходило привязку, голое «Другое»
        // проходило как конкретика.
        Assert.Equal(expected, ExcludeTriggerValidator.IsWellFormedExcludeEntry(entry));
    }

    private static PartContext DentPart() => new()
    {
        PartName = "Корпус проточной части АРМ1-119П-01-011",
        Order = "УЧ2609-0069.1.1",
        Setup = 1,
        MasterSetupComment = "Другое",
        MasterSetupDetail = "На корпусе очень много вмятин по наружному диаметру (180мм). Реставрация.",
    };

    [Fact]
    public void ExcludeReason_Grounded_Kept()
    {
        Assert.True(ExcludeTriggerValidator.HasGroundedReason(
            DentPart(), "реставрация вмятин корпуса 180мм"));
    }

    [Fact]
    public void ExcludeReason_Confabulated_Dropped()
    {
        // Пилот 25.09 (QTS350 23.09): «внутренняя резьба» выдумана — в текстах
        // только вмятины и реставрация.
        Assert.False(ExcludeTriggerValidator.HasGroundedReason(
            DentPart(), "недостаток заготовки с внутренней резьбой"));
    }

    [Fact]
    public void ReasonSemantics_UnknownReason_ListsCatalog()
    {
        var r = AgentTools.Execute(Request(), "get_reason_semantics",
            """{"reason":"Какая-то ерунда","category":"setup"}""");
        Assert.Contains("неизвестна каталогу", r);
        Assert.Contains("Освоение", r);
    }

    [Fact]
    public void ReasonSemantics_EmptyReason_ListsNonAnomalies()
    {
        // Пустая причина: короткий перечень НЕ-аномалий (одно место вместо трёх повторов промпта).
        var r = AgentTools.Execute(Request(), "get_reason_semantics",
            """{"reason":"","category":"setup"}""");
        Assert.Contains("б/н/б/и", r);
        Assert.Contains("без системного сигнала", r);
    }

    [Fact]
    public void PartHistory_Found()
    {
        var r = AgentTools.Execute(Request(BnPart()), "get_part_history",
            """{"partName":"Корпус","setup":1}""");
        Assert.Contains("2026-09-20", r);
        Assert.Contains("ok", r);
    }

    [Fact]
    public void PartHistory_NoHistory_ConfirmsFirstTime()
    {
        var part = BnPart();
        part.PartsHistory = null;
        var r = AgentTools.Execute(Request(part), "get_part_history",
            """{"partName":"Корпус"}""");
        Assert.Contains("ПОДТВЕРЖДЕНО", r);
    }

    [Fact]
    public void PartHistory_MissingPart()
    {
        var r = AgentTools.Execute(Request(BnPart()), "get_part_history",
            """{"partName":"Неведома зверушка"}""");
        Assert.Contains("нет в данных дня", r);
    }

    [Fact]
    public void PastDecisions_Aggregates()
    {
        var r = AgentTools.Execute(Request(BnPart()), "get_past_decisions", """{"limit":5}""");
        Assert.Contains("2026-09-20", r);
        Assert.Contains("ok", r);
    }

    [Fact]
    public void Normatives_FromDayData()
    {
        // Нормативы — JSON: числа числами, естьЗаказ считает C#.
        var r = AgentTools.Execute(Request(BnPart()), "get_normatives",
            """{"partName":"Корпус"}""");
        using var doc = JsonDocument.Parse(r);
        Assert.Equal(114, doc.RootElement.GetProperty("планНаладки").GetDouble());
        Assert.True(doc.RootElement.GetProperty("естьЗаказ").GetBoolean());
    }

    [Fact]
    public void ReasonSemantics_PartialSetupWithMinutes_Normalized()
    {
        // Кейс QTS350 2026-09-24: модель цитирует «частичная наладка: 38 мин»,
        // точного ключа нет — без нормализации отвечалось «неизвестна каталогу»,
        // что читалось как «подозрительна» и эскалировалось.
        var r = AgentTools.Execute(Request(), "get_reason_semantics",
            """{"reason":"частичная наладка: 38 мин","category":"setup"}""");
        Assert.Contains("Переходная", r);
        Assert.Contains("нормализован", r);
        Assert.DoesNotContain("неизвестна каталогу", r);
    }

    [Fact]
    public void ReasonSemantics_DrugoeWithDetail_Normalized()
    {
        var r = AgentTools.Execute(Request(), "get_reason_semantics",
            """{"reason":"Другое: ждал деталь с другого станка","category":"setup"}""");
        Assert.Contains("Только если ничего из списка", r);
        Assert.DoesNotContain("неизвестна каталогу", r);
    }

    [Fact]
    public void ShiftReport_NoReports()
    {
        var r = AgentTools.Execute(Request(BnPart()), "get_shift_report", "{}");
        Assert.Contains("проверять нечего", r);
    }

    [Fact]
    public void ShiftReport_WithReport()
    {
        var req = Request(BnPart());
        req.ShiftReports.Add(new ShiftReportContext
        {
            Shift = "День",
            ShiftMinutes = 660,
            ReportExists = true,
            Master = "",
            FreshUnspecifiedDowntimes = 0,
        });
        var r = AgentTools.Execute(req, "get_shift_report", "{}");
        using var doc = JsonDocument.Parse(r);
        Assert.True(doc.RootElement.GetProperty("мастерНеУказан").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("мастер").ValueKind);
    }

    [Fact]
    public void UnknownTool_ListsAvailable()
    {
        var r = AgentTools.Execute(Request(), "no_such_tool", "{}");
        Assert.Contains("Неизвестный инструмент", r);
        Assert.Contains("get_reason_semantics", r);
    }

    [Fact]
    public void LongResult_Truncated()
    {
        // Длинный результат режется до MaxResultChars с маркером.
        var parts = Enumerable.Range(1, 3).Select(i => new PartContext
        {
            PartName = $"Деталь{i}",
            Order = "З1",
            Setup = 1,
            PartsHistory = new PartsHistoryDto
            {
                RecordsFound = 5,
                Lines = Enumerable.Range(1, 5).Select(d => new PartsHistoryLineDto
                {
                    ShiftDate = $"2026-09-{d:00}",
                    ProductionRatio = "50%",
                    SetupRatio = "50%",
                    FinishedCount = 1,
                    AnalystDecision = "escalated",
                    AiFeedback = new string('я', 500),
                }).ToList(),
            },
        }).ToArray();
        var r = AgentTools.Execute(Request(parts), "get_past_decisions", """{"limit":10}""");
        Assert.True(r.Length <= AgentTools.MaxResultChars + 20);
        Assert.EndsWith("[обрезано]", r);
    }
}
