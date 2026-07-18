using AiService.Models;

namespace AiService.Services;

/// <summary>
/// Детерминированная подготовка запроса ДО всех проверок (hard rules, промпт,
/// эхо сигналов в ответ). Удаляет клиентские сигналы, физически бессмысленные
/// для состояния детали — они не должны попадать ни в промпт, ни в правило
/// «2+ сигналов от системы», ни в список сигналов для аналитика.
/// </summary>
public static class RequestShaper
{
    public static void Shape(AnalyzeRequest request)
    {
        foreach (var p in request.Parts)
        {
            // «Машинное время >= штучного норматива» при б/и: норматив штучный,
            // а изготовления не было — сравнение не имеет физического смысла.
            // Клиент remeLog с какой-то версии сам не шлёт этот сигнал при б/и,
            // но старые клиенты и уже сохранённые запросы отсекаем здесь.
            if (p.NoProductionHappened && p.Signals.Count > 0)
            {
                p.Signals.RemoveAll(s =>
                    s.Contains("Машинное время", StringComparison.OrdinalIgnoreCase)
                    && s.Contains(">= штучного норматива", StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
