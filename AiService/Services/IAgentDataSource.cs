using AiService.Models;

namespace AiService.Services;

// ═══ Источник фактологии для tools (стадия 2) ═══
// SqlAgentDataSource — прямые read-only запросы в SQL (та же машина, см. AGENTS.md).
// RequestAgentDataSource — данные из AnalyzeRequest (стадия 1: без БД, тесты, replay).
// Контракт: null/пусто = «источник не знает», вызывающий код идёт дальше
// по fallback-цепочке и НИКОГДА не роняет цикл из-за источника.

/// <summary> Источник данных для инструментов агента. </summary>
public interface IAgentDataSource
{
    /// <summary>
    /// История детали под другими заказами, уже свёрнутая построчно.
    /// Null — у источника нет данных (вызывающий использует PartsHistoryDto
    /// из запроса или вердикт «впервые»).
    /// </summary>
    Task<PartsHistoryDto?> GetPartHistoryAsync(
        AnalyzeRequest request, string partName, int? setup, CancellationToken ct);

    /// <summary>
    /// Операторы по запросу «Фамилия И.О.» (пусто — в источнике их нет).
    /// </summary>
    Task<IReadOnlyList<OperatorInfo>> FindOperatorsAsync(string query, CancellationToken ct);

    /// <summary>
    /// Прошлые смены оператора из таблицы Parts (новые сверху, не более лимита).
    /// date — конкретный день (спросили «во вторник», «12 сентября»): только его строки.
    /// Null — у источника нет данных (без БД) или оператор не работал.
    /// </summary>
    Task<OperatorHistoryDto?> GetOperatorHistoryAsync(
        string operatorQuery, int daysBack, DateOnly? date, CancellationToken ct);

    /// <summary>
    /// Проверка доступа к БД для честного ответа «есть/нет»: количество строк
    /// в Parts и операторов. Без БД — Available=false.
    /// </summary>
    Task<DbStatusDto> GetDbStatusAsync(CancellationToken ct);

    /// <summary>
    /// Справочник типов простоев (cnc_downtime_reasons). Пусто — нет источника.
    /// </summary>
    Task<IReadOnlyList<string>> GetDowntimeReasonsAsync(CancellationToken ct);
}
