using AiService.Models;
using System.Xml.Linq;

namespace AiService.Services;

/// <summary>
/// Вычисляет очевидные сигналы из числовых данных.
/// LLM не считает коэффициенты.
/// </summary>
public static class SignalDetector
{
    public static List<string> Detect(AnalyzeRequest req)
    {
        var daySignals = new List<string>(req.Signals);
        return [.. daySignals.Distinct()]; // заглушка
    }
}

