using AiService.Models;

namespace AiService.Services;

// ═══ Источник стадии 1: всё уже лежит в AnalyzeRequest ═══
// История не строится здесь — у вызывающего (AgentTools) остаётся старая
// построчная логика поверх Parts[].PartsHistoryDto (возвращаем null).
// Операторов в запросе нет — пусто (tool честно так и отвечает).

/// <summary> Источник без БД: история — null (legacy-путь в AgentTools),
/// операторы — пусто. Stateless singleton. </summary>
public sealed class RequestAgentDataSource : IAgentDataSource
{
    public static readonly RequestAgentDataSource Instance = new();

    private RequestAgentDataSource() { }

    public Task<PartsHistoryDto?> GetPartHistoryAsync(
        AnalyzeRequest request, string partName, int? setup, CancellationToken ct)
        => Task.FromResult<PartsHistoryDto?>(null);

    public Task<IReadOnlyList<OperatorInfo>> FindOperatorsAsync(string query, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<OperatorInfo>>([]);

    public Task<OperatorHistoryDto?> GetOperatorHistoryAsync(
        string operatorQuery, int daysBack, DateOnly? date, CancellationToken ct)
        => Task.FromResult<OperatorHistoryDto?>(null);

    public Task<DbStatusDto> GetDbStatusAsync(CancellationToken ct)
        => Task.FromResult(new DbStatusDto(false, "Нет строки подключения", 0, 0));

    public Task<IReadOnlyList<string>> GetDowntimeReasonsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
