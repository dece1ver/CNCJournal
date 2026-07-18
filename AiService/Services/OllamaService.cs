using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiService.Services;

public class OllamaService(IConfiguration config, ILogger<OllamaService> logger)
{
    private readonly string _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
    private readonly string _model = config["Ollama:Model"] ?? "qwen3:14b";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static int _queueCounter;
    private static int _currentQueueLength;

    public int QueueLength => _currentQueueLength;

    /// <summary>Встать в очередь к LLM. Возвращает позицию в очереди (1 = сейчас выполняется).</summary>
    public async Task<int> EnterQueueAsync(CancellationToken ct)
    {
        var pos = Interlocked.Increment(ref _queueCounter);
        Interlocked.Exchange(ref _currentQueueLength, pos);
        await _gate.WaitAsync(ct);
        return pos;
    }

    /// <summary>Покинуть очередь.</summary>
    public void LeaveQueue()
    {
        _gate.Release();
        var left = Interlocked.Decrement(ref _queueCounter);
        Interlocked.Exchange(ref _currentQueueLength, Math.Max(0, left));
    }

    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Встать в очередь, выполнить запрос к LLM, покинуть очередь.</summary>
    public async Task<(string Response, string? Thinking)> GenerateAsync(
    string prompt,
    bool think = false,
    IProgress<string>? thinkingProgress = null,
    CancellationToken ct = default,
    string? model = null)
    {
        await EnterQueueAsync(ct);
        try
        {
            return await GenerateCoreAsync(prompt, think, thinkingProgress, ct, model);
        }
        finally
        {
            LeaveQueue();
        }
    }

    /// <summary>Выполнить запрос к Ollama без управления очередью.</summary>
    public async Task<(string Response, string? Thinking)> GenerateCoreAsync(
    string prompt,
    bool think = false,
    IProgress<string>? thinkingProgress = null,
    CancellationToken ct = default,
    string? model = null)
    {
        var effectiveModel = string.IsNullOrWhiteSpace(model) ? _model : model;

        var request = new OllamaGenerateRequest
        {
            Model = effectiveModel,
            Prompt = prompt,
            Stream = think,
            Think = think,
            Format = think ? null : "json",
            Options = new()
            {
                Temperature = 0.1,
                NumCtx = 8192,
                NumPredict = think ? 4096 : -1,
            }
        };

        logger.LogInformation(
            "Отправка в Ollama. Модель: {Model}, think={Think}, символов: {Len}",
            effectiveModel, think, prompt.Length);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        if (!think)
        {
            var result = await response.Content
                .ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);
            var text = result?.Response ?? "";
            logger.LogInformation("Ответ без think. Символов: {Len}", text.Length);
            logger.LogDebug("Сырой ответ: {Raw}", text);
            return (text, null);
        }

        var fullResponse = new StringBuilder();
        var thinkBuffer = new StringBuilder();
        var lastReportedLen = 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.Length == 0) continue;

            logger.LogTrace("Chunk: {Chunk}", line);

            OllamaStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Не удалось разобрать chunk: {Chunk}", line);
                continue;
            }

            if (chunk == null) continue;

            if (!string.IsNullOrEmpty(chunk.Thinking))
            {
                thinkBuffer.Append(chunk.Thinking);

                if (thinkingProgress != null)
                {
                    var currentLen = thinkBuffer.Length;
                    if (currentLen > lastReportedLen)
                    {
                        var delta = thinkBuffer.ToString(lastReportedLen,
                                           currentLen - lastReportedLen);
                        var lastChar = delta[^1];

                        if (lastChar is ' ' or '\n' or '\r' or ',' or '.' or '!'
                                     or '?' or ':' or ';' or '-'
                            || delta.Length >= 10)
                        {
                            lastReportedLen = currentLen;
                            logger.LogDebug("Think delta [{L}]: [{D}]", delta.Length, delta);
                            thinkingProgress.Report(delta);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(chunk.Response))
                fullResponse.Append(chunk.Response);

            if (chunk.Done)
            {
                if (thinkingProgress != null && thinkBuffer.Length > lastReportedLen)
                {
                    var tail = thinkBuffer.ToString(lastReportedLen,
                                   thinkBuffer.Length - lastReportedLen);
                    if (!string.IsNullOrWhiteSpace(tail))
                        thinkingProgress.Report(tail);
                }
                break;
            }
        }

        var raw = fullResponse.ToString();
        var thinking = thinkBuffer.Length == 0 ? null : thinkBuffer.ToString();

        logger.LogInformation(
            "Ответ с think. Символов response: {RLen}, thinking: {TLen}",
            raw.Length, thinking?.Length ?? 0);
        logger.LogDebug("Сырой ответ: {Raw}", raw);

        return (raw, thinking);
    }

    private class OllamaStreamChunk
    {
        [JsonPropertyName("response")] public string? Response { get; set; }
        [JsonPropertyName("thinking")] public string? Thinking { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
    }

    private class OllamaGenerateRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Format { get; set; }
        [JsonPropertyName("think")] public bool Think { get; set; } = true;
        [JsonPropertyName("options")] public OllamaOptions Options { get; set; } = new();
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.1;
        [JsonPropertyName("num_ctx")] public int NumCtx { get; set; } = 8192;
        [JsonPropertyName("num_predict")] public int NumPredict { get; set; } = -1;
    }

    private class OllamaGenerateResponse
    {
        [JsonPropertyName("response")] public string Response { get; set; } = "";
    }
}