using System.Text.Json.Serialization;

namespace AiService.Models;

// ═══ Производственный чат eLog (кнопка ИИ-помощника на главном окне) ═══
// Сервер stateless: историю диалога присылает клиент каждым запросом,
// сессии не храним. Контекст смены («что происходит») — снимком в запросе.

public class ChatMessageDto
{
    /// <summary> Роль: user | assistant. System собирается сервером из промпта. </summary>
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary> Один простой активной детали eLog для контекста. </summary>
public class ChatDowntimeDto
{
    public string Name { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public string Comment { get; set; } = "";
}

public class ChatActivePartDto
{
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    public string Order { get; set; } = "";
    public int Setup { get; set; }
    public string StartSetupTime { get; set; } = "";
    public string OperatorComment { get; set; } = "";
    public List<ChatDowntimeDto> Downtimes { get; set; } = [];
}

/// <summary> Снимок «происходящего» в eLog: минимум, достаточный для
/// производственного помощника (станок, смена, оператор, активная деталь + простои). </summary>
public class ElogChatContext
{
    public string Machine { get; set; } = "";
    public string Shift { get; set; } = "";
    /// <summary>Дата смены клиента YYYY-MM-DD: без неё «сегодняшняя смена»
    /// после полуночи считалась бы от вчера.</summary>
    public string ShiftDate { get; set; } = "";
    public string OperatorName { get; set; } = "";
    public bool ShiftStarted { get; set; }
    public int PartsCount { get; set; }
    public ChatActivePartDto? ActivePart { get; set; }

    /// <summary> Типы простоя, которые оператор отмечает у себя в eLog (список
    /// клиента: eLog/Infrastructure/Text.cs, DownTimes). НЕ путать со справочником
    /// cnc_downtime_reasons — это причины простоя ЦЕЛОЙ смены, их выбирает мастер.
    /// Пустой список означает, что клиент его не прислал: тогда модель не имеет
    /// права называть типы и говорит «отметь по своей инструкции». </summary>
    public List<string> OperatorDowntimeTypes { get; set; } = [];
}

public class ChatRequest
{
    /// <summary> История диалога от клиента (старые → новые), без system. </summary>
    public List<ChatMessageDto> Messages { get; set; } = [];
    public ElogChatContext? Context { get; set; }
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    /// <summary>
    /// Клиент просит искать в сети (чекбокс «Искать в сети» в окне чата).
    /// Даже при false сервер может поискать по эвристике (см. WebSearchService.NeedsWeb),
    /// если поиск включён в конфиге.
    /// </summary>
    public bool AllowWebSearch { get; set; }
}

public class ChatSourceDto
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Snippet { get; set; } = "";
}

public class ChatResponse
{
    public string Reply { get; set; } = "";
    public List<ChatSourceDto> Sources { get; set; } = [];
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    /// <summary> Поиск в сети реально использовался для этого ответа. </summary>
    public bool WebUsed { get; set; }
    /// <summary> Сколько вызовов инструментов сделал агент (0 — ответил сразу). </summary>
    public int ToolRounds { get; set; }
    public string? Error { get; set; }
}
