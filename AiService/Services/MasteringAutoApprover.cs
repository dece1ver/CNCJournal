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

    // Держать в синхроне с AiHistorySensitiveReasons в remeLog/Models/Part.cs. В отличие от
    // «Освоение» история здесь НЕ проверяется вообще: «Отсутствие/Некорректные нормативов»
    // верим слепо по выбору мастера — норматив мог не совпасть из-за другого выданного
    // техпроцесса или ещё нескольких причин, которых в присланных полях просто не видно, и
    // формализовать их regex'ом/сверкой истории нельзя, в отличие от FinishedCount для
    // «Освоение». Модель verify-part стабильно требует «конкретику» вопреки прямому тексту
    // промпта (прогон 27.07: id «Кулачки», «Заглушка НМГ48-03-509-01» х2 — ok=false).
    private static readonly string[] NormativesReasons =
    {
        "Отсутствие нормативов",
        "Некорректные нормативы",
    };

    // «Изготовление не по техпроцессу» + глоссарийная формулировка master_check.txt правило 4 —
    // модель стабильно отклоняет и эти, несмотря на явные примеры в промпте (прогон 29.07:
    // «Заложена 1 установка» на трёх записях подряд ok=false с «требуется указать, что именно
    // отклонилось»). Ловим только ДВЕ regex-надёжные формулировки глоссария — «Заложен(а/о/ы) X»
    // (X = установка/операция/станок, число и род не важны) и «деталь/заготовка с других
    // станков» — не конкретное упоминание бренда станка (тот особый случай регэкспом не
    // формализовать, останется за моделью).
    private const string NotByProcessReason = "Изготовление не по техпроцессу";
    private static readonly Regex LaidOutWord = new(@"залож\w*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LaidOutNoun = new(
        @"устан\w*|операц\w*|станк\w*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OtherMachinesRoute = new(
        @"с\s+других\s+станк\w*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Outcome(
        List<string> RemovedSignals,
        List<string> AutoExcludes);

    /// <summary>
    /// Причина наладки самодостаточна (verify-part может подтверждать без обращения к модели):
    /// «Освоение» — по правилам <see cref="IsConfirmedMastering"/>; «Отсутствие/Некорректные
    /// нормативов» — безусловно, история не проверяется (см. комментарий у NormativesReasons);
    /// «Изготовление не по техпроцессу» — только для regex-надёжных формулировок глоссария.
    /// </summary>
    public static bool IsSetupReasonSelfSufficient(PartContext p)
    {
        var reason = p.MasterSetupComment.Trim();
        if (reason.Equals("Освоение", StringComparison.OrdinalIgnoreCase))
            return IsConfirmedMastering(p);
        if (NormativesReasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
            return true;
        if (reason.Equals(NotByProcessReason, StringComparison.OrdinalIgnoreCase))
            return MatchesNotByProcessGlossary(EffectiveDetail(p.MasterSetupDetail, p.MasterComment));
        return false;
    }

    /// <summary>
    /// Причина изготовления самодостаточна: «Отсутствие/Некорректные нормативов» — безусловно;
    /// «Изготовление не по техпроцессу» — только для regex-надёжных формулировок глоссария.
    /// «Освоение» для изготовления не рассматривается — в текущем регламенте это причина наладки.
    /// </summary>
    public static bool IsMachiningReasonSelfSufficient(PartContext p)
    {
        var reason = p.MasterMachiningComment.Trim();
        if (NormativesReasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
            return true;
        if (reason.Equals(NotByProcessReason, StringComparison.OrdinalIgnoreCase))
            return MatchesNotByProcessGlossary(EffectiveDetail(p.MasterMachiningDetail, p.MasterComment));
        return false;
    }

    private static bool MatchesNotByProcessGlossary(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return false;
        if (OtherMachinesRoute.IsMatch(detail)) return true;
        return LaidOutWord.IsMatch(detail) && LaidOutNoun.IsMatch(detail);
    }

    private static string EffectiveDetail(string detail, string archiveComment) =>
        string.IsNullOrWhiteSpace(detail) ? archiveComment : detail;

    /// <summary> Диспетчер для verify-part: покрывает ли C# аномалию по имени поля целиком. </summary>
    public static bool IsAnomalyFieldSelfSufficient(string field, PartContext p) => field switch
    {
        nameof(PartContext.MasterSetupDetail) => IsSetupReasonSelfSufficient(p),
        nameof(PartContext.MasterMachiningDetail) => IsMachiningReasonSelfSufficient(p),
        _ => false,
    };

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
