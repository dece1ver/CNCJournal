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
/// Освоение считается ПОДТВЕРЖДЁННЫМ, когда мастер выбрал «Освоение» в комбобоксе
/// (MasterSetupComment) И выполнено ЛИБО:
///  • MasterSetupDetail/MasterComment явно указывает на потерю программы («не сохранилась,
///    отрабатывали заново» и т.п.) — это ДОСТАТОЧНАЯ причина по регламенту ИМЕННО для случая
///    «деталь делалась раньше» и ПЕРЕКРЫВАЕТ опровержение историей (проверяется первой — если
///    сначала отсекать по истории, это исключение никогда не сработает для случая, для которого
///    оно и написано);
///  • ЛИБО история не опровергает (нет записи с FinishedCount &gt; 0) И деталь не заявляет иное
///    изменение УП/КД/технологии (там конкретика обязательна, релевантность — уже за моделью,
///    формализовать regex'ом нельзя).
/// Для подтверждённых деталей сигналы наладки (частичная наладка) снимаются ДО
/// промпта, а при КПД наладки &lt;100% строка автоматически предлагается к
/// исключению из отчётов с К1-формулировкой (Триггер 3, промпт 2.6).
/// </summary>
public static class MasteringAutoApprover
{
    private static readonly Regex RevisionClaim = new(
        @"\bуп\b|\bкд\b|технолог|программ|изменен|чертеж|чертёж",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // «Программа не сохранилась, отрабатывали заново» — это ПОТЕРЯ программы, не заявление
    // об ИЗМЕНЕНИИ УП/технологии (system_prompt.txt прямо называет эту формулировку достаточной
    // конкретикой без доп. проверки). Голое слово "программ" в RevisionClaim иначе ловит и её —
    // баг, найденный на проде (id 352: "Не была сохранена отработанная программа после прошлого
    // изготовления" ошибочно требовало доп. подтверждения историей).
    // ВАЖНО: "не" и "сохран" проверяются НЕЗАВИСИМО, без требования соседства — реальные формулировки
    // разносят их словами между ("не БЫЛА сохранена"), жёсткая склейка "не\s*сохран" такое не ловит
    // (первая версия фикса ошибочно предполагала соседство — не проверил на реальном тексте).
    private static readonly Regex Negation = new(@"\bне\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SaveWord = new(@"сохран", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProgramLost = new(
        @"утеря|потеря|слет",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Outcome(
        List<string> RemovedSignals,
        List<string> AutoExcludes);

    public static bool IsConfirmedMastering(PartContext p)
    {
        if (!p.MasterSetupComment.Trim().Equals("Освоение", StringComparison.OrdinalIgnoreCase)) return false;

        var detail = string.IsNullOrWhiteSpace(p.MasterSetupDetail) ? p.MasterComment : p.MasterSetupDetail;
        bool isProgramLoss = detail.Contains("программ", StringComparison.OrdinalIgnoreCase)
            && (ProgramLost.IsMatch(detail) || (Negation.IsMatch(detail) && SaveWord.IsMatch(detail)));

        // «Программа не сохранилась, отрабатывали заново» — промпт называет это ДОСТАТОЧНОЙ причиной
        // ИМЕННО для случая «деталь делалась раньше, но теперь освоение из-за изменений» — т.е. она
        // призвана перекрывать опровержение историей, а не наоборот. Проверять историю ПЕРЕД этим
        // (как было в первой версии фикса) — ошибка: тогда исключение никогда не сработает для
        // единственного случая, для которого оно писалось (id 352: история показывает деталь ранее,
        // 29-30.05, но «программа не сохранилась» — валидная причина повторного освоения).
        if (isProgramLoss) return true;

        if (HistoryRefutes(p.PartsHistory)) return false;

        return !RevisionClaim.IsMatch(detail);
    }

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
