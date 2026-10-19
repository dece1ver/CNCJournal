namespace AiService.Models;

// ═══ Результаты операторской статистики ═══
// Числа считает сервис теми же формулами, что и отчёты мастера
// (remeLog.Core.PartExtensions). Модель только проговаривает готовые числа:
// ничего не досчитывает сама (см. промпт, блок СТАТИСТИКА).

/// <summary> Простой без комментария там, где инструкция требует пояснения. </summary>
public sealed record DowntimeRemark(string Type, double Minutes, bool CommentMissing);

/// <summary>
/// Проверка одной смены оператора: сколько времени отмечено, что мастер увидит
/// как замечание. Неотмеченное время — ровно то, что считает мастер в суточном
/// отчёте, но по строкам самого оператора.
/// </summary>
public sealed record ShiftReviewResult(
    string Machine,
    string Shift,
    DateOnly Date,
    string Operator,
    int ShiftMinutes,
    double MarkedMinutes,
    double UnmarkedMinutes,
    int Records,
    double SpecifiedDowntimeMinutes,
    double SpecifiedDowntimeRatio,
    double PartialSetupMinutes,
    double PartialSetupRatio,
    int OpenRecords,
    IReadOnlyList<DowntimeRemark> DowntimeRemarks,
    IReadOnlyList<(string Field, string Description)> RowAnomalies);

/// <summary> Статистика оператора за период — те же агрегаты, что и отчёт по оператору. </summary>
public sealed record PeriodStatsResult(
    string Operator,
    string? Machine,
    DateOnly From,
    DateOnly To,
    int WorkedShifts,
    int WorkDays,
    int MinShiftsForCoefficient,
    int Orders,
    int Setups,
    int ProductionRows,
    double FinishedCount,
    double SetupFactMinutes,
    double ProductionFactMinutes,
    double SpecifiedDowntimeMinutes,
    double SpecifiedDowntimeRatio,
    double FullWorkedMinutes,
    double SetupRatio,
    double ProductionRatio,
    double IncludedTimeRatio,
    IReadOnlyList<(string Type, double Minutes)> DowntimeByType,
    int ExcludedFromReports,
    bool ShiftsOk,
    bool IncludedTimeOk);
