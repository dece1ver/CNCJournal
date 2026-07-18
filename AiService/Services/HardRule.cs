using AiService.Models;

namespace AiService.Services;

/// <summary>
/// Результат детерминированной проверки жёстких правил.
/// HardSignals — требуют ОБЯЗАТЕЛЬНОЙ эскалации, LLM не может отменить.
/// SoftSignals — оставлены для будущих случаев, где контекст важен.
///               Машинное время >= норматива переведено в HardSignals после того,
///               как LLM стабильно игнорировала его в soft-режиме.
/// </summary>
public record HardRuleResult(
    List<string> HardSignals,
    List<string> SoftSignals)
{
    public bool MustEscalate => HardSignals.Count > 0;
    public bool HasSoftSignals => SoftSignals.Count > 0;
}

public static class HardRuleEvaluator
{
    // Фиксированные строки из комбобокса MasterSetupComment/MasterMachiningComment,
    // которые однозначно требуют эскалации — это работа технологов/нормировщиков,
    // которые подключаются именно после эскалации.
    private static readonly HashSet<string> EscalationReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Изготовление не по техпроцессу",
        "Некорректные нормативы",
        "Отсутствие нормативов",
    };

    // Причины из комбобокса MasterMachiningComment, при наличии которых
    // и непустого MasterComment правило "машинное время >= норматива"
    // понижается до soft — LLM решает по правилам soft_signal_explanation.txt.
    private static readonly HashSet<string> MachiningTimeSoftDowngradeReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Разовое изменение времени из-за проблем с инструментом/оборудованием",
        "Несоответствующие заготовки",
    };

    // "Доработка" в причине наладки/изготовления освобождает от ряда проверок:
    // КПД = 0 и отсутствие норматива нормальны для доработки.
    private const string ReworkReason = "Доработка";

    public static HardRuleResult Evaluate(AnalyzeRequest req)
    {
        var hard = new List<string>();
        var soft = new List<string>();

        foreach (var p in req.Parts)
        {
            var hasOrder = !string.IsNullOrWhiteSpace(p.Order)
                           && !p.Order.Equals("Без М/Л", StringComparison.OrdinalIgnoreCase);

            var isReworkSetup = ReworkReason.Equals(p.MasterSetupComment, StringComparison.OrdinalIgnoreCase);
            var isReworkMachining = ReworkReason.Equals(p.MasterMachiningComment, StringComparison.OrdinalIgnoreCase);

            // комбобокс-причины, требующие участия технологов
            // Проверяем MasterSetupComment и MasterMachiningComment по фиксированному списку.
            // "Отсутствие нормативов" — только если есть заказ (доработка без норматива — норма).
            if (!string.IsNullOrWhiteSpace(p.MasterSetupComment)
                && EscalationReasons.Contains(p.MasterSetupComment))
            {
                if (p.MasterSetupComment.Equals("Отсутствие нормативов", StringComparison.OrdinalIgnoreCase)
                    && !hasOrder)
                { }
                else
                {
                    hard.Add($"[{p.PartName}] Причина наладки требует пересмотра технологии: «{p.MasterSetupComment}»");
                }
            }

            if (!string.IsNullOrWhiteSpace(p.MasterMachiningComment)
                && EscalationReasons.Contains(p.MasterMachiningComment))
            {
                if (p.MasterMachiningComment.Equals("Отсутствие нормативов", StringComparison.OrdinalIgnoreCase)
                    && !hasOrder)
                { }
                else
                {
                    hard.Add($"[{p.PartName}] Причина изготовления требует пересмотра технологии: «{p.MasterMachiningComment}»");
                }
            }

            // Правило 2: оператор сообщает о проблеме с нормативом/технологией.
            // SOFT — модель может понизить если мастер дал конкретное, причинно-релевантное
            // объяснение в MasterComment (не просто выбрал причину из комбобокса).
            // Остаётся HARD только если MasterComment пустой при наличии жалобы оператора.
            if (OperatorMentionsNormativeIssue(p.OperatorComment))
            {
                bool masterGaveConcreteExplanation =
                    !string.IsNullOrWhiteSpace(p.MasterMachiningComment)
                    || !string.IsNullOrWhiteSpace(p.MasterSetupComment);
                // Если мастер вообще ничего не написал — жёстко
                if (!masterGaveConcreteExplanation)
                    hard.Add($"[{p.PartName}] Оператор сообщает о некорректном нормативе или технологии без объяснения мастера");
                else
                    soft.Add($"[{p.PartName}] Оператор сообщает о некорректном нормативе или технологии (мастер дал объяснение — требует верификации)");
            }

            // Правило 3: КПД изготовления < 70% без объяснения мастера
            // null (= "б/и") не считается, доработка освобождена.
            if (p.ProductionRatio is { } pr && pr < 0.7
                && !p.NoProductionHappened
                && !isReworkMachining
                && string.IsNullOrWhiteSpace(p.MasterMachiningComment))
            {
                hard.Add($"[{p.PartName}] КПД изготовления {pr:0%} < 70% без объяснения мастера");
            }

            // Правило 4: КПД = 0 при наличии фактической работы
            // Наладка: КПД наладки = 0 означает SetupTimePlan = 0 при SetupTimeFact > 0.
            // Это нормально для инструментальных операций (нет заказа, нет норматива),
            // но подозрительно когда заказ есть и это не доработка.
            if (p.SetupRatio is 0
                && p.SetupTimeFact > 0
                && p.SetupTimePlan > 0
                && !p.NoSetupHappened
                && hasOrder
                && !isReworkSetup)
            {
                hard.Add($"[{p.PartName}] КПД наладки = 0% при наличии фактической наладки ({p.SetupTimeFact:0}мин) и норматива");
            }

            // Изготовление: КПД = 0 при наличии деталей и заказа — противоречие данных.
            if (p.ProductionRatio is 0
                && p.FinishedCount > 0
                && hasOrder
                && !isReworkMachining)
            {
                hard.Add($"[{p.PartName}] КПД изготовления = 0% при {p.FinishedCount:0} изготовленных деталях");
            }

            // машинное время >= норматива
            // Переведено из soft в hard: LLM стабильно игнорировала soft-сигнал,
            // не перенося его в downgraded_signals, но и не эскалируя.
            // Физический смысл: норматив = машинное время + ручные операции,
            // поэтому machiningTime >= plan означает, что на ручные операции
            // времени не осталось вообще — это структурная аномалия, не шум.
            //
            // 2026-07-15: исключение — разовые причины (проблемы с инструментом/оборудованием,
            // несоответствующие заготовки) при непустом MasterComment → soft.
            // LLM решает по правилам soft_signal_explanation.txt.
            // Без исключения или с пустым MasterComment — остаётся HARD.
            if (p.MachiningTime > 0.5 && p.SingleProductionTimePlan > 0
                && p.MachiningTime >= p.SingleProductionTimePlan
                && !p.NoProductionHappened)
            {
                var signal =
                    $"[{p.PartName}] Машинное время {p.MachiningTime:0.#}мин >= " +
                    $"норматива {p.SingleProductionTimePlan:0.#}мин " +
                    $"({p.MachiningTime / p.SingleProductionTimePlan:0%})";

                bool hasConcreteOneTimeReason =
                    !string.IsNullOrWhiteSpace(p.MasterMachiningComment)
                    && MachiningTimeSoftDowngradeReasons.Contains(p.MasterMachiningComment)
                    && !string.IsNullOrWhiteSpace(p.MasterComment);

                if (hasConcreteOneTimeReason)
                    soft.Add(signal);
                else
                    hard.Add(signal);
            }
        }

        // два и более необъяснённых сигнала от C#-клиента
        // Soft-сигналы намеренно не включаем в подсчёт (их сейчас нет, но на будущее).
        var allClientSignals = req.Parts.SelectMany(p => p.Signals).Concat(req.Signals).Distinct().ToList();
        if (allClientSignals.Count >= 2)
        {
            hard.Add($"Два и более необъяснённых сигнала от системы ({allClientSignals.Count})");
        }

        return new HardRuleResult([.. hard.Distinct()], soft);
    }

    private static bool OperatorMentionsNormativeIssue(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return false;
        var l = comment.ToLowerInvariant();

        if ((l.Contains("укладыва") || l.Contains("уложиться")) && l.Contains("норматив"))
            return false;

        return l.Contains("норматив") || l.Contains("не соответствует")
            || l.Contains("некорректн") || l.Contains("режимы не")
            || l.Contains("программа не соответ") || l.Contains("скорректировать");
    }
}