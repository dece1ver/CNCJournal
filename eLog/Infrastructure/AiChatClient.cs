using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace eLog.Infrastructure
{
    /// <summary> Сообщение диалога для сервера (роль user/assistant). </summary>
    public class ChatMessageDto
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    public class ChatDowntimeDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("start")]
        public string Start { get; set; } = "";
        [JsonPropertyName("end")]
        public string End { get; set; } = "";
        [JsonPropertyName("comment")]
        public string Comment { get; set; } = "";
    }

    public class ChatActivePartDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("number")]
        public string Number { get; set; } = "";
        [JsonPropertyName("order")]
        public string Order { get; set; } = "";
        [JsonPropertyName("setup")]
        public int Setup { get; set; }
        [JsonPropertyName("startSetupTime")]
        public string StartSetupTime { get; set; } = "";
        [JsonPropertyName("operatorComment")]
        public string OperatorComment { get; set; } = "";
        [JsonPropertyName("downtimes")]
        public List<ChatDowntimeDto> Downtimes { get; set; } = new();
    }

    /// <summary> Снимок «происходящего»: станок, смена, оператор, активная деталь. </summary>
    public class ElogChatContextDto
    {
        [JsonPropertyName("machine")]
        public string Machine { get; set; } = "";
        [JsonPropertyName("shift")]
        public string Shift { get; set; } = "";
        [JsonPropertyName("operatorName")]
        public string OperatorName { get; set; } = "";
        [JsonPropertyName("shiftStarted")]
        public bool ShiftStarted { get; set; }
        [JsonPropertyName("partsCount")]
        public int PartsCount { get; set; }
        [JsonPropertyName("activePart")]
        public ChatActivePartDto? ActivePart { get; set; }
    }

    public class ChatRequestDto
    {
        [JsonPropertyName("messages")]
        public List<ChatMessageDto> Messages { get; set; } = new();
        [JsonPropertyName("context")]
        public ElogChatContextDto? Context { get; set; }
        [JsonPropertyName("model")]
        public string? Model { get; set; }
        [JsonPropertyName("allowWebSearch")]
        public bool AllowWebSearch { get; set; }
    }

    public class ChatSourceDto
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
        [JsonPropertyName("snippet")]
        public string Snippet { get; set; } = "";
    }

    public class ChatResponseDto
    {
        [JsonPropertyName("reply")]
        public string Reply { get; set; } = "";
        [JsonPropertyName("sources")]
        public List<ChatSourceDto> Sources { get; set; } = new();
        [JsonPropertyName("model")]
        public string? Model { get; set; }
        [JsonPropertyName("promptVersion")]
        public string? PromptVersion { get; set; }
        [JsonPropertyName("webUsed")]
        public bool WebUsed { get; set; }
        [JsonPropertyName("toolRounds")]
        public int ToolRounds { get; set; }
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public enum ChatProgressKind { Queue, Token, Tool }

    public record ChatProgress(ChatProgressKind Kind, string Text);

    /// <summary>
    /// Транспорт к AiService для производственного чата. По образцу
    /// remeLog AiServiceClient, но проще: один SSE-эндпоинт api/chat/stream
    /// (события queue/token/result/error). Очередь к GPU общая с дневным
    /// анализом — событие queue показывает позицию («позиция 2»).
    /// </summary>
    public class AiChatClient
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };
        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        private readonly string? _baseUrl;

        public AiChatClient(string? baseUrl = null)
        {
            _baseUrl = baseUrl;
        }

        private string GetUrl()
        {
            if (!string.IsNullOrWhiteSpace(_baseUrl)) return _baseUrl.TrimEnd('/');
            var host = string.IsNullOrWhiteSpace(AppSettings.Instance.AiHost)
                ? "localhost" : AppSettings.Instance.AiHost.Trim();
            var port = AppSettings.Instance.AiPort <= 0 ? 5050 : AppSettings.Instance.AiPort;
            return $"http://{host}:{port}";
        }

        public async Task<(bool Server, bool Ollama)> CheckHealthAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await _http.GetAsync($"{GetUrl()}/api/analysis/health", cts.Token);
                if (!response.IsSuccessStatusCode) return (false, false);
                using var doc = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
                var ollama = doc.RootElement.TryGetProperty("ollama", out var o)
                    && o.ValueKind == JsonValueKind.True;
                return (true, ollama);
            }
            catch
            {
                return (false, false);
            }
        }

        /// <summary>
        /// Диалог через SSE: токены ответа идут живьём в progress (Kind=Token),
        /// очередь — Kind=Queue (заменяет статус). Возвращает итоговый ответ
        /// с источниками. Событие error и обрыв без result — исключение.
        /// </summary>
        public async Task<ChatResponseDto> ChatStreamAsync(
            ChatRequestDto request,
            IProgress<ChatProgress>? progress = null,
            CancellationToken ct = default)
        {
            var payload = JsonSerializer.Serialize(request, _json);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post, $"{GetUrl()}/api/chat/stream") { Content = content };
            using var response = await _http.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string currentEvent = "";
            ChatResponseDto? result = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (line is null) break;
                if (line.Length == 0) { currentEvent = ""; continue; }
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    currentEvent = line["event:".Length..].Trim();
                    continue;
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line["data:".Length..].Trim();
                switch (currentEvent)
                {
                    case "queue":
                        progress?.Report(new ChatProgress(ChatProgressKind.Queue, QueueText(data)));
                        break;
                    case "token":
                        progress?.Report(new ChatProgress(ChatProgressKind.Token, Unquote(data)));
                        break;
                    case "tool":
                        progress?.Report(new ChatProgress(ChatProgressKind.Tool, Unquote(data)));
                        break;
                    case "result":
                        result = JsonSerializer.Deserialize<ChatResponseDto>(data, _json);
                        break;
                    case "error":
                        throw new HttpRequestException($"ИИ-помощник: {Unquote(data)}");
                }
            }

            if (result == null)
                throw new InvalidOperationException("ИИ-помощник оборвал ответ без результата.");
            if (!string.IsNullOrWhiteSpace(result.Error))
                throw new HttpRequestException($"ИИ-помощник: {result.Error}");
            return result;
        }

        private static string QueueText(string data)
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("position", out var p)
                    && p.TryGetInt32(out var pos))
                    return $"Очередь к ИИ, позиция {pos}…";
            }
            catch (JsonException) { }
            return "Ожидание очереди к ИИ…";
        }

        private static string Unquote(string data)
        {
            try
            {
                var s = JsonSerializer.Deserialize<string>(data);
                if (s != null) return s;
            }
            catch (JsonException) { }
            return data.Trim('"');
        }
    }
}
