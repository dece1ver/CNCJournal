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

    public Task WriteAsync(AnalyzeRequest request, AnalyzeResponse response, string endpoint) =>
        WriteCoreAsync(request.Machine, request.ShiftDate, response.PromptVersion, request, response, endpoint);

    // Префикс "verify_" в имени файла отделяет проверки строк от дневных анализов,
    // чтобы tools/ai-replay и ручной разбор не смешивали разноформатные логи.
    public Task WriteVerifyAsync(VerifyPartRequest request, VerifyPartResponse response) =>
        WriteCoreAsync(request.Machine, request.ShiftDate, response.PromptVersion, request, response,
            "verify-part", filePrefix: "verify_");

    /// <summary>
    /// Записывает сбой ДО получения ответа модели (Ollama вернула ошибку/упала,
    /// таймаут и т.п.) — эти запросы раньше вообще не попадали в request_logs, потому
    /// что WriteAsync/WriteVerifyAsync вызываются только при успехе (см. инцидент с
    /// 500 от Ollama 24.07.2026 — единственным источником был лог самой Ollama).
    /// Полный запрос не пишем (не нужен для этой цели, лишний объём) — только то,
    /// что нужно сопоставить с логом Ollama по времени.
    /// </summary>
    public async Task WriteFailureAsync(
        string machine, string shiftDate, string endpoint, string? model, bool think,
        long elapsedMs, Exception exception)
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

            var machineSafe = string.Concat(machine.Select(c =>
                char.IsLetterOrDigit(c) ? c : '_'));
            var fileName = $"error_{shiftDate}_{machineSafe}_{now:yyyyMMdd-HHmmss-fff}.json";

            var payload = new
            {
                loggedAt = now,
                endpoint,
                machine,
                shiftDate,
                model,
                think,
                elapsedMs,
                exceptionType = exception.GetType().Name,
                exceptionMessage = exception.Message,
            };

            await File.WriteAllTextAsync(
                Path.Combine(dir, fileName),
                JsonSerializer.Serialize(payload, _json));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось записать error-лог для {Machine} {Date}",
                machine, shiftDate);
        }
    }

    private async Task WriteCoreAsync(
        string machine, string shiftDate, string? promptVersion,
        object request, object response, string endpoint, string filePrefix = "")
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

            var machineSafe = string.Concat(machine.Select(c =>
                char.IsLetterOrDigit(c) ? c : '_'));
            var fileName = $"{filePrefix}{shiftDate}_{machineSafe}_{now:yyyyMMdd-HHmmss-fff}.json";

            var payload = new
            {
                loggedAt = now,
                endpoint,
                promptVersion,
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
                machine, shiftDate);
        }
    }
}
