using AiService.Models;
using System.Text.Json;

namespace AiService.Services;

/// <summary>
/// Пишет каждый выполненный анализ (полный запрос + итоговый ответ) в JSON-файл.
/// Назначение — датасет для offline-replay: прогон исторических дней через новые
/// версии промпта/модели и сверка с решениями аналитиков (tools/ai-replay).
/// Ошибки записи не влияют на анализ — только warning в лог.
/// </summary>
public class RequestLog(IConfiguration configuration, ILogger<RequestLog> logger)
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task WriteAsync(AnalyzeRequest request, AnalyzeResponse response, string endpoint)
    {
        try
        {
            if (!configuration.GetValue("RequestLog:Enabled", true)) return;

            var baseDir = configuration.GetValue<string>("RequestLog:Directory") ?? "request_logs";
            if (!Path.IsPathRooted(baseDir))
                baseDir = Path.Combine(AppContext.BaseDirectory, baseDir);

            var now = DateTime.Now;
            var dir = Path.Combine(baseDir, now.ToString("yyyy-MM"));
            Directory.CreateDirectory(dir);

            var machineSafe = string.Concat(request.Machine.Select(c =>
                char.IsLetterOrDigit(c) ? c : '_'));
            var fileName = $"{request.ShiftDate}_{machineSafe}_{now:yyyyMMdd-HHmmss-fff}.json";

            var payload = new
            {
                loggedAt = now,
                endpoint,
                promptVersion = response.PromptVersion,
                request,
                response,
            };

            await File.WriteAllTextAsync(
                Path.Combine(dir, fileName),
                JsonSerializer.Serialize(payload, _json));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось записать request-лог для {Machine} {Date}",
                request.Machine, request.ShiftDate);
        }
    }
}
