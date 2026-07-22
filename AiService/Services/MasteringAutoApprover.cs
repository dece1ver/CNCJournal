using AiService.Models;
using System.Text.RegularExpressions;

namespace AiService.Services;

/// <summary>
/// Детерминированное подтверждение «Освоения» по регламенту — первый шаг инверсии
/// архитектуры (аномалию закрывает C#, модели остаётся только спорное). Правило
/// «комбобокса достаточно, отсутствие истории = подтверждение» трижды прописано в
/// промпте, но модель стабильно требует «конкретику» и «подтверждение историей»
/// (прогон 21v1: id 15, 129, 266, 285, 301, 310, 457), а её отказ пометить
/// системный сигнал частичной наладки объяснённым не лечится пост-фильтрацией.
/// Освоение считается ПОДТВЕРЖДЁННЫМ, когда:
///  • мастер выбрал «Освоение» в комбобоксе (MasterSetupComment);
///  • история не опровергает — нет ни одной записи с FinishedCount &gt; 0;
///  • MasterComment не заявляет изменение УП/КД/технологии — там конкретика
///    обязательна, релевантность оценивает модель.
/// Для подтверждённых деталей сигналы наладки (частичная наладка) снимаются ДО
/// промпта, а при КПД наладки &lt;100% строка автоматически предлагается к
/// исключению из отчётов с К1-формулировкой (Триггер 3, промпт 2.6).
/// Случаи «история противоречит» (FinishedCount&gt;0) и «освоение из-за изменения
/// УП» под условие не попадают и решаются моделью, как раньше.
/// </summary>
public static class MasteringAutoApprover
{
    private static readonly Regex RevisionClaim = new(
        @"\bуп\b|\bкд\b|технолог|программ|изменен|чертеж|чертёж",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Outcome(
        List<string> RemovedSignals,
        List<string> AutoExcludes);

    public static bool IsConfirmedMastering(PartContext p) =>
        p.MasterSetupComment.Trim().Equals("Освоение", StringComparison.OrdinalIgnoreCase)
        && !HistoryRefutes(p.PartsHistory)
        && !RevisionClaim.IsMatch(p.MasterComment);

    private static bool HistoryRefutes(PartsHistoryDto? history) =>
        history != null && history.Lines.Any(l => l.FinishedCount > 0);

    /// <summary> Мутирует request: снимает сигналы наладки подтверждённых освоений. </summary>
    public static Outcome Apply(AnalyzeRequest request)
    {
        var removed = new List<string>();
        var excludes = new List<string>();

        foreach (var p in request.Parts)
        {
            if (!IsConfirmedMastering(p)) continue;

            var setupSignals = p.Signals
                .Where(s => s.Contains("наладк", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (setupSignals.Count > 0)
            {
                p.Signals = [.. p.Signals.Except(setupSignals)];
                removed.AddRange(setupSignals.Select(s => $"[{p.PartName}] {s}"));
            }

            if (p.SetupRatio is > 0 and < 1 && !p.NoSetupHappened)
                excludes.Add(
                    $"{p.PartName}§{p.Setup}§{p.Order}§освоение: КПД наладки "
                    + $"{p.SetupRatio.Value * 100:0}% ниже 100% может негативно повлиять на К1 оператора");
        }

        return new Outcome(removed, excludes);
    }
}
