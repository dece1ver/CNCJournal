using System.Text.Json.Serialization;

namespace AiService.Services;

public class OllamaService(IConfiguration config, ILogger<OllamaService> logger)
{
    private readonly string _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
    private readonly string _model = config["Ollama:Model"] ?? "qwen3:14b";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>
    /// Отправляет промпт в Ollama, возвращает сырой текст ответа модели.
    /// </summary>
    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var request = new OllamaGenerateRequest
        {
            Model = _model,
            Prompt = prompt,
            Stream = false,
            Format = "json",
            Think = false,
            Options = new()
            {
                Temperature = 0.1,
                NumCtx = 8192,
            }
        };

        logger.LogInformation("Отправка запроса в Ollama. Модель: {Model}, символов промпта: {Len}",
            _model, prompt.Length);

        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);
        var text = result?.Response ?? "";

        logger.LogInformation("Ответ Ollama получен. Символов: {Len}", text.Length);
        // Полное сырое тело — критично для диагностики пустых explanation
        // и обрезанных JSON. Уровень Debug, чтобы не засорять прод-логи,
        // включайте через appsettings при разборе конкретных кейсов.
        logger.LogDebug("Сырой ответ Ollama: {Raw}", text);

        return text;
    }

    // Внутренние DTO для Ollama API

    private class OllamaGenerateRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("format")] public string Format { get; set; } = "json";
        [JsonPropertyName("think")] public bool Think { get; set; } = false;
        [JsonPropertyName("options")] public OllamaOptions Options { get; set; } = new();
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.1;
        [JsonPropertyName("num_ctx")] public int NumCtx { get; set; } = 8192;
    }

    private class OllamaGenerateResponse
    {
        [JsonPropertyName("response")] public string Response { get; set; } = "";
    }
}