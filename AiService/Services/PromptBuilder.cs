using AiService.Models;
using System.Text;

namespace AiService.Services;

public static class PromptBuilder
{
    private const string SystemPrompt = """
    Ты — аналитик системы учёта производительности ЧПУ-станков.
    Тебе передаются данные одних суток работы одного станка.

    ОБЩИЙ ФОН: около 60-65% дней — нормальные (ok). Не ищи нарушения любой ценой.
    Если отклонение объяснено мастером или оператором — это нормально.

    ЭТО НОРМАЛЬНО, не повод для эскалации:
    - "б/н" (null КПД наладки при отсутствии наладки не требует объяснений)
    - "б/и" (null КПД изготовления при noProductionHappened = true не требует объяснений)
    - finishedCount = 0 при noProductionHappened = true (была только наладка)
    - partialSetup > 0 (наладка на стыке смен)
    - отсутствие норматива (plan = 0) без других аномалий — типично для инструментальных операций
    - простои и низкий КПД с комментарием мастера
    - освоение, написание УП, ожидание ОТК — объясняют сниженный КПД наладки
    - машинное время МЕНЬШЕ норматива — это нормально и ожидаемо, не повод для анализа
    - machiningTime ≈ 0, при finishedCount = 0 (изготовления не происходило)
    - Простои > 30% с релевантными комментариями (неисправности, организационные меропрятия)

    ОСОБО ПРО ПРИЧИНЫ "ИЗГОТОВЛЕНИЕ НЕ ПО ТЕХПРОЦЕССУ" И "НЕКОРРЕКТНЫЕ НОРМАТИВЫ":
    Если MasterSetupComment или MasterMachiningComment содержит одну из этих причин —
    это объясняет аномальные КПД (0%, очень низкий), НО не означает "всё в порядке".
    Напротив, это ПОДТВЕРЖДЕНИЕ необходимости эскалации: такие ситуации требуют
    участия технологов/нормировщиков. Система уже пометила их как жёсткие правила.
    Твоя задача — написать explanation который СВЯЗНО объясняет ЧТО именно произошло
    (деталь делалась не по тех.карте, норматив не соответствует реальному процессу и т.п.),
    а не просто констатировать "объяснение есть/отсутствует".

    ПРИЗНАКИ ДЛЯ ЭСКАЛАЦИИ (требуют твоей оценки контекста, а не формулы):
    - КПД наладки < 70% > 200% без объяснения — возможно норматив некорректен
    - КПД изготовления < 70% и > 120% без объяснения — возможно норматив некорректен
    - Частичная наладка значительно превышает норматив (> 1.5x)
    - Простои > 30% без комментария оператора и без комментария мастера или с нерелевантными комментариями

    КАЛИБРОВКА CONFIDENCE:
    0.9-1.0 — ситуация однозначна (явно ok или явно проблема)
    0.7-0.8 — один ясный признак
    0.6     — пограничный признак, есть сомнение
    Никогда не ставь 0.5 "по умолчанию" — это запрещённое значение,
    confidence всегда должен отражать реальную уверенность.
    """;

    private const string SoftSignalExplanation = """
    SOFT-СИГНАЛЫ (см. список в конце) — по умолчанию эскалируют, но ты МОЖЕШЬ
    понизить каждый из них в downgraded_signals при наличии ПРИЧИННО-РЕЛЕВАНТНОГО
    объяснения. Правила понижения:

    1. МАШИННОЕ ВРЕМЯ >= НОРМАТИВА:
       Штучный норматив = станочное время + ручные операции. Если машинное время
       уже равно или превышает весь норматив — структурная проблема.
       ПОНИЗИТЬ можно только если комментарий оператора/мастера содержит прямое
       указание на причину роста именно станочного времени (снижение режимов
       резания, проблема с инструментом/программой, состояние станка).
       НЕ понижать если комментарий объясняет что-то другое (простой, ожидание,
       поход в другой отдел).

    2. ОПЕРАТОР СООБЩАЕТ О НЕКОРРЕКТНОМ НОРМАТИВЕ (мастер дал объяснение):
       Мастер выбрал причину из списка и/или написал комментарий.
       ВЕРИФИЦИРУЙ связность: соответствует ли объяснение мастера фактическим данным?
       Проверь:
       a) Причина из комбобокса логически объясняет именно этот вид отклонения
          (пример: 'Разовое изменение времени из-за проблем с инструментом/оборудованием'
          при КПД 60% — логично; та же причина при КПД 5% — сомнительно).
       б) MasterComment конкретно описывает проблему (не просто повторяет
          комментарий оператора и не является пустым/формальным).
       в) Описанная причина является РАЗОВОЙ (проблема с конкретным инструментом,
          материалом), а не системной (норматив в принципе неверный).
       ПОНИЗИТЬ можно если все три пункта выполнены.
       НЕ понижать если MasterComment пустой, формальный, или описывает
       системную проблему норматива/технологии.
    """;

    public static string Build(AnalyzeRequest req, HardRuleResult hardRules)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SystemPrompt);

        if (hardRules.SoftSignals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(SoftSignalExplanation);
        }

        sb.AppendLine();
        sb.AppendLine($"Станок: {req.Machine}");
        sb.AppendLine($"Дата: {req.ShiftDate}");
        sb.AppendLine($"Записей: {req.Parts.Count}");

        if (req.Signals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Сигналы уровня дня:");
            foreach (var sig in req.Signals)
                sb.AppendLine($" ⚠ {sig}");
        }

        sb.AppendLine();

        foreach (var p in req.Parts)
        {
            var setupR = p.SetupRatio.HasValue ? $"{p.SetupRatio.Value:0%}" : "б/н";
            var prodR = p.ProductionRatio.HasValue ? $"{p.ProductionRatio.Value:0%}" : "б/и";
            var dtR = p.DowntimeRatio.HasValue ? $"{p.DowntimeRatio.Value:0%}" : "—";

            sb.Append($"▸ {p.PartName} | М/Л: {p.Order} | Уст.{p.Setup}");
            if (p.NoSetupHappened) sb.Append(" [наладки не было]");
            if (p.NoProductionHappened) sb.Append(" [изготовления не было]");
            sb.AppendLine();

            sb.AppendLine($"Наладка: план={p.SetupTimePlan:0}мин, факт={p.SetupTimeFact:0}мин, КПД={setupR}" +
                          (p.PartialSetup > 0 ? $", частичная={p.PartialSetup:0}мин" : ""));
            sb.AppendLine($"Изготовление: норматив={p.SingleProductionTimePlan:0.#}мин/дет, " +
                          $"выполнено={p.FinishedCount:0}шт, КПД={prodR}");

            if (p.MachiningTime > 0 || p.SingleProductionTimePlan > 0)
                sb.AppendLine($"Машинное время/дет: {p.MachiningTime:0.#}мин | Простои: {dtR}");

            var comments = new List<string>();
            if (!string.IsNullOrWhiteSpace(p.OperatorComment)) comments.Add($"оператор: «{p.OperatorComment.Trim()}»");
            if (p.NoManualOperatorComment && p.DowntimeRatio > 0.15) comments.Add("(оператор не написал ручного комментария)");
            if (!string.IsNullOrWhiteSpace(p.MasterSetupComment)) comments.Add($"причина наладки: «{p.MasterSetupComment.Trim()}»");
            if (!string.IsNullOrWhiteSpace(p.MasterMachiningComment)) comments.Add($"причина изгот.: «{p.MasterMachiningComment.Trim()}»");
            if (!string.IsNullOrWhiteSpace(p.MasterComment)) comments.Add($"мастер: «{p.MasterComment.Trim()}»");
            if (comments.Count > 0)
                sb.AppendLine($"  Комментарии: {string.Join("; ", comments)}");

            if (p.Signals is { Count: > 0 })
            {
                sb.AppendLine("  Сигналы системы (числовые факты):");
                foreach (var sig in p.Signals)
                    sb.AppendLine($"    ⚠ {sig}");
            }
            if (p.PartsHistory is { RecordsFound: > 0 } ph)
            {
                sb.AppendLine($"  История этой детали ({ph.RecordsFound} прошлых смен, от новых к старым):");
                foreach (var line in ph.Lines)
                {
                    // ⚠ если была необъяснённая проблема — выделяем, чтобы модель не пропустила
                    var flag = line.HasUnexplainedLowEfficiency ? " ⚠низкий КПД" : "";

                    var decision = line.AnalystDecision switch
                    {
                        "escalated" => "эскалация",
                        "ok" => "ок",
                        _ => "не проверено",
                    };

                    sb.Append($"    {line.ShiftDate}");
                    sb.Append($" | КПД изгот.={line.ProductionRatio}");
                    sb.Append($" | КПД нал.={line.SetupRatio}");
                    sb.Append($" | изгот.={line.FinishedCount}шт");
                    sb.Append($" | аналитик: {decision}{flag}");

                    if (!string.IsNullOrWhiteSpace(line.AnalystComment))
                        sb.Append($" — «{line.AnalystComment}»");

                    sb.AppendLine();

                    // AiExplanation показываем только для эскалированных смен —
                    // для "ок" без аномалий это лишний шум в контексте
                    if (!string.IsNullOrWhiteSpace(line.AiExplanation)
                        && line.AnalystDecision == "escalated")
                    {
                        sb.AppendLine($"      AI тогда: {line.AiExplanation}");
                    }
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("════════════════════════════════════════");

        if (hardRules.HardSignals.Count > 0)
        {
            sb.AppendLine("СИСТЕМА УЖЕ ОПРЕДЕЛИЛА: requires_review = true (правила ниже неотменяемы).");
            sb.AppendLine("Сработавшие жёсткие правила:");
            foreach (var r in hardRules.HardSignals)
                sb.AppendLine($" • {r}");
            sb.AppendLine("Поле requires_review в ответе ОБЯЗАНО быть true.");
            sb.AppendLine("Поле confidence ОБЯЗАНО быть в диапазоне 0.85-1.0.");
        }
        else
        {
            sb.AppendLine("Жёсткие неотменяемые правила НЕ сработали. Реши требуется ли эскалация,");
            sb.AppendLine("опираясь на 'ПРИЗНАКИ ДЛЯ ЭСКАЛАЦИИ' выше и контекст комментариев.");
        }

        if (hardRules.SoftSignals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Soft-сигналы для ЭТОГО конкретного дня (см. 'ОСОБО ПРО' выше):");
            sb.AppendLine("По умолчанию эскалируют, но ты МОЖЕШЬ понизить каждый из них в downgraded_signals,");
            sb.AppendLine("если для конкретной детали есть причинно-релевантное объяснение:");
            foreach (var s in hardRules.SoftSignals)
                sb.AppendLine($" • {s}");
        }

        sb.AppendLine();
        sb.AppendLine("Ответь СТРОГО в этом формате, без markdown, без преамбулы:");
        sb.AppendLine("""
        {
          "requires_review": true или false,
          "confidence": число от 0.0 до 1.0,
          "signals": ["краткие описания необъяснённых проблем, если есть"],
          "downgraded_signals": ["soft-сигналы из списка выше, которые ты решил НЕ эскалировать, с явным релевантным объяснением — если таких нет, пустой массив"],
          "explanation": "1-2 предложения, объясняющие решение, при указании аномалий - указывай их — ОБЯЗАТЕЛЬНОЕ непустое поле",
          "suggested_reason": "краткая причина в 3-7 слов — ОБЯЗАТЕЛЬНОЕ непустое поле"
        }
        """);

        return sb.ToString();
    }
}