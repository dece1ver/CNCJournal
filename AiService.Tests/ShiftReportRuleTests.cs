using AiService.Models;
using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Детерминированная проверка суточных отчётов мастера (cnc_shifts):
/// R1–R6 — hard (эскалация), S2 — рекомендательный soft.
/// Кейсы сверены с реальными данными stanki за 09.26.
/// </summary>
public class ShiftReportRuleTests
{
    private static readonly List<string> Reasons =
        ["Другое", "Организационные потери", "Отсутствие оператора", "Отсутствие электричества", "Ремонт оборудования"];

    private static AnalyzeRequest Request(params ShiftReportContext[] reports) => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-15",
        Parts = [],
        ShiftReports = [.. reports],
        DowntimeReasons = Reasons,
    };

    private static ShiftReportContext Day(
        bool exists = true,
        string master = "Степанов Евгений Юрьевич",
        double stored = 0,
        double fresh = 0,
        string reason = "",
        string comment = "",
        bool hasParts = true,
        double partialRatio = 0) => new()
    {
        Shift = "День",
        ShiftMinutes = 660,
        ReportExists = exists,
        Master = master,
        StoredUnspecifiedDowntimes = stored,
        FreshUnspecifiedDowntimes = fresh,
        DowntimeReason = reason,
        MasterComment = comment,
        HasParts = hasParts,
        PartialSetupRatio = partialRatio,
    };

    [Fact]
    public void Evaluate_CleanDay_NoSignals()
    {
        var req = Request(Day(stored: 41, fresh: 41));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
        Assert.Empty(result.HardSignals);
        Assert.Empty(result.SoftSignals);
    }

    [Fact]
    public void Evaluate_NoReports_NoSignals_OldClient()
    {
        var req = Request();

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
        Assert.Empty(result.HardSignals);
    }

    [Fact]
    public void Evaluate_MissingReport_Hard()
    {
        var req = Request(Day(exists: false));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("Нет суточного отчёта"));
    }

    [Fact]
    public void Evaluate_EmptyMaster_Hard()
    {
        var req = Request(Day(master: ""));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("не указан мастер"));
    }

    [Fact]
    public void Evaluate_R1_DowntimeWithoutReason_Hard()
    {
        // Hyundai WIA SKT21 №104, 15.09: 117 мин без причины — реально было в БД.
        var req = Request(Day(stored: 117, fresh: 117, hasParts: true));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("без причины"));
    }

    [Fact]
    public void Evaluate_R1_DowntimeWithReason_Ok()
    {
        var req = Request(Day(stored: 467, fresh: 467, reason: "Ремонт оборудования",
            comment: "порван ремень привода"));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
    }

    [Fact]
    public void Evaluate_R2_OtherWithoutDetail_Hard()
    {
        var req = Request(Day(stored: 117, fresh: 117, reason: "Другое", comment: ""));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("«Другое»"));
    }

    [Fact]
    public void Evaluate_R2_OtherWithDetail_Ok()
    {
        var req = Request(Day(stored: 94, fresh: 94, reason: "Другое",
            comment: "оператор работает на 2х станках"));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
    }

    [Fact]
    public void Evaluate_R3_SmallOverflow_Ok()
    {
        // Rontek HTC650M, 15.09: −1.0 — в допуске −10%.
        var req = Request(Day(stored: -1, fresh: -1));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
    }

    [Fact]
    public void Evaluate_R3_BigOverflow_Hard()
    {
        // Hyundai XH6300, 15.09: −450 — эскалация безусловно.
        var req = Request(Day(stored: -450, fresh: -450, reason: "Другое",
            comment: "некорректное заполнение журнала, корпус снят со станка"));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("Переполнение"));
    }

    [Fact]
    public void Evaluate_R4_StaleSnapshot_Hard()
    {
        var req = Request(Day(stored: 41, fresh: 117, reason: "Другое",
            comment: "подробный комментарий мастера о простое"));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("устарел"));
    }

    [Fact]
    public void Evaluate_R5_IdleWithParts_Hard()
    {
        var req = Request(Day(stored: 660, fresh: 660, reason: "Ремонт оборудования",
            hasParts: true));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("целиком в простое"));
    }

    [Fact]
    public void Evaluate_R5_IdleWithoutParts_Ok()
    {
        // Штатная ночная «не-смена»: простой целиком + причина + записей нет.
        var req = Request(new ShiftReportContext
        {
            Shift = "Ночь",
            ShiftMinutes = 630,
            ReportExists = true,
            Master = "Степанов Евгений Юрьевич",
            StoredUnspecifiedDowntimes = 630,
            FreshUnspecifiedDowntimes = 630,
            DowntimeReason = "Отсутствие оператора",
            HasParts = false,
        });

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
    }

    [Fact]
    public void Evaluate_R6_UnknownReason_Hard()
    {
        var req = Request(Day(stored: 100, fresh: 100, reason: "Простой по вине НЛО"));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.True(result.MustEscalate);
        Assert.Contains(result.HardSignals, s => s.Contains("вне справочника"));
    }

    [Fact]
    public void Evaluate_S2_PartialSetup_SoftOnly()
    {
        var req = Request(Day(stored: 10, fresh: 10, partialRatio: 0.35));

        var result = ShiftReportRuleEvaluator.Evaluate(req);

        Assert.False(result.MustEscalate);
        Assert.Single(result.SoftSignals);
        Assert.Contains("Частичная наладка", result.SoftSignals[0]);
    }

    [Fact]
    public void CleanModelIssues_TrimsDedupesDropsEmpty()
    {
        var cleaned = ShiftReportRuleEvaluator.CleanModelIssues(
            ["  Смена День: причина не релевантна ", "", "   ", "Смена День: причина не релевантна"]);

        Assert.Single(cleaned);
        Assert.Equal("Смена День: причина не релевантна", cleaned[0]);
    }

    [Fact]
    public void CleanModelIssues_Null_Empty()
    {
        Assert.Empty(ShiftReportRuleEvaluator.CleanModelIssues(null));
    }

    [Fact]
    public void MergeIssues_FuzzyDupesOfDet_Dropped()
    {
        // Кейс со скриншота 21.09: одно утверждение тремя формулировками.
        var det = new ShiftReportRuleResult(
            ["[День] В отчёте мастера не указан мастер",
             "[Ночь] Нет суточного отчёта мастера"], []);

        var merged = ShiftReportRuleEvaluator.MergeIssues(det,
        [
            "Смена Ночь: отсутствие суточного отчёта мастера",
            "Отсутствие суточного отчёта мастера за ночь",
            "Смена День: комментарий не о простое",
        ]);

        Assert.Equal(2 + 1, merged.Count);
        Assert.Contains("Смена День: комментарий не о простое", merged);
    }

    [Fact]
    public void MergeIssues_OtherShift_NotDropped()
    {
        var det = new ShiftReportRuleResult(["[День] В отчёте мастера не указан мастер"], []);

        var merged = ShiftReportRuleEvaluator.MergeIssues(det,
            ["Смена Ночь: отсутствие суточного отчёта мастера"]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void MergeIssues_DetFirstThenModel_NoDupes()
    {
        var det = ShiftReportRuleEvaluator.Evaluate(
            Request(Day(stored: 117, fresh: 117, hasParts: true)));

        var merged = ShiftReportRuleEvaluator.MergeIssues(det,
            ["[День] Неотмеченный простой 117мин (18%) без причины из списка",
             "Смена День: комментарий не о простое"]);

        Assert.True(merged.Count >= 2);
        // Детерминированные идут первыми, модельные — после, дублей нет.
        Assert.Equal(det.HardSignals[0], merged[0]);
        Assert.Contains("Смена День: комментарий не о простое", merged);
        Assert.Equal(merged.Count, merged.Distinct().Count());
    }

    [Fact]
    public void Summarize_CleanDay_OkLine()
    {
        var req = Request(Day(stored: 41, fresh: 41));
        var det = ShiftReportRuleEvaluator.Evaluate(req);

        var summary = ShiftReportRuleEvaluator.Summarize(req, det);

        Assert.Single(summary);
        Assert.Contains("41 мин", summary[0]);
        Assert.Contains("— ок", summary[0]);
    }

    [Fact]
    public void Summarize_StaleDay_PointsToIssues()
    {
        var req = Request(Day(stored: 41, fresh: 117, reason: "Другое",
            comment: "подробный комментарий мастера о простое"));
        var det = ShiftReportRuleEvaluator.Evaluate(req);

        var summary = ShiftReportRuleEvaluator.Summarize(req, det);

        Assert.Single(summary);
        Assert.Contains("— вопрос ниже", summary[0]);
    }

    [Fact]
    public void Summarize_MissingReport_MarksIt()
    {
        var req = Request(Day(exists: false));
        var det = ShiftReportRuleEvaluator.Evaluate(req);

        var summary = ShiftReportRuleEvaluator.Summarize(req, det);

        Assert.Single(summary);
        Assert.Contains("Нет отчёта", summary[0]);
    }

    [Fact]
    public void Summarize_NoReports_Empty_OldClient()
    {
        var det = new ShiftReportRuleResult([], []);

        Assert.Empty(ShiftReportRuleEvaluator.Summarize(Request(), det));
    }

    private static (bool Data, bool ShiftReport) Causes(
        HardRuleResult? hard = null,
        List<string>? notDowngraded = null,
        bool llmRequiresReview = false,
        bool llmHasError = false,
        bool resetEffective = false,
        ShiftReportRuleResult? shift = null,
        List<string>? modelIssues = null) =>
        ShiftReportRuleEvaluator.EscalationCauses(
            hard ?? new HardRuleResult([], []),
            notDowngraded ?? [],
            llmRequiresReview, llmHasError, resetEffective,
            shift ?? new ShiftReportRuleResult([], []),
            modelIssues ?? []);

    [Fact]
    public void EscalationCauses_CleanDay_Neither()
    {
        var (data, shift) = Causes();

        Assert.False(data);
        Assert.False(shift);
    }

    [Fact]
    public void EscalationCauses_PartHard_DataOnly()
    {
        var (data, shift) = Causes(hard: new HardRuleResult(["x"], []));

        Assert.True(data);
        Assert.False(shift);
    }

    [Fact]
    public void EscalationCauses_ShiftHard_ShiftOnly()
    {
        var det = ShiftReportRuleEvaluator.Evaluate(Request(Day(exists: false)));

        var (data, shift) = Causes(shift: det);

        Assert.False(data);
        Assert.True(shift);
    }

    [Fact]
    public void EscalationCauses_ModelIssue_ShiftOnly()
    {
        var (data, shift) = Causes(modelIssues: ["Смена День: комментарий не о простое"]);

        Assert.False(data);
        Assert.True(shift);
    }

    [Fact]
    public void EscalationCauses_Reset_KillsBoth()
    {
        var (data, shift) = Causes(
            llmRequiresReview: true, resetEffective: true,
            modelIssues: ["Смена День: комментарий не о простое"]);

        Assert.False(data);
        Assert.False(shift);
    }

    [Fact]
    public void EscalationCauses_Both_Both()
    {
        var (data, shift) = Causes(
            hard: new HardRuleResult(["x"], []),
            modelIssues: ["Смена День: комментарий не о простое"]);

        Assert.True(data);
        Assert.True(shift);
    }

    [Fact]
    public void ModelSurvivors_DetDupes_RemovedFromSignals()
    {
        var det = new ShiftReportRuleResult(["[День] Отчёт устарел: простой 10мин"], []);
        var merged = new List<string>(det.HardSignals) { "Смена День: комментарий не о простое" };

        var survivors = ShiftReportRuleEvaluator.ModelSurvivors(det, merged);

        Assert.Single(survivors);
        Assert.Contains("Смена День: комментарий не о простое", survivors);
    }

    [Fact]
    public void LlmDataCaused_RequiresSignals()
    {
        Assert.True(ShiftReportRuleEvaluator.LlmDataCaused(true, ["x"]));
        Assert.False(ShiftReportRuleEvaluator.LlmDataCaused(true, []));
        Assert.False(ShiftReportRuleEvaluator.LlmDataCaused(false, ["x"]));
    }

    [Fact]
    public void SplitShiftEchoes_ReportEchoes_Moved()
    {
        // Формулировки модели со скриншота 20.09: эхо отчёта в общем signals.
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
        [
            "Отсутствие мастера в дневной смене",
            "Устарел отчёт мастера (простой увеличился с 0 до 6 мин)",
            "Нет суточного отчёта мастера за ночную смену",
            "КПД изготовления 0% без объяснения мастера",
        ]);

        Assert.Single(data);
        Assert.Equal(3, echoes.Count);
    }

    [Fact]
    public void SplitShiftEchoes_PartSignals_Kept()
    {
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
        [
            "КПД изготовления 71% объяснён заменой сверла",
            "Низкий КПД в ночную смену",
            "Машинное время 13мин >= норматива",
        ]);

        Assert.Equal(3, data.Count);
        Assert.Empty(echoes);
    }

    [Fact]
    public void SplitShiftEchoes_ReportWithoutMasterWord_Moved()
    {
        // Модель пишет про отчёт без слова «мастер» — всё равно эхо.
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
            ["Отчёт устарел: простой увеличился с 0 до 6 мин"]);

        Assert.Empty(data);
        Assert.Single(echoes);
    }

    [Fact]
    public void ShiftOnlyExplanation_ShiftOnly_Replaces()
    {
        var text = ShiftReportRuleEvaluator.ShiftOnlyExplanation(
            dataCaused: false, shiftCaused: true);

        Assert.NotNull(text);
        Assert.Contains("суточному отчёту мастера", text);
    }

    [Fact]
    public void ShiftOnlyExplanation_WithData_Null()
    {
        Assert.Null(ShiftReportRuleEvaluator.ShiftOnlyExplanation(true, true));
        Assert.Null(ShiftReportRuleEvaluator.ShiftOnlyExplanation(false, false));
        Assert.Null(ShiftReportRuleEvaluator.ShiftOnlyExplanation(true, false));
    }

    private static AnalyzeRequest IdleNightRequest() => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-20",
        Parts = [],
        ShiftReports =
        [
            new ShiftReportContext
            {
                Shift = "День", ShiftMinutes = 660, ReportExists = true, Master = "Иванов",
                StoredUnspecifiedDowntimes = 6, FreshUnspecifiedDowntimes = 6,
                DowntimeReason = "", MasterComment = "", HasParts = true,
            },
            new ShiftReportContext
            {
                Shift = "Ночь", ShiftMinutes = 630, ReportExists = true, Master = "Иванов",
                StoredUnspecifiedDowntimes = 630, FreshUnspecifiedDowntimes = 630,
                DowntimeReason = "Отсутствие оператора", MasterComment = "", HasParts = false,
            },
        ],
        DowntimeReasons = Reasons,
    };

    [Fact]
    public void DropCommentDemands_SelfSufficientReason_Dropped()
    {
        // Кейс со скриншота 21.09: простой целиком + «Отсутствие оператора»,
        // модель требует комментарий — требование режется.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            IdleNightRequest(),
            ["Смена Ночь: отсутствие комментария к простоям при наличии причины «Отсутствие оператора»"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropCommentDemands_RelevanceComplaint_Kept()
    {
        // Претензия к содержанию ИМЕЮЩЕГОСЯ комментария — genuine S1, сохраняется.
        var req = IdleNightRequest();
        req.ShiftReports[1].MasterComment = "меняли инструмент";
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            req, ["Смена Ночь: комментарий не о простое"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void DropCommentDemands_RelevanceComplaintEmptyComment_Dropped()
    {
        // Кейс 22.09.2026 (Mazak QTS350): «без релевантного комментария» при ПУСТОМ
        // комментарии и самодостаточной причине — требовать нечего, режется.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            IdleNightRequest(),
            ["Ночная смена: простой 630 мин (100%) без релевантного комментария мастера",
             "Ночная смена: простой 100% без детального комментария мастера"]);

        Assert.Empty(kept);
        Assert.Equal(2, dropped.Count);
    }

    [Fact]
    public void DropCommentDemands_MidStringShiftTag_Dropped()
    {
        // «…в ночную смену…» внутри строки тоже привязывает вопрос к смене.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            IdleNightRequest(),
            ["Простой 630мин в ночную смену без релевантного комментария мастера"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropCommentDemands_OtherReason_Kept()
    {
        // «Другое» без детализации — требование комментария легитимно.
        var req = IdleNightRequest();
        req.ShiftReports[1].DowntimeReason = "Другое";
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            req, ["Смена Ночь: отсутствует комментарий к простою"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void DropCommentDemands_NoShiftTag_Kept()
    {
        // Вопрос без привязки к смене — консервативно сохраняем.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            IdleNightRequest(), ["Отсутствует комментарий мастера к простою"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void DropCommentDemands_NightShiftPrefix_Dropped()
    {
        // Кейс со скриншота 21.09.2026 (Rontek HTC420 №2): «Ночная смена: …»
        // вместо «Смена Ночь: …» — тег обязан распознаваться.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            IdleNightRequest(),
            ["Ночная смена: простой целиком (100%) указан как «Отсутствие оператора», но комментарий мастера отсутствует"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropReasonDemands_BezUkazaniya_Dropped()
    {
        // «без указания причины» — та же жалоба на отсутствие, что «не указана».
        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnneededReasonDemands(
            IdleNightRequest(), ["Смена День: простой 3% без указания причины"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void SplitShiftEchoes_DayDowntimeDemand_Echo()
    {
        // Дневное требование причины простоя — материал R1-порога, не данные.
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
            ["Дневной простой 22 мин (3% смены) без указания причины",
             "[Втулка] КПД изготовления 50%"]);

        Assert.Single(echoes);
        Assert.Single(data);
    }

    [Fact]
    public void DayDowntimeDemand_BelowThreshold_FullChainDropped()
    {
        // Сквозная цепочка скриншота: эхо из signals → порог 10% → вопрос снят.
        var req = IdleNightRequest();
        req.ShiftReports[0].StoredUnspecifiedDowntimes = 22;
        req.ShiftReports[0].FreshUnspecifiedDowntimes = 22;
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
            ["Дневной простой 22 мин (3% смены) без указания причины"]);

        Assert.Empty(data);
        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnneededReasonDemands(req, echoes);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropReasonDemands_BelowThreshold_Dropped()
    {
        // Кейс со скриншота: 6 мин (1%) — причина не требуется, требование режется.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnneededReasonDemands(
            IdleNightRequest(),
            ["Смена День: отсутствие причины простоя при наличии простоя 6 мин"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropReasonDemands_AboveThreshold_Kept()
    {
        // 117 мин (18%) без причины — причина действительно требуется (R1 hard).
        var req = IdleNightRequest();
        req.ShiftReports[0].StoredUnspecifiedDowntimes = 117;
        req.ShiftReports[0].FreshUnspecifiedDowntimes = 117;
        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnneededReasonDemands(
            req, ["Смена День: отсутствует причина простоя"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void DropReasonDemands_ReasonPresent_KeptRelevanceOnly()
    {
        // Причина указана, но модель сомневается в релевантности — сохраняется.
        var req = IdleNightRequest();
        req.ShiftReports[0].DowntimeReason = "Другое";
        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnneededReasonDemands(
            req, ["Смена День: причина нерелевантна простою"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void DropReasonDemands_HallucinatedAbsence_Dropped()
    {
        // Причина указана, а модель утверждает обратное — галлюцинация, режется.
        var req = IdleNightRequest();
        req.ShiftReports[0].DowntimeReason = "Ремонт оборудования";
        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnneededReasonDemands(
            req, ["Смена День: причина простоя не указана"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropNoReport_NoBlocks_AllDropped()
    {
        // Старый клиент блоков не шлёт — любой вопрос модели ни на что не опирается.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropIssuesWithoutReport(
            Request(),
            ["Смена День: отчёт устарел", "Смена Ночь: мастер не указан"]);

        Assert.Empty(kept);
        Assert.Equal(2, dropped.Count);
    }

    [Fact]
    public void DropNoReport_MissingShift_AllDropped()
    {
        // Отчёта за смену нет: «устарел», «мастер не указан» — выдумка, а пересказ
        // отсутствия режется здесь же (детерминированный hard смену уже покрывает).
        var (kept, dropped) = ShiftReportRuleEvaluator.DropIssuesWithoutReport(
            Request(Day(exists: false)),
            ["Смена День: отчёт устарел, простой вырос",
             "Смена День: мастер не указан",
             "Смена День: нет суточного отчёта мастера"]);

        Assert.Empty(kept);
        Assert.Equal(3, dropped.Count);
    }

    [Fact]
    public void DropNoReport_ExistingReport_AllKept()
    {
        var (kept, dropped) = ShiftReportRuleEvaluator.DropIssuesWithoutReport(
            Request(Day(stored: 41, fresh: 41)),
            ["Смена День: причина нерелевантна простою"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void DropNoReport_OtherShiftIssue_Kept()
    {
        // День без отчёта, ночь с отчётом: вопрос по ночи сохраняется.
        var night = Day(stored: 41, fresh: 41);
        night.Shift = "Ночь";
        var (kept, dropped) = ShiftReportRuleEvaluator.DropIssuesWithoutReport(
            Request(Day(exists: false), night),
            ["Смена Ночь: причина нерелевантна простою", "Смена День: отчёт устарел"]);

        Assert.Single(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropNoReport_UntaggedIssue_Kept()
    {
        // Без привязки к смене — консервативно сохраняется.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropIssuesWithoutReport(
            Request(Day(exists: false)),
            ["комментарий мастера не о простое"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void DropNoReport_ScreenshotRegression()
    {
        // Регрессия со скриншота 21.09.2026 (Hyundai L230A): отчёта за ночь нет,
        // детерминированный hard есть, а модель дописала пересказ отсутствия.
        // Нечёткий дедуп в MergeIssues его НЕ ловит (отчёт/отчёта — разные
        // словоформы, точных совпадений 2 при пороге 3), поэтому режется здесь.
        var req = Request(Day(exists: false));
        req.ShiftReports[0].Shift = "Ночь";
        var det = ShiftReportRuleEvaluator.Evaluate(req);
        Assert.True(det.MustEscalate);

        var (kept, dropped) = ShiftReportRuleEvaluator.DropIssuesWithoutReport(
            req, ["Смена Ночь: отсутствует суточный отчёт мастера"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
        var merged = ShiftReportRuleEvaluator.MergeIssues(det, kept);
        Assert.DoesNotContain(merged, m => m.StartsWith("Смена Ночь:"));
        Assert.Contains(merged, m => m.Contains("день закрывать нельзя"));
    }

    [Fact]
    public void Preverdict_FullIdleWithReason_NoRecords_Clear()
    {
        // Пилот 24.09.2026: ночь целиком + «Отсутствие оператора» + записей нет —
        // модель требовала комментарий, хотя такой причине добавить нечего.
        var night = Day(stored: 630, fresh: 630, reason: "Отсутствие оператора",
            comment: "", hasParts: false);
        night.Shift = "Ночь";
        night.ShiftMinutes = 630;
        var req = Request(night);
        var det = ShiftReportRuleEvaluator.Evaluate(req);
        Assert.False(det.MustEscalate);

        var pv = ShiftReportRuleEvaluator.AgentPreverdicts(req, det);

        var n = Assert.Single(pv);
        Assert.False(n.NeedsModel);
    }

    [Fact]
    public void Preverdict_SmallIdle_Clear()
    {
        // Простой 3% без причины — причина не требуется (порог 10%).
        var req = Request(Day(stored: 22, fresh: 22));
        var det = ShiftReportRuleEvaluator.Evaluate(req);

        var pv = ShiftReportRuleEvaluator.AgentPreverdicts(req, det);

        Assert.False(Assert.Single(pv).NeedsModel);
    }

    [Fact]
    public void Preverdict_LargeIdleWithReason_NeedsModel()
    {
        // Простой 61% с причиной из списка: релевантность пары — за моделью.
        var req = Request(Day(stored: 403, fresh: 403, reason: "Отсутствие оператора"));
        var det = ShiftReportRuleEvaluator.Evaluate(req);
        Assert.False(det.MustEscalate);

        var pv = ShiftReportRuleEvaluator.AgentPreverdicts(req, det);

        Assert.True(Assert.Single(pv).NeedsModel);
    }

    [Fact]
    public void Preverdict_HardShift_Clear()
    {
        // Структурное нарушение уже в HARD — модели там делать нечего.
        var req = Request(Day(exists: false));
        var det = ShiftReportRuleEvaluator.Evaluate(req);
        Assert.True(det.MustEscalate);

        var pv = ShiftReportRuleEvaluator.AgentPreverdicts(req, det);

        Assert.False(Assert.Single(pv).NeedsModel);
    }

    [Fact]
    public void DropUnknownReason_KnownReasonInList_Dropped()
    {
        // Пилот 24.09.2026 (SKT21 22.09): модель объявила «Отсутствие оператора»
        // неизвестной причиной, хотя она в закрытом списке. Жалоба неверна фактом.
        var req = Request(Day(stored: 403, fresh: 403, reason: "Отсутствие оператора"));

        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnknownReasonComplaints(
            req, ["Причина 'Отсутствие оператора' не входит в список известных причин, что может быть признаком неполного или некорректного отчёта мастера"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropUnknownReason_TrulyUnknown_Kept()
    {
        // Претензия про причину, которой правда нет в списке, — сохраняется.
        var req = Request(Day(stored: 403, fresh: 403, reason: "Отсутствие оператора"));

        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnknownReasonComplaints(
            req, ["Причина 'Телепортация' не входит в список известных причин"]);

        Assert.Single(kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public void SplitShiftEchoes_ObliqueCase_Moved()
    {
        // «некорректного отчёта мастера» (родительный падеж) — то же эхо отчёта.
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
            ["Причина 'Отсутствие оператора' не входит в список известных причин, что может быть признаком неполного или некорректного отчёта мастера"]);

        Assert.Empty(data);
        Assert.Single(echoes);
    }

    [Fact]
    public void DropUnscoped_DualTag_Dropped()
    {
        // «Смена День/Ночь:» буквально — шаблон скопирован без выбора смены
        // (пилот 24.09.2026, SKT21 22.09). Отнести не к чему.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropUnscopedShiftIssues(
            ["Смена День/Ночь: простой объясняет только простои, не КПД деталей",
             "Смена День: простой 61% без релевантного комментария"]);

        Assert.Single(kept);
        Assert.Single(dropped);
    }

    [Fact]
    public void DropRelevanceVerdicts_RelevantDropped_NegationKept()
    {
        // Кейс QTS350 2026-09-24: модель одобрила отчёт («релевантно»), но положила
        // одобрение в issues. Одобрение — не проблема; отрицание — жалоба.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropRelevanceVerdicts(
            ["Смена День: Отсутствие оператора — релевантно (работал на другом станке)",
             "Смена Ночь: комментарий нерелевантен",
             "Смена День: причина не релевантна простою"]);

        Assert.Equal(2, kept.Count);
        Assert.Single(dropped);
    }

    [Fact]
    public void SplitShiftEchoes_ShiftPrefixedSignal_Moved()
    {
        // Дубль вопроса из shift_report_issues в общем signals (там же):
        // префикс «Смена День:» — формат issues по OUTPUT-контракту.
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
            ["Смена День: Отсутствие оператора — релевантно (работал на другом станке)",
             "[Д] КПД наладки 45% без объяснения"]);

        Assert.Single(data);
        Assert.Single(echoes);
    }

    [Fact]
    public void DropKpdDowntimeLink_KpdExplainedByDowntime_Dropped()
    {
        // Кейс QTS350 2026-09-24 (v14): простой и КПД не связаны никогда —
        // жалоба неверна по построению. Легитимные (нет причины, не о простое,
        // «нерелевантно») — без связки, сохраняются.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropKpdDowntimeLinkComplaints(
            ["Смена День: отсутствие оператора (72% простой) не объясняет КПД наладки",
             "Смена Ночь: нет причины простоя",
             "Смена День: комментарий не о простое",
             "Смена День: комментарий нерелевантен"]);

        Assert.Equal(3, kept.Count);
        Assert.Single(dropped);
    }

    [Fact]
    public void SplitShiftEchoes_DayNightForms_Moved()
    {
        // Кейс Hyundai L230A 20.06.2026: «Дневной простой…» / «Ночная смена: …»
        // лежали в signals, минуя shift-дропы. «Дневной КПД…» без слов
        // смен/просто/отч и «Смена инструмента…» — данные, не задеваются.
        var (data, echoes) = ShiftReportRuleEvaluator.SplitShiftEchoes(
            ["Дневной простой 66% смены (435 мин) без подтверждения планового ТО",
             "Ночная смена: отсутствие комментария к причине 'Отсутствие оператора'",
             "Дневной КПД наладки 45% без объяснения",
             "Смена инструмента 3 раза за день"]);

        Assert.Equal(2, data.Count);
        Assert.Equal(2, echoes.Count);
    }

    [Fact]
    public void DropSelfSufficient_ConfirmationDemand_Dropped()
    {
        // Кейс Hyundai L230A 20.06.2026: «без подтверждения планового ТО»
        // при причине из списка + комментарии — требование сверх причины.
        var (kept, dropped) = ShiftReportRuleEvaluator.DropSelfSufficientCommentDemands(
            Request(Day(stored: 435, fresh: 435, reason: "Ремонт оборудования", comment: "ТО станка")),
            ["Смена День: простой 66% без подтверждения планового ТО"]);

        Assert.Empty(kept);
        Assert.Single(dropped);
    }
}
