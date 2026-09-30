using AiService.Models;
using Microsoft.Data.SqlClient;
using remeLog.Core;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using JournalPart = remeLog.Models.Part;

namespace AiService.Services;

// ═══ Операторская статистика: числа из отчётов, а не из головы ДОЗОра ═══
//
// Ключевое требование: числа должны совпадать с тем, что мастер видит в отчёте.
// Поэтому формулы здесь НЕ переписаны — считает remeLog.Core (PartExtensions,
// Shifts, ShiftType, DomainSettings) теми же функциями, что и суточный отчёт
// мастера и отчёт по оператору. Своя «примерно такая же» формула — это
// гарантированное расхождение с отчётом на первой же неделе.
//
// Проверено на боевых данных: сумма (SetupTimeFact + ProductionTimeFact +
// SetupDowntimes + MachiningDowntimes + PartialSetupTime) по строкам смены даёт
// ровно ту же «неотмеченную» величину, что хранит мастер в cnc_shifts
// (Mazak QTS350 29.09 День: мастер 10 мин = расчёт 10 мин).
//
// Расхождения с отчётом, которые здесь СОЗНАТЕЛЬНО не воспроизводятся:
//  * «Считать по исправленным нормативам» — переключатель интерфейса мастера
//    (remeLog.Core.Models.Part.CalcFixed), в БД его нет. Считаем по нормативам
//    техкарты и говорим об этом в ответе.
//  * Итоговый коэффициент по разряду (лестница cnc_qualifications) — управленческая
//    оценка, оператору не показывается.

public sealed class OperatorStatsService(
    IConfiguration config, ILogger<OperatorStatsService> logger)
{
    /// <summary>Порог, с которого мастер требует причину неотмеченного простоя.
    /// Общий с ShiftReportRules.DowntimeReasonRatioThreshold.</summary>
    public const double UnmarkedAttentionRatio = 0.1;

    /// <summary>Частичная наладка, выше которой мастер просит объяснение
    /// (ShiftReportRules.PartialSetupAttentionRatio).</summary>
    public const double PartialSetupAttentionRatio = 0.3;

    /// <summary>Доля простоев, выше которой мастер обязан написать комментарий
    /// (remeLog.Core.Models.Part — валидация строки).</summary>
    public const double DowntimeCommentRatio = 0.5;

    /// <summary>Простои, для которых инструкция оператора требует пояснения:
    /// дополнительная информация для склада и для технической службы.</summary>
    public static readonly IReadOnlyList<string> DowntimesNeedingComment =
        ["Отказ оборудования", "Поиск и получение инструмента"];

    private static readonly (Downtime Type, string Title)[] DowntimeTitles =
    [
        (Downtime.CreateNcProgram, "Написание УП"),
        (Downtime.Maintenance, "Обслуживание"),
        (Downtime.ToolSearching, "Поиск и получение инструмента"),
        (Downtime.ToolChanging, "Замена инструмента"),
        (Downtime.Mentoring, "Помощь / обучение"),
        (Downtime.ContactingDepartments, "Обращение в другие службы"),
        (Downtime.FixtureMaking, "Изготовление оснастки и калибров"),
        (Downtime.HardwareFailure, "Отказ оборудования"),
        (Downtime.Special, "Особый простой"),
    ];

    private readonly string _connectionString = config.GetValue("Database:ConnectionString", "");
    private readonly int _timeoutSecs =
        Math.Clamp(config.GetValue("Database:CommandTimeoutSeconds", 15), 5, 120);
    private readonly int _maxParts =
        Math.Clamp(config.GetValue("Chat:StatsMaxParts", 400), 20, 2000);
    private readonly int _maxPeriodDays =
        Math.Clamp(config.GetValue("Chat:StatsMaxDays", 92), 1, 366);

    // ───────────────────────── Загрузка строк журнала ─────────────────────────

    /// <summary>
    /// Строки журнала оператора за период. Фильтр по фамилии — в SQL (префикс,
    /// зеркало GetOperatorHistoryAsync), по инициалам — в коде. Конструктор Part
    /// тот же, что у клиента, колонки читаются по имени.
    /// </summary>
    private async Task<List<JournalPart>> ReadPartsAsync(
        string operatorQuery, string? machine, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var parts = new List<JournalPart>();
        var parsed = OperatorQueryParser.Parse(operatorQuery);
        if (parsed == null || _connectionString.Length == 0)
            return parts;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await EnsureReportSettingsAsync(connection, ct);

        const string sql =
            "SELECT TOP (@MaxParts) Guid, Machine, Shift, ShiftDate, Operator, PartName, " +
            "[Order], Setup, FinishedCount, DefectiveCount, TotalCount, StartSetupTime, " +
            "StartMachiningTime, SetupTimeFact, EndMachiningTime, SetupTimePlan, " +
            "SetupTimePlanForReport, SingleProductionTimePlan, ProductionTimeFact, " +
            "MachiningTime, SetupDowntimes, MachiningDowntimes, PartialSetupTime, " +
            "CreateNcProgramTime, MaintenanceTime, ToolSearchingTime, ToolChangingTime, " +
            "MentoringTime, ContactingDepartmentsTime, FixtureMakingTime, " +
            "HardwareFailureTime, SpecialDowntimeTime, OperatorComment, MasterSetupComment, " +
            "MasterMachiningComment, SpecifiedDowntimesComment, UnspecifiedDowntimeComment, " +
            "MasterComment, MasterSetupDetail, MasterMachiningDetail, FixedSetupTimePlan, " +
            "FixedProductionTimePlan, ExcludeFromReports " +
            "FROM Parts " +
            "WHERE Operator LIKE @like " +
            "  AND ShiftDate >= @From AND ShiftDate <= @To " +
            "  AND (@Machine IS NULL OR Machine = @Machine) " +
            "ORDER BY ShiftDate, StartSetupTime;";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = _timeoutSecs;
        cmd.Parameters.AddWithValue("@MaxParts", _maxParts);
        cmd.Parameters.AddWithValue("@like", OperatorQueryParser.EscapeLike(parsed.LastName) + "%");
        cmd.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@Machine", (object?)machine ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var stored = Str(reader, 4);
            if (!SqlAgentDataSource.OperatorInitialsMatch(
                    stored, parsed.FirstInitial, parsed.PatronymicInitial))
                continue;
            parts.Add(new JournalPart(
                guid: reader.GetGuid(0),
                machine: Str(reader, 1),
                shift: Str(reader, 2),
                shiftDate: reader.GetDateTime(3),
                @operator: stored,
                partName: Str(reader, 5),
                order: Str(reader, 6),
                setup: Int(reader, 7),
                finishedCount: Dbl(reader, 8),
                defectiveCount: Int(reader, 9),
                totalCount: Int(reader, 10),
                startSetupTime: Date(reader, 11),
                startMachiningTime: Date(reader, 12),
                setupTimeFact: Dbl(reader, 13),
                endMachiningTime: Date(reader, 14),
                setupTimePlan: Dbl(reader, 15),
                setupTimePlanForReport: Dbl(reader, 16),
                singleProductionTimePlan: Dbl(reader, 17),
                productionTimeFact: Dbl(reader, 18),
                machiningTime: TimeSpan.FromTicks(Long(reader, 19)),
                setupDowntimes: Dbl(reader, 20),
                machiningDowntimes: Dbl(reader, 21),
                partialSetupTime: Dbl(reader, 22),
                createNcProgramTime: Dbl(reader, 23),
                maintenanceTime: Dbl(reader, 24),
                toolSearchingTime: Dbl(reader, 25),
                toolChangingTime: Dbl(reader, 26),
                mentoringTime: Dbl(reader, 27),
                contactingDepartmentsTime: Dbl(reader, 28),
                fixtureMakingTime: Dbl(reader, 29),
                hardwareFailureTime: Dbl(reader, 30),
                specialDowntimeTime: Dbl(reader, 31),
                operatorComment: Str(reader, 32),
                masterSetupComment: Str(reader, 33),
                masterMachiningComment: Str(reader, 34),
                specifiedDowntimesComment: Str(reader, 35),
                unspecifiedDowntimesComment: Str(reader, 36),
                masterComment: Str(reader, 37),
                masterSetupDetail: Str(reader, 38),
                masterMachiningDetail: Str(reader, 39),
                fixedSetupTimePlan: Dbl(reader, 40),
                fixedMachineTimePlan: Dbl(reader, 41),
                excludeFromReports: !reader.IsDBNull(42) && reader.GetBoolean(42)));
        }
        return parts;
    }

    // Настройки расчёта живут в статике remeLog.Core — заполняем из БД, как клиент.
    private static readonly SemaphoreSlim SettingsLock = new(1, 1);
    private static DateTime _settingsLoadedUtc = DateTime.MinValue;
    private static readonly TimeSpan SettingsTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// MaxSetupLimit / MaxSetupLimits / Holidays нужны формулам Part и
    /// GetAiCheckAnomalies. Без них проверки считали бы по значениям по умолчанию
    /// и разошлись бы с отчётом мастера. TTL 30 минут, при сбое — дефолты.
    /// </summary>
    private async Task EnsureReportSettingsAsync(SqlConnection connection, CancellationToken ct)
    {
        if (DateTime.UtcNow - _settingsLoadedUtc < SettingsTtl)
            return;
        await SettingsLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _settingsLoadedUtc < SettingsTtl)
                return;
            var holidays = new List<DateTime>();
            await using (var cfg = new SqlCommand(
                "SELECT max_setup_limit, Holidays FROM cnc_remelog_config", connection))
            {
                cfg.CommandTimeout = _timeoutSecs;
                await using var r = await cfg.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    if (!r.IsDBNull(0)) DomainSettings.MaxSetupLimit = r.GetDouble(0);
                    if (!r.IsDBNull(1)) holidays.Add(r.GetDateTime(1));
                }
            }
            DomainSettings.Holidays = [.. holidays];
            var limits = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            await using (var m = new SqlCommand(
                "SELECT Name, SetupCoefficient FROM cnc_machines", connection))
            {
                m.CommandTimeout = _timeoutSecs;
                await using var r = await m.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct) && !r.IsDBNull(0) && !r.IsDBNull(1))
                    limits[r.GetString(0)] = r.GetDouble(1);
            }
            DomainSettings.MaxSetupLimits.Clear();
            foreach (var (name, value) in limits)
                DomainSettings.MaxSetupLimits[name] = value;
            _settingsLoadedUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Не роняем чат: считаем на дефолтах.
            logger.LogWarning(ex, "Настройки расчёта (remeLog.Core) не загрузились — дефолты");
        }
        finally
        {
            SettingsLock.Release();
        }
    }

    // ───────────────────────── Проверка смены ─────────────────────────

    /// <summary>
    /// Что оператору стоит доделать в своей смене. Неотмеченное время считается
    /// ровно как в суточном отчёте мастера, но по строкам оператора.
    /// </summary>
    public async Task<ShiftReviewResult?> LoadShiftReviewAsync(
        string operatorQuery, string? machine, DateOnly? date, string? shiftName, CancellationToken ct)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.Today);
        var parts = await ReadPartsAsync(operatorQuery, machine, day, day, ct);
        // Название смены из аргумента модели может быть мусором (бывало «{}»),
        // поэтому берём только известное значение, иначе — смену из строк журнала.
        var shift = KnownShift(shiftName) ?? parts.FirstOrDefault()?.Shift ?? Shifts.Day;
        var shiftType = ShiftTypeOf(shift);
        var own = parts.Where(p => p.Shift == shift).ToList();
        var dayStart = day.ToDateTime(TimeOnly.MinValue);

        // Та же сумма, что в суточном отчёте (PartExtensions.UnspecifiedDowntimes).
        var marked = own.Sum(p => p.SetupTimeFact + p.ProductionTimeFact
            + p.SetupDowntimes + p.MachiningDowntimes + p.PartialSetupTime);
        var minutes = (int)shiftType;

        var remarks = new List<DowntimeRemark>();
        foreach (var (type, title) in DowntimeTitles)
        {
            var byType = own.SpecifiedDowntime(type);
            if (byType <= 0) continue;
            remarks.Add(new DowntimeRemark(
                title, byType,
                DowntimesNeedingComment.Any(t =>
                    title.StartsWith(t, StringComparison.OrdinalIgnoreCase))));
        }

        // Аномалии строк — ровно те, что мастер увидит при проверке.
        // Аномалии строк — те же, что увидит мастер. КПД 0% при отсутствии
        // норматива отбрасываем: это следствие отсутствия норматива (оно уже
        // в списке отдельной строкой), а не отдельная проблема оператора —
        // иначе он видит «КПД 0% вне нормы 70–120%» и пугается зря (кейс
        // проверки на прочность A2/D1).
        var anomalies = own
            .SelectMany(p => p.GetAiCheckAnomalies().Select(a =>
                (Part: $"{p.PartName}{(string.IsNullOrWhiteSpace(p.Order) ? "" : $" ({p.Order})")}",
                 Text: a.Description, NoNorm: NoNormative(p))))
            .Where(x => !(x.NoNorm && x.Text.Contains("КПД", StringComparison.Ordinal)))
            .GroupBy(x => x.Part + "|" + x.Text, StringComparer.Ordinal)
            .Select(g => (Field: g.First().Part, Description: g.First().Text))
            .ToList();

        return new ShiftReviewResult(
            machine ?? own.FirstOrDefault()?.Machine ?? "",
            shift, day,
            own.FirstOrDefault()?.Operator ?? operatorQuery,
            minutes,
            Math.Round(marked, 1),
            Math.Round(minutes - marked, 1),
            own.Count,
            Math.Round(own.SpecifiedDowntimes(shiftType), 1),
            Round3(own.SpecifiedDowntimesRatio(shiftType)),
            Math.Round(own.PartialSetup(dayStart, dayStart, shiftType), 1),
            Round3(own.PartialSetupRatio(dayStart, dayStart, shiftType)),
            own.Count(p => p.EndMachiningTime == DateTime.MinValue),
            remarks,
            anomalies);
    }

    // ───────────────────────── Статистика за период ─────────────────────────

    /// <summary>
    /// Агрегаты за период — те же, что в отчёте по оператору: смены, М/Л, наладок,
    /// деталей, КПД наладки/изготовления, простои по типам, доля простоев и
    /// условия включения в расчёт коэффициента.
    /// </summary>
    public async Task<PeriodStatsResult?> LoadPeriodStatsAsync(
        string operatorQuery, string? machine, int days, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var toDay = to ?? DateOnly.FromDateTime(DateTime.Today);
        var span = Math.Clamp(days <= 0 ? 30 : days, 1, _maxPeriodDays);
        var fromDay = from ?? toDay.AddDays(-span + 1);
        if (fromDay > toDay) (fromDay, toDay) = (toDay, fromDay);
        if (toDay.DayNumber - fromDay.DayNumber >= _maxPeriodDays)
            fromDay = toDay.AddDays(-_maxPeriodDays + 1);

        var all = await ReadPartsAsync(operatorQuery, machine, fromDay, toDay, ct);
        if (all.Count == 0)
            return null;
        // Отчёт по оператору по умолчанию не берёт исключённые мастером записи.
        var parts = all.Where(p => !p.ExcludeFromReports).ToList();

        var (total, included) = parts.Aggregate(
            (Total: TimeSpan.Zero, Included: TimeSpan.Zero),
            (acc, p) => (acc.Total + p.FullWorkedTime(), acc.Included + NotExcludedTime(p)));

        // Условие по сменам — как в отчёте по оператору: уникальные даты,
        // минимум рабочих дней / 6.
        var workedDates = all.Select(p => p.ShiftDate.Date).Distinct().Count();
        var workDays = DomainSettings.GetWorkDaysBetween(
            fromDay.ToDateTime(TimeOnly.MinValue), toDay.ToDateTime(TimeOnly.MinValue));
        var minShifts = Math.Max(1, workDays / 6);

        return new PeriodStatsResult(
            all[0].Operator, machine, fromDay, toDay,
            all.Select(p => (p.ShiftDate.Date, p.Shift)).Distinct().Count(),
            workDays, minShifts,
            parts.Select(p => p.Order).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().Count(),
            parts.Count(p => p.SetupTimeFact > 0),
            parts.Count(p => p.FinishedCount > 0 || p.ProductionTimeFact > 0),
            parts.Sum(p => p.FinishedCount),
            Math.Round(parts.TotalSetupTime().TotalMinutes, 1),
            Math.Round(parts.TotalProductionTime().TotalMinutes, 1),
            Math.Round(parts.SpecifiedDowntimes(ShiftType.All), 1),
            Round3(parts.SpecifiedDowntimesRatio(ShiftType.All)),
            Math.Round(total.TotalMinutes, 1),
            Round3(parts.SetupRatio()),
            Round3(parts.ProductionRatio()),
            total > TimeSpan.Zero ? Math.Round(included.TotalMinutes / total.TotalMinutes, 3) : 0,
            [.. DowntimeTitles
                .Select(t => (Type: t.Title, Minutes: parts.SpecifiedDowntime(t.Type)))
                .Where(x => x.Minutes > 0)],
            all.Count - parts.Count,
            workedDates >= minShifts,
            included >= total * 0.3);
    }

    /// <summary>«Учётное» время строки — зеркало Xl.cs NotExcludedTime: наладка
    /// с её простоями плюс изготовление с простоями, по нормативам строки.</summary>
    private static TimeSpan NotExcludedTime(JournalPart part)
    {
        var span = TimeSpan.Zero;
        if (part.ExcludeFromReports) return span;
        if (IsValidRatio(part.SetupRatio))
            span += TimeSpan.FromMinutes(part.SetupTimeFact + part.SetupDowntimes);
        if (IsValidRatio(part.ProductionRatio))
            span += TimeSpan.FromMinutes(part.ProductionTimeFact + part.MachiningDowntimes);
        return span;
    }

    private static bool IsValidRatio(double ratio) =>
        ratio != 0 && !double.IsInfinity(ratio) && !double.IsNaN(ratio);

    private static double Round3(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Round(v, 3);

    /// <summary>В строке нет ни одного норматива — тогда «КПД 0%» беспредметно
    /// и в список замечаний оператору попадать не должен.</summary>
    private static bool NoNormative(JournalPart p) =>
        p.SetupTimePlanForCalc <= 0 && p.ProductionTimePlanForCalc <= 0;

    /// <summary>Название смены — только «День» или «Ночь», иначе null
    /// (в аргументах модели бывает мусор вида «{}» или «смена»).</summary>
    public static string? KnownShift(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (string.Equals(t, Shifts.Day, StringComparison.OrdinalIgnoreCase)) return Shifts.Day;
        if (string.Equals(t, Shifts.Night, StringComparison.OrdinalIgnoreCase)) return Shifts.Night;
        return null;
    }

    public static ShiftType ShiftTypeOf(string shift) =>
        string.Equals(shift, Shifts.Night, StringComparison.OrdinalIgnoreCase)
            ? ShiftType.Night : ShiftType.Day;

    // В таблице есть колонки, которые остаются NULL (FixedSetupTimePlan,
    // FixedProductionTimePlan, ExcludeFromReports в строках eLog), поэтому все
    // числа и даты читаем через безопасные помощники: NULL → 0 / MinValue.
    private static string Str(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
    private static double Dbl(SqlDataReader r, int i) =>
        r.IsDBNull(i) ? 0 : Convert.ToDouble(r.GetValue(i));
    private static int Int(SqlDataReader r, int i) =>
        r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    private static long Long(SqlDataReader r, int i) =>
        r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
    private static DateTime Date(SqlDataReader r, int i) =>
        r.IsDBNull(i) ? DateTime.MinValue : r.GetDateTime(i);
}
