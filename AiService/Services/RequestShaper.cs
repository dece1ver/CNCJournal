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
            // «Машинное время >= штучного норматива» отсекаем когда сигнал не имеет
            // смысла (условия зеркалят HardRuleEvaluator, клиент remeLog новых версий
            // сам не шлёт, старые клиенты и сохранённые запросы чистим здесь):
            //  • б/и — норматив штучный, изготовления не было;
            //  • штучная партия — в отчёты не попадает, данные не показательны;
            //  • КПД изготовления > 100% — показатели не пострадали.
            if (p.Signals.Count > 0
                && (p.NoProductionHappened
                    || p.IsSmallBatch
                    || p.ProductionRatio is > 1))
            {
                p.Signals.RemoveAll(s =>
                    s.Contains("Машинное время", StringComparison.OrdinalIgnoreCase)
                    && s.Contains(">= штучного норматива", StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
