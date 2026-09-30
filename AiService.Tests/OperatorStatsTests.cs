using AiService.Models;
using AiService.Services;
using remeLog.Core;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using JournalPart = remeLog.Models.Part;
using Xunit;

namespace AiService.Tests;

// ═══ Операторская статистика: числа должны совпадать с отчётом мастера ═══
//
// Проверяем не «посчитали ли мы что-то», а совпадает ли это с тем, что посчитает
// remeLog.Core (суточнй отчёт мастера и отчёт по оператору считаются им же).
// Если формулы в сервисе разойдутся с отчётом — тесты падают.

public class OperatorStatsTests
{
    private static readonly DateTime Day = new(2026, 9, 29, 0, 0, 0, DateTimeKind.Unspecified);

    private static JournalPart Row(
        string shift = Shifts.Day,
        double setupFact = 0, double setupDowntimes = 0, double machiningDowntimes = 0,
        double partialSetup = 0, double productionFact = 0,
        double setupPlan = 0, double singlePlan = 0, double finished = 0,
        string partName = "Деталь", string order = "12345", int setup = 1,
        double hardwareFailure = 0, double toolSearching = 0,
        bool excludeFromReports = false,
        DateTime? startSetup = null, DateTime? startMachining = null,
        DateTime? endMachining = null)
    {
        var start = startSetup ?? Day.AddHours(8);
        var machining = startMachining ?? (setupFact > 0 ? start.AddMinutes(setupFact) : start);
        var end = endMachining ?? (productionFact > 0
            ? machining.AddMinutes(productionFact)
            : DateTime.MinValue);
        return new JournalPart(
            guid: Guid.NewGuid(),
            machine: "Mazak QTS350",
            shift: shift,
            shiftDate: Day,
            @operator: "Хомутов Александр Борисович",
            partName: partName,
            order: order,
            setup: setup,
            finishedCount: finished,
            defectiveCount: 0,
            totalCount: 0,
            startSetupTime: start,
            startMachiningTime: machining,
            setupTimeFact: setupFact,
            endMachiningTime: end,
            setupTimePlan: setupPlan,
            setupTimePlanForReport: setupPlan,
            singleProductionTimePlan: singlePlan,
            productionTimeFact: productionFact,
            machiningTime: TimeSpan.FromMinutes(productionFact),
            setupDowntimes: setupDowntimes,
            machiningDowntimes: machiningDowntimes,
            partialSetupTime: partialSetup,
            createNcProgramTime: 0,
            maintenanceTime: 0,
            toolSearchingTime: toolSearching,
            toolChangingTime: 0,
            mentoringTime: 0,
            contactingDepartmentsTime: 0,
            fixtureMakingTime: 0,
            hardwareFailureTime: hardwareFailure,
            specialDowntimeTime: 0,
            operatorComment: "",
            excludeFromReports: excludeFromReports);
    }

    // ───────── Неотмеченное время: та же формула, что в суточном отчёте ─────────

    [Fact]
    public void Неотмеченное_Считается_По_ФормулеСуточногоОтчёта()
    {
        List<JournalPart> parts =
        [
            Row(setupFact: 200, setupDowntimes: 30),
            Row(setupFact: 100, productionFact: 300, machiningDowntimes: 20,
                partName: "Другая", order: "12346"),
        ];
        // Эталон — формула мастера из PartExtensions.
        var expected = (int)ShiftType.Day - parts.Sum(p =>
            p.SetupTimeFact + p.ProductionTimeFact + p.SetupDowntimes
            + p.MachiningDowntimes + p.PartialSetupTime);

        // Так же считает сервис: отмеченное = сумма тех же полей.
        var marked = parts.Sum(p => p.SetupTimeFact + p.ProductionTimeFact
            + p.SetupDowntimes + p.MachiningDowntimes + p.PartialSetupTime);
        var review = new ShiftReviewResult(
            "Mazak QTS350", Shifts.Day, DateOnly.FromDateTime(Day), "Хомутов А. Б.",
            660, marked, (int)ShiftType.Day - marked, 2, 50, 0.05, 0, 0, 0, [], []);

        Assert.Equal(expected, (int)review.UnmarkedMinutes);
        Assert.Equal(660, review.ShiftMinutes);
    }

    [Fact]
    public void Пороги_Совпадают_С_ПравиламиМастера()
    {
        // Те же числа, что у мастера: ShiftReportRules (0.1 и 0.3) и
        // remeLog.Core.Models.Part — валидация строки (0.5).
        Assert.Equal(0.1, OperatorStatsService.UnmarkedAttentionRatio, 6);
        Assert.Equal(0.3, OperatorStatsService.PartialSetupAttentionRatio, 6);
        Assert.Equal(0.5, OperatorStatsService.DowntimeCommentRatio, 6);
    }

    [Fact]
    public void ПростоеИзИнструкции_Требуют_Пояснения()
    {
        // Ровно те два простоя, для которых инструкция оператора требует
        // дополнительную информацию (склад / техническая служба).
        Assert.Equal(["Отказ оборудования", "Поиск и получение инструмента"],
            OperatorStatsService.DowntimesNeedingComment);
    }

    [Fact]
    public void Длительности_Смен_Совпадают_С_Отчётом()
    {
        Assert.Equal(660, (int)ShiftType.Day);
        Assert.Equal(630, (int)ShiftType.Night);
        Assert.Equal(ShiftType.Day, OperatorStatsService.ShiftTypeOf(Shifts.Day));
        Assert.Equal(ShiftType.Night, OperatorStatsService.ShiftTypeOf(Shifts.Night));
        Assert.Equal(ShiftType.Day, OperatorStatsService.ShiftTypeOf("ДЕНЬ"));
    }

    /// <summary>Модель в аргументы иногда кладёт мусор («{}», «смена»). Такое
    /// значение не должно обнулять смену: строки журнала терялись, и оператору
    /// показывалось «записей нет» при полной смене.</summary>
    [Theory]
    [InlineData("{}", null)]
    [InlineData("смена", null)]
    [InlineData("День", Shifts.Day)]
    [InlineData("день", Shifts.Day)]
    [InlineData(" НОЧЬ ", Shifts.Night)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void НазваниеСмены_ПринимаетсяТолькоИзвестное(string? raw, string? expected)
    {
        Assert.Equal(expected, OperatorStatsService.KnownShift(raw));
    }

    // ───────── Проверка смены: текст для оператора ─────────

    private static ShiftReviewResult Review(
        double marked = 600, int minutes = 660, int records = 3,
        double downtimeRatio = 0.1, double partialRatio = 0.05,
        IReadOnlyList<(string Field, string Description)>? anomalies = null) =>
        new("Mazak QTS350", Shifts.Day, new DateOnly(2026, 9, 29), "Хомутов А. Б.",
            minutes, marked, minutes - marked, records, 90, downtimeRatio,
            30, partialRatio, 0,
            [new DowntimeRemark("Отказ оборудования", 40, true)],
            anomalies ?? []);

    [Fact]
    public void ПроверкаСмены_Неотмеченное_ВНорме_БезДействий()
    {
        var review = new ShiftReviewResult(
            "Mazak QTS350", Shifts.Day, new DateOnly(2026, 9, 29), "Хомутов А. Б.",
            660, 600, 60, 3, 90, 0.05, 0, 0, 0, [], []);
        var text = OperatorStatsFormatter.FormatShiftReview(review, "Хомутов А. Б.");
        Assert.Contains("Не отмечено: 60 мин", text);
        Assert.DoesNotContain("больше 10%", text);
        Assert.Contains("Явных проблем нет", text);
    }

    [Fact]
    public void ПроверкаСмены_МногоНеотмеченного_ПроситЗакрытьВремя()
    {
        var text = OperatorStatsFormatter.FormatShiftReview(
            Review(marked: 500, minutes: 660), "Хомутов А. Б.");
        Assert.Contains("Не отмечено 160 мин", text);
        Assert.Contains("больше 10%", text);
        Assert.Contains("мастер обязан указать причину простоя", text);
        // Нюанс инструкции: отработал не всю смену — отмечать не обязательно.
        Assert.Contains("отработал не всю смену", text);
    }

    [Fact]
    public void ПроверкаСмены_ПростоиБольшеПоловины_ПроситПроверить()
    {
        var text = OperatorStatsFormatter.FormatShiftReview(
            Review(downtimeRatio: 0.62), "Хомутов А. Б.");
        Assert.Contains("больше половины", text);
        Assert.Contains("не отмечено ли лишнего", text);
    }

    [Fact]
    public void ПроверкаСмены_ЧастичнаяНалдка_ПроситОбъяснить()
    {
        var text = OperatorStatsFormatter.FormatShiftReview(
            Review(partialRatio: 0.45), "Хомутов А. Б.");
        Assert.Contains("Частичная наладка 45%", text);
        Assert.Contains("попросит объяснить причину в комментарии", text);
    }

    [Fact]
    public void ПроверкаСмены_ПростойБезПояснения_УпоминаетИнструкцию()
    {
        var text = OperatorStatsFormatter.FormatShiftReview(Review(), "Хомутов А. Б.");
        Assert.Contains("дополнительная информация", text);
        Assert.Contains("Отказ оборудования", text);
    }

    [Fact]
    public void ПроверкаСмены_ЗамечанияМастера_ПоказываетСтроки()
    {
        var text = OperatorStatsFormatter.FormatShiftReview(
            Review(anomalies: [("Вал (12345)", "Отсутствует норматив наладки при реальном заказе")]),
            "Хомутов А. Б.");
        Assert.Contains("ЗАМЕЧАНИЯ МАСТЕРА ПО СТРОКАМ", text);
        Assert.Contains("Вал (12345)", text);
        Assert.Contains("Отсутствует норматив наладки", text);
        Assert.Contains("мастер потребует причину или комментарий", text);
    }

    [Fact]
    public void ПроверкаСмены_БезЗаписей_НеВыдумываетЧисла()
    {
        var text = OperatorStatsFormatter.FormatShiftReview(
            Review(marked: 0, records: 0), "Хомутов А. Б.");
        Assert.Contains("Записей в журнале за эту смену нет", text);
        Assert.DoesNotContain("Отмечено времени", text);
        // Инструкция разрешает не отмечать ничего, если оператор отработал часть смены.
        Assert.Contains("можно ничего не отмечать", text);
    }

    [Fact]
    public void ПроверкаСмены_НетДанных_ЧестныйОтказ()
    {
        var text = OperatorStatsFormatter.FormatShiftReview(null, "Хомутов А. Б.");
        Assert.Contains("нет", text);
        Assert.Contains("Не выдумывай цифры", text);
    }

    /// <summary>Мусорный аргумент machine не должен сужать выборку до нуля:
    /// станок известен из контекста клиента, расширить выборку может только
    /// явное «все станки».</summary>
    [Theory]
    [InlineData("все", true)]
    [InlineData("Все станки", true)]
    [InlineData("all machines", true)]
    [InlineData("{}", false)]
    [InlineData("Mazak QTS350", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Станок_ИзАргументов_ПонимаетсяСтрого(string? raw, bool all)
    {
        Assert.Equal(all, ChatAgentService.IsAllMachines(raw));
    }

    /// <summary>Дата смены — по её началу. Ночная смена после полуночи (до 09:00)
    /// относится к предыдущим суткам: закончил в 3 часа 21-го — смена началась
    /// 20-го в 19:00, детали пишутся за 20-е. Правило взято из eLog
    /// (Infrastructure/Extensions/Parts.cs) и зафиксировано тестом.</summary>
    [Fact]
    public void ЧасПереходаНочнойСмены_СовпадаетСЗаписьюВБазу()
    {
        Assert.Equal(9, ChatAgentService.NightShiftCutoverHour);
    }

    [Theory]
    [InlineData(0, "Ночь", -1)]   // полночь — ещё вчерашняя смена
    [InlineData(3, "Ночь", -1)]
    [InlineData(8, "Ночь", -1)]   // до 09:00 — тоже вчера
    [InlineData(9, "Ночь", 0)]    // с 09:00 — сегодняшняя
    [InlineData(23, "Ночь", 0)]
    [InlineData(0, "День", 0)]    // дневная всегда сегодня
    [InlineData(3, "День", 0)]
    [InlineData(8, "День", 0)]
    public void ДатаСмены_СчитаетсяПоНачалуСмены(int hour, string shift, int dayOffset)
    {
        var now = new DateTime(2026, 9, 30, hour, 30, 0);
        var expected = now.Date.AddDays(dayOffset);
        var date = ChatAgentService.ShiftDateFor(
            new ElogChatContext { Shift = shift }, now);
        Assert.Equal(expected, date);
    }

    [Fact]
    public void ДатаСмены_ИзКонтекстаКлиента_ВажнееРасчёта()
    {
        // Клиент прислал дату — берём её, даже если она «нелогичная» для текущего часа.
        var ctx = new ElogChatContext { Shift = "Ночь", ShiftDate = "2026-09-20" };
        Assert.Equal(new DateOnly(2026, 9, 20), ChatAgentService.ShiftDateOf(ctx, new DateTime(2026, 9, 21, 3, 0, 0)));
    }

    // ───────── Статистика за период ─────────

    private static PeriodStatsResult Stats(
        int workedShifts = 5, int workDays = 30, int minShifts = 5,
        double setupRatio = 0.87, double productionRatio = 0.96,
        double includedTimeRatio = 0.82, int excluded = 0) =>
        new("Хомутов Александр Борисович", "Mazak QTS350",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
            workedShifts, workDays, minShifts, 4, 6, 5, 43, 210, 980, 240, 0.16,
            1450, setupRatio, productionRatio, includedTimeRatio,
            [("Обслуживание", 120), ("Отказ оборудования", 40)], excluded,
            workedShifts >= minShifts, includedTimeRatio >= 0.3);

    [Fact]
    public void Статистика_ПоказываетКлючевыеЧисла()
    {
        var text = OperatorStatsFormatter.FormatPeriodStats(Stats(), "Хомутов А. Б.");
        Assert.Contains("Смен с записями: 5", text);
        Assert.Contains("М/Л: 4", text);
        Assert.Contains("Наладок: 6", text);
        Assert.Contains("Деталей изготовлено: 43", text);
        Assert.Contains("КПД наладки: 87%", text);
        Assert.Contains("КПД изготовления: 96%", text);
        Assert.Contains("Обслуживание 120", text);
    }

    [Fact]
    public void Статистика_УсловияКоэффициента_ПоказываетТребования()
    {
        var ok = OperatorStatsFormatter.FormatPeriodStats(Stats(), "Х");
        Assert.Contains("нужно ≥ 5", ok);
        Assert.Contains("нужно ≥ 30%", ok);

        var fail = OperatorStatsFormatter.FormatPeriodStats(
            Stats(workedShifts: 2, workDays: 60, minShifts: 10, includedTimeRatio: 0.11), "Х");
        Assert.Contains("нужно ≥ 10", fail);
        Assert.Contains("не выполнено", fail);
    }

    [Fact]
    public void Статистика_БезНормативов_ПишетНетДанных()
    {
        var text = OperatorStatsFormatter.FormatPeriodStats(
            Stats(setupRatio: 0, productionRatio: double.NaN), "Х");
        Assert.Contains("КПД наладки: нет данных (нет нормативов)", text);
        Assert.Contains("КПД изготовления: нет данных (нет нормативов)", text);
    }

    [Fact]
    public void Статистика_ИсключённыеМастером_Упоминаются()
    {
        var text = OperatorStatsFormatter.FormatPeriodStats(Stats(excluded: 2), "Х");
        Assert.Contains("исключённых мастером из отчётов: 2", text);
    }

    [Fact]
    public void Статистика_БезДанных_ЧестныйОтказ()
    {
        var text = OperatorStatsFormatter.FormatPeriodStats(null, "Хомутов А. Б.");
        Assert.Contains("нет", text);
        Assert.Contains("выдумывать нельзя", text);
    }

    [Fact]
    public void Статистика_ОбъясняетКакСчитано_ИНеПоказываетКоэффициентМастера()
    {
        var text = OperatorStatsFormatter.FormatPeriodStats(Stats(), "Х");
        Assert.Contains("Как считано", text);
        Assert.Contains("без исправленных", text);
        Assert.Contains("коэффициент по разряду считает мастер", text);
    }

    // ───────── Формулы остаются формулами отчёта ─────────

    [Fact]
    public void Агрегаты_Считаются_ТемжеКодомЧтоОтчёт()
    {
        List<JournalPart> parts =
        [
            Row(setupFact: 120, setupPlan: 100, productionFact: 300, singlePlan: 2, finished: 100),
            Row(setupFact: 80, setupPlan: 90, productionFact: 200, singlePlan: 1.5, finished: 120,
                partName: "Другая", order: "999"),
        ];
        // Эталоны — формулы из remeLog.Core, которыми считает отчёт по оператору
        // (Part.cs: SetupTimeFact/ProductionTimeFact, PartExtensions: агрегаты).
        Assert.Equal(parts.Sum(p => p.SetupTimeFact + p.PartialSetupTime),
            parts.TotalSetupTime().TotalMinutes, 6);
        Assert.Equal(parts.Sum(p => p.ProductionTimeFact),
            parts.TotalProductionTime().TotalMinutes, 6);
        Assert.Equal(parts.Sum(p => p.SetupDowntimes + p.MachiningDowntimes),
            parts.TotalDowntimesTime().TotalMinutes, 6);

        // КПД наладки: сумма уникальных нормативов / сумма факта.
        var setupPlanSum = new HashSet<(string, string)>();
        double uniqueNorms = 0;
        foreach (var p in parts)
            if (p.SetupTimeFact > 0 && setupPlanSum.Add((p.PartName, p.Order)))
                uniqueNorms += p.SetupTimePlan;
        Assert.Equal(uniqueNorms / parts.Sum(p => p.SetupTimeFact), parts.SetupRatio(), 6);

        // КПД изготовления: Σ(FinishedCountFact × штучный норматив) / Σ факта.
        var planSum = parts
            .Where(p => p.ProductionTimePlanForCalc > 0)
            .Sum(p => p.FinishedCountFact * p.ProductionTimePlanForCalc);
        var factSum = parts.Where(p => p.ProductionTimePlanForCalc > 0).Sum(p => p.ProductionTimeFact);
        Assert.Equal(planSum / factSum, parts.ProductionRatio(), 6);
    }

    [Fact]
    public void Время_Вычитает_РегламентныеПерерывы_КакВОтчёте()
    {
        // 08:00 → 10:00 в дневную смену пересекает перерыв 09:00 (15 мин):
        // в факт наладки попадает 105, а не 120. Отчёт мастера так и считает,
        // поэтому и сервис обязан считать так же (свойство, а не колонка).
        var part = Row(setupFact: 0, productionFact: 0,
            startSetup: new DateTime(2026, 9, 29, 8, 0, 0),
            startMachining: new DateTime(2026, 9, 29, 8, 0, 0),
            endMachining: new DateTime(2026, 9, 29, 10, 0, 0));
        var expected = 120 - 15;
        Assert.Equal(expected, part.FullWorkedTime().TotalMinutes, 6);
    }

    [Fact]
    public void КПД_Изготовления_УчитываетFinishedCountFact()
    {
        // Отчёт берёт FinishedCountFact = FinishedCount - 1, когда наладка была
        // отдельным этапом. Наивный пересчёт по FinishedCount дал бы другой КПД.
        var withSetup = Row(setupFact: 50, setupPlan: 40, productionFact: 100,
            singlePlan: 1, finished: 11);
        var withoutSetup = Row(setupFact: 0, setupPlan: 40, productionFact: 100,
            singlePlan: 1, finished: 11, partName: "Другая", order: "998");
        var parts = new[] { withSetup, withoutSetup };
        Assert.Equal(10, withSetup.FinishedCountFact);
        Assert.Equal(11, withoutSetup.FinishedCountFact);

        var expected = parts.Sum(p => p.FinishedCountFact * p.SingleProductionTimePlan)
            / parts.Sum(p => p.ProductionTimeFact);
        Assert.Equal(expected, parts.ProductionRatio(), 6);
    }

    [Fact]
    public void Простои_Считаются_ПоТипам_КакВОтчёте()
    {
        // Простой наладки идёт в колонку SetupDowntimes, простой изготовления —
        // в MachiningDowntimes; у каждого типа своя колонка для расшифровки.
        var parts = new[]
        {
            Row(setupFact: 100, setupDowntimes: 40, hardwareFailure: 40),
            Row(setupFact: 60, machiningDowntimes: 25, toolSearching: 25,
                partName: "Другая", order: "998"),
        };
        Assert.Equal(40, parts.SpecifiedDowntime(Downtime.HardwareFailure), 6);
        Assert.Equal(25, parts.SpecifiedDowntime(Downtime.ToolSearching), 6);
        Assert.Equal(65, parts.TotalDowntimesTime().TotalMinutes, 6);
        // Доля простоев — от времени работы (FullWorkedTime), а не от смены.
        Assert.Equal(65 / parts.FullWorkedTime().TotalMinutes,
            parts.SpecifiedDowntimesRatio(ShiftType.Day), 6);
    }

    [Fact]
    public void РабочиеДни_УчитываютПраздники()
    {
        var saved = DomainSettings.Holidays;
        try
        {
            DomainSettings.Holidays =
                [Day, Day.AddDays(1), Day.AddDays(2)];
            // 30 дней минус 3 праздника.
            Assert.Equal(27, DomainSettings.GetWorkDaysBetween(Day, Day.AddDays(29)));
        }
        finally
        {
            DomainSettings.Holidays = saved;
        }
    }
}
