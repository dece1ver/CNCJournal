namespace AiService.Models;

// ═══ Сырые строки для истории детали (стадия 2: напрямую из БД) ═══
// Зеркало полей, которые клиент считает из таблицы Parts (см. remeLog
// PartsDb.ReadPartsHistoryAsync + Part.cs): брать больше не нужно, меньше —
// разъедется свёртка HistorySummarizer с клиентской.

/// <summary> Одна прошлая строка детали из таблицы Parts. </summary>
public record HistoryRow(
    DateTime ShiftDate,
    double FinishedCount,
    double SetupRatio,
    double ProductionRatio,
    string EffectiveMachiningReason);

/// <summary> Решение аналитика по суткам станка (таблица ai_day_reviews). </summary>
public record ReviewRow(
    DateTime ShiftDate,
    string Decision,
    string? Comment,
    string? AiExplanation,
    string? AiFeedback);

/// <summary> Оператор из cnc_operators. Qualification — int (разряд числом). </summary>
public record OperatorInfo(
    string LastName,
    string FirstName,
    string Patronymic,
    int Qualification,
    bool IsActive);

/// <summary> Одна строка прошлой работы оператора из таблицы Parts. </summary>
public record OperatorHistoryLine(
    DateTime ShiftDate,
    string Machine,
    string PartName,
    string Order,
    int Setup,
    double FinishedCount);

/// <summary> Прошлые смены оператора (новые сверху). Пусто — не работал / нет данных.
/// Approximate=true: точного совпадения по инициалам нет, показаны однофамильцы —
/// модели велено уточнить, а не выдавать за факт. </summary>
public record OperatorHistoryDto(
    string OperatorName,
    List<OperatorHistoryLine> Lines,
    bool Approximate = false);

/// <summary> Доступность БД для инструментов: доказательство цифрами, а не словами. </summary>
public record DbStatusDto(
    bool Available,
    string? Error,
    long PartsRows,
    long OperatorsCount);
