namespace AiService.Services;

/// <summary>
/// Сопоставление soft-сигналов из HardRuleEvaluator со списком downgraded_signals
/// из ответа LLM. Промпт требует дословного копирования строки, но модели
/// перефразируют — поэтому кроме точного совпадения принимаем совпадение
/// по детали + типу сигнала. Типов сейчас два (см. HardRuleEvaluator):
/// «машинное время >= норматива» и «оператор сообщает о нормативе/технологии».
/// </summary>
public static class SoftSignalMatcher
{
    public enum SignalKind
    {
        Unknown,
        MachiningTime,
        OperatorComplaint,
    }

    public static List<string> GetNotDowngraded(
        IReadOnlyList<string> softSignals, IReadOnlyList<string> downgradedSignals) =>
        [.. softSignals.Where(s => !downgradedSignals.Any(d => Matches(s, d)))];

    public static SignalKind Classify(string signal)
    {
        if (signal.Contains("оператор сообщает", StringComparison.OrdinalIgnoreCase))
            return SignalKind.OperatorComplaint;
        if (signal.Contains("машинное время", StringComparison.OrdinalIgnoreCase))
            return SignalKind.MachiningTime;
        return SignalKind.Unknown;
    }

    /// <summary> Имя детали из префикса "[PartName] ...", либо null. </summary>
    public static string? ExtractPartName(string signal)
    {
        var t = signal.TrimStart();
        if (!t.StartsWith('[')) return null;
        var end = t.IndexOf(']');
        return end > 1 ? t[1..end] : null;
    }

    /// <summary>
    /// Клиентский сигнал-эхо remeLog «Машинное время X >= штучного норматива Y» —
    /// дубль soft-сигнала о машинном времени (см. AiServiceClient и RequestShaper).
    /// </summary>
    public static bool IsMachiningTimeEcho(string clientSignal) =>
        clientSignal.Contains("Машинное время", StringComparison.OrdinalIgnoreCase)
        && clientSignal.Contains(">= штучного норматива", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Клиентский сигнал-эхо remeLog «Оператор сообщает о некорректном нормативе
    /// или технологии» — дубль soft-сигнала о жалобе оператора (AiServiceClient).
    /// </summary>
    public static bool IsOperatorComplaintEcho(string clientSignal) =>
        clientSignal.Contains("Оператор сообщает о некорректном нормативе", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string soft, string downgraded)
    {
        var s = soft.Trim();
        var d = downgraded.Trim();
        if (string.Equals(s, d, StringComparison.OrdinalIgnoreCase)) return true;

        var kind = Classify(s);
        if (kind == SignalKind.Unknown || Classify(d) != kind) return false;

        var softPart = ExtractPartName(s);
        if (softPart == null) return false;

        var downPart = ExtractPartName(d);
        if (downPart != null)
            return string.Equals(softPart, downPart, StringComparison.OrdinalIgnoreCase);

        // Модель опустила скобки — ищем имя детали в тексте перефразированной строки.
        return d.Contains(softPart, StringComparison.OrdinalIgnoreCase);
    }
}
