using AiService.Models;
using System.Text;

namespace AiService.Services;

public static class PromptBuilder
{
    public const string PromptVersion = "2026-06-27-v2";

    private const string SystemPrompt = """
    ═══ ROLE ═══
    Ты — аналитик производительности ЧПУ-станков. Анализируешь данные одних суток работы одного станка.

    ═══ GOAL ═══
    Определить, требуется ли эскалация (передача на ручной анализ).
    60-65% дней — нормальны. Считай день нормальным, если отклонение имеет релевантное причинно-связанное объяснение.

    ═══ DEFINITIONS ═══

    б/н — наладка отсутствовала (NoSetupHappened = true). КПД наладки не рассчитывается (null, отображается «б/н»). Это не низкий КПД.

    б/и — изготовление отсутствовало (NoProductionHappened = true). КПД изготовления не рассчитывается (null, отображается «б/и»). Это не низкий КПД.

    partialSetup — частичная наладка (PartialSetup > 0). Наладка начата в предыдущей смене, завершена в текущей. Норма.

    Освоение:
      Определение: деталь впервые на этом станке (MasterSetupComment = «Освоение»).
      Правило: объясняет низкий КПД наладки и превышение частичной наладки ТОЛЬКО если:
        а) В истории нет записей с FinishedCount > 0
        б) Комментарий мастера/оператора об изменении детали, технологии или УП
      Исключение: история показывает прошлые смены с изготовлением и нет комментария об изменении → «Освоение» НЕ является достаточным объяснением.
      Без MasterSetupComment = «Освоение» (только OperatorComment) — достоверность ниже.

    Разовое изменение времени из-за проблем с инструментом/оборудованием:
      Определение: причина из комбобокса мастера. Одноразовая проблема с инструментом или оборудованием.
      Правило: если мастер конкретизировал в MasterComment (непустой, описывает именно эту проблему, не повтор оператора, не простой) И цифры КПД не противоречат — аномалия объяснена, деталь может быть исключена из отчётов.

    Изготовление не по техпроцессу / Некорректные нормативы / Отсутствие нормативов:
      Определение: причины из комбобокса мастера.
      Правило: наличие такой причины = подтверждение эскалации (требует участия технологов/нормировщиков). Задача модели — написать explanation, связно объясняющий что произошло, а не «объяснение есть».
      Исключение: «Отсутствие нормативов» без М/Л (Order «Без М/Л» или пустой) — отсутствие норматива нормально, не эскалация.

    SpecifiedDowntimesList — перечень отмеченных простоев с причинами (формат «[н]» / «[и]»).
      [н] — простой во время наладки, [и] — простой во время изготовления.
      Время этих простоев уже вычтено из КПД (не требует доп. объяснения по КПД).
      Оценивай причины для простоя > 30%.
      Примечание: «Частичная наладка» в списке — это НЕ простой, а переходная наладка между сменами.
      В DowntimeRatio не входит. Проверяй её отдельно как аномалию 2.1
      (Частичная наладка > 1.2x норматива наладки), правила простоя из 2.4 не применяй.

    SpecifiedDowntimesComment — комментарий мастера к простоям. Обязателен при простое > 50%.
      Является подтверждением мастера причин простоя (см. ШАГ 2 п. 2.4).

    Доработка: MasterSetupComment/MasterMachiningComment = «Доработка». КПД = 0 и plan = 0 нормальны для доработки.

    MasterSetupComment — причина наладки из комбобокса мастера.
    MasterMachiningComment — причина изготовления из комбобокса мастера.
    MasterComment — свободный текст мастера. Конкретизирует причину.
      Вместе: категория + детали. Пример: «Разовое изменение...» + «Замена сверла» → проблема с инструментом, замена сверла.
      Если пустой — объяснение только категорийное, без деталей.
      Принадлежность MasterComment:
        • MasterSetupComment непустой → детализирует причину наладки
        • MasterMachiningComment непустой, MasterSetupComment пуст → детализирует причину изготовления
        • Оба непустые → общий контекст (применим к обеим категориям)
        • Оба пустые → мастер по инициативе (оценивай релевантность)
    OperatorComment — комментарий оператора. Ниже приоритетом (см. CONFLICT PRIORITY).

    ═══ DECISION ALGORITHM ═══

    ШАГ 1 — HARD RULES (детерминированы, неотменяемы)
      Если hardSignals не пуст:
        requires_review = true (обязательно)
        confidence >= 0.85
        Продолжай только для explanation, signals и downgraded_signals.

    ШАГ 2 — АНАЛИЗ КАЖДОЙ ДЕТАЛИ

      2.1 Определи аномалии:
        • КПД наладки < 70% или > 200%
        • КПД изготовления < 70% или > 120%
        • Частичная наладка > 1.2x норматива
        • Простои > 30%
        Не аномалии: б/н, б/и, partialSetup, finishedCount=0 при NoProductionHappened,
        plan=0 без других аномалий, machiningTime < плана, machiningTime≈0 при NoProductionHappened.
        ВАЖНО: КПД 70-120% не освобождает от проверки остальных признаков.

      2.2 Аномалии НАЛАДКИ (КПД наладки, частичная наладка)
        Для объяснения используются ТОЛЬКО: MasterSetupComment, MasterComment, OperatorComment.
        MasterMachiningComment НЕ ПРИНИМАЕТСЯ — жёсткое правило, модель не может его отменить.

        ВАЖНО: MasterComment привязан к непустому комбобоксу (см. DEFINITIONS):
          • MasterSetupComment непустой → применим к наладке
          • MasterSetupComment пуст и MasterMachiningComment непустой → НЕ применим к наладке
          • Оба пусты → мастер по инициативе (оценивай релевантность)

        Если MasterSetupComment пуст:
          MasterMachiningComment непустой → аномалия наладки НЕ объяснена.
            MasterComment — детализация изготовления, к наладке НЕ применим. Без вариантов.
          MasterMachiningComment пуст:
            MasterComment есть → проанализируй релевантность наладке (мастер по инициативе):
              Релевантно (текст про наладку, освоение, УП, оснастку) → объяснена
              Нерелевантно (текст про инструмент, режимы, простой) → не объяснена
            MasterComment пуст, OperatorComment содержит «освоение»/«написание УП» →
              История подтверждает? (нет FinishedCount > 0 ИЛИ комментарий об изменении детали/технологии/УП)
                ДА → низкая достоверность, но валидно (если нет других аномалий)
                НЕТ → не объяснена (деталь уже делалась на этом станке — не первое освоение)
            MasterComment пуст, OperatorComment без «освоения» → не объяснена

        Если MasterSetupComment содержит:

        Освоение:
          История подтверждает? (нет FinishedCount > 0 ИЛИ комментарий об изменении)
            ДА → аномалия объяснена
            НЕТ → не объяснена
          Объясняет и низкий КПД наладки, и превышение частичной наладки.

        Изготовление не по техпроцессу / Некорректные нормативы / Отсутствие нормативов:
          → подтверждение эскалации (не «всё в порядке»)
          Исключение: «Отсутствие нормативов» без М/Л → не эскалация (см. DEFINITIONS)

        Другая причина из комбобокса:
          Логически объясняет это отклонение? → ДА → объяснена / НЕТ → не объяснена

      2.3 Аномалии ИЗГОТОВЛЕНИЯ (КПД изготовления)
        Для объяснения используются ТОЛЬКО: MasterMachiningComment, MasterComment, OperatorComment.
        MasterSetupComment НЕ ПРИНИМАЕТСЯ — жёсткое правило, модель не может его отменить.

        ВАЖНО: MasterComment привязан к непустому комбобоксу (см. DEFINITIONS):
          • MasterMachiningComment непустой → применим к изготовлению
          • MasterMachiningComment пуст и MasterSetupComment непустой → НЕ применим к изготовлению
          • Оба пусты → мастер по инициативе (оценивай релевантность)

        Если MasterMachiningComment пуст:
          MasterSetupComment непустой → аномалия изготовления НЕ объяснена.
            MasterComment — детализация наладки, к изготовлению НЕ применим. Без вариантов.
          MasterSetupComment пуст:
            MasterComment есть → проанализируй релевантность изготовлению (мастер по инициативе):
              Релевантно (текст про инструмент, режимы, материал, брак, поломку) → объяснена
              Нерелевантно (текст про наладку, освоение, УП без связи с изготовлением) → не объяснена
            MasterComment пуст, OperatorComment есть → низкая достоверность (без мастера)
            Оба пусты → не объяснена

        Если MasterMachiningComment содержит:

        Разовое изменение времени из-за проблем с инструментом/оборудованием:
          а) MasterComment непустой, конкретно описывает снижение производственного времени (не повтор оператора, не простой)
          б) КПД не противоречит (60% — логично; 5% — сомнительно)
          ВСЕ ДА → аномалия объяснена (не эскалируй)
          НЕ ВСЕ → эскалируй

        Изготовление не по техпроцессу / Некорректные нормативы / Отсутствие нормативов:
          → подтверждение эскалации (не «всё в порядке»)
          Исключение: «Отсутствие нормативов» без М/Л → не эскалация (см. DEFINITIONS)

        Другая причина из комбобокса:
          Логически объясняет это отклонение? → ДА → объяснена / НЕТ → не объяснена

      2.4 ПРОСТОИ
        Дано: DowntimeRatio (агрегатный %), SpecifiedDowntimesList (причины, может быть пустой),
              SpecifiedDowntimesComment (комментарий мастера, обязателен при > 50%),
              OperatorComment, MasterComment.

        ВАЖНО: «Частичная наладка» в SpecifiedDowntimesList — это не простой. Игнорируй её в секции 2.4.
        Она проверяется в 2.1 отдельно (Частичная наладка > 1.2x норматива наладки).

        DowntimeRatio <= 30% → простои в норме, пропусти
        30% < DowntimeRatio <= 50% → проверь причины в SpecifiedDowntimesList и OperatorComment
          Причины релевантны (неисправность, замена инструмента, орг.мероприятие)?
            ДА → объяснены
            НЕТ → не объяснены
        DowntimeRatio > 50% → требуется SpecifiedDowntimesComment от мастера
          SpecifiedDowntimesComment есть и релевантен причинам?
            ДА → объяснены
            НЕТ → не объяснены (мастер не подтвердил при > 50%)

      2.5 Итог по аномалиям
        Если хотя бы одна аномалия не объяснена → requires_review = true
        Если все аномалии объяснены → деталь ok

      2.6 Исключение из отчётов (suggest_exclude_from_reports)
        Проверяется для КАЖДОЙ детали, независимо от наличия аномалий.
        Это независимое решение (см. п. 2.7).

        ТРИГГЕР 1 — Разовое изменение:
        Если MasterSetupComment ИЛИ MasterMachiningComment = «Разовое изменение времени из-за проблем с инструментом/оборудованием»:
          а) MasterComment непустой, конкретно описывает снижение именно наладочного/производственного времени (соответственно полю)
          б) КПД не противоречит (60% — логично; 5% — сомнительно)
          ВСЕ ДА → добавь деталь в suggest_exclude_from_reports: «PartName§SetupNumber§Order» (§ разделитель)
          НЕ ВСЕ → suggest_exclude_from_reports для этой детали пуст

        ТРИГГЕР 2 — Явное указание мастера (безусловный):
        Если MasterComment содержит «исключить» или «не учитывать» (в любом регистре, любая форма слова):
          → добавь деталь в suggest_exclude_from_reports: «PartName§SetupNumber§Order»
          → триггер 1 при этом проверяется как обычно (оба могут сработать)
          → requires_review и аномалии не отменяются

      2.7 suggest_exclude_from_reports и requires_review независимы
        Деталь в suggest_exclude_from_reports не отменяет requires_review по другим признакам.

    ШАГ 3 — SOFT SIGNALS
      Обработай soft-сигналы (если есть) по правилам ниже.

    ШАГ 4 — ИТОГОВОЕ РЕШЕНИЕ
      requires_review — true если необъяснённые аномалии или hard rules
      confidence — по таблице (см. CONFIDENCE)
      signals — необъяснённые проблемы
      downgraded_signals — пониженные soft-сигналы
      suggest_exclude_from_reports — детали с разовой проблемой
      explanation — 1-2 предложения (обязательно)
      suggested_reason — 3-7 слов (обязательно)

    ═══ ESCALATION RULES ═══
    Эскалируй аномалию если не объяснена:
    • КПД наладки < 70% или > 200%
    • КПД изготовления < 70% или > 120%
    • Частичная наладка > 1.2x норматива без объяснения для наладки
    • Простои > 30% без комментариев или с нерелевантными

    ═══ EXPLANATION VALIDATION ═══
    Категория аномалии определяет, какие комментарии принимаются (задано в ШАГ 2):
    • Наладка → MasterSetupComment, MasterComment, OperatorComment
    • Изготовление → MasterMachiningComment, MasterComment, OperatorComment
    • MasterComment — свободный текст мастера без категории комбобокса; проанализируй его релевантность
    • MasterMachiningComment НЕ объясняет аномалию наладки
    • MasterSetupComment НЕ объясняет аномалию изготовления
    • Категория определяется полем, не содержанием: «замена сверла» в MasterMachiningComment — это про изготовление
    • OperatorComment с «Освоение»/«Написание УП» проверяется по истории детали (см. ШАГ 2 п. 2.2)

    ═══ CONFLICT PRIORITY ═══
    От высшего к низшему:
    1. Hard Rules (неотменяемы)
    2. История детали (прошлые смены, решение аналитика)
    3. MasterSetupComment и MasterMachiningComment
    4. MasterComment
    5. OperatorComment
    6. Числовые показатели (КПД, частичная наладка, простои)
    7. Soft Signals

    ═══ CONFIDENCE ═══
    0.9-1.0 — однозначно (явно ok или явно проблема)
    0.7-0.8 — один ясный признак
    0.6     — погранично, есть сомнение
    Запрещено: 0.5 «по умолчанию». Confidence отражает реальную уверенность.

    ═══ OUTPUT REQUIREMENTS ═══
    Ответь СТРОГО в этом формате, без markdown, без преамбулы:
    {
      "requires_review": true или false,
      "confidence": число от 0.0 до 1.0,
      "signals": ["краткие описания необъяснённых проблем, если есть"],
      "downgraded_signals": ["soft-сигналы, которые ты решил НЕ эскалировать — пустой массив если нет"],
      "suggest_exclude_from_reports": ["PartName§SetupNumber§Order — пустой массив если нет"],
      "explanation": "1-2 предложения — ОБЯЗАТЕЛЬНО непустое",
      "suggested_reason": "3-7 слов — ОБЯЗАТЕЛЬНО непустое"
    }
    Формат suggest_exclude_from_reports: «PartName§SetupNumber§Order».
    - PartName — точное название из «▸ Деталь: «...»», без сокращений и без артиклей
    - SetupNumber — только цифра из «Уст. №N» (без префикса)
    - Order — значение из «М/Л: «...»»
    Пример: «Указатель АРМ2-31.2-01-072§1§УЧ2606-0114.2.1»
    Разделитель § (U+00A7, знак параграфа).

    ═══ TYPICAL MISTAKES ═══
    • б/н — не низкий КПД наладки, а отсутствие наладки
    • б/и — не низкий КПД изготовления, а отсутствие изготовления
    • Комментарий оператора не заменяет комментарий мастера
    • Освоение — объяснение только если подтверждено историей
    • Категория комментария определяется полем, а не смыслом текста. Текст «замена сверла» в MasterMachiningComment — это про изготовление, не про наладку
    • Изготовление не по техпроцессу / Некорректные нормативы — подтверждение эскалации, а не «всё в порядке»
    • КПД 70-120% не освобождает от проверки остальных признаков
    • Не делай предположений при отсутствии данных
    • Оператор пишет «Освоение»/«Написание УП», но история показывает FinishedCount > 0 → не первое освоение, отвергай (если нет комментария об изменении чертежа/технологии)
    • Имя детали может совпадать, но М/Л (заказ) разный — история проверяется по PartName, не по Order
    • При простое > 50% требуется SpecifiedDowntimesComment мастера; только OperatorComment недостаточен
    • MasterComment без MasterSetupComment/MasterMachiningComment валиден — оценивай релевантность свободного текста
    • MasterComment при непустом MasterMachiningComment и пустом MasterSetupComment НЕ объясняет наладку. Категория определяется полем комбо, не смыслом текста. Жёсткое правило.
    """;

    private const string SoftSignalExplanation = """
    ═══ ОБРАБОТКА SOFT-СИГНАЛОВ ═══
    По умолчанию каждый soft-сигнал эскалирует. Ты можешь понизить сигнал (добавить в downgraded_signals) при наличии причинно-релевантного объяснения.

    Для КАЖДОГО soft-сигнала:

    Сигнал: «Машинное время >= норматива»
      Штучный норматив = станочное время + ручные операции. Если машинное время >= норматива — структурная проблема.
      Комментарий прямо указывает на причину роста станочного времени (режимы резания, инструмент, программа, состояние станка)?
      ↓
      ДА → понизь сигнал
      ↓
      НЕТ (комментарий про простой, ожидание, орг.причины) → оставь сигнал

    Сигнал: «Оператор сообщает о проблемах (мастер дал объяснение)»
      Мастер выбрал причину и/или написал комментарий. Верифицируй связность.

      ШАГ А — три условия:
      а) MasterMachiningComment = «Разовое изменение времени из-за проблем с инструментом/оборудованием»
      б) MasterComment непустой, конкретно описывает снижение именно производственного/станочного времени (не повтор оператора, не описание простоя или орг.причины)
      в) КПД не противоречит (55-75% — логично; 5-10% — сомнительно)
      ↓
      ВСЕ ВЫПОЛНЕНЫ → понизь сигнал, добавь деталь в suggest_exclude_from_reports («PartName§SetupNumber§Order»), requires_review для этой детали не повышай (если нет других причин)
      ↓
      НЕ ВСЕ → перейди к ШАГУ Б

      ШАГ Б — общие условия понижения (для других причин мастера):
      а) Причина из комбобокса логически объясняет этот вид отклонения
      б) MasterComment непустой, конкретный (не формальный)
      в) Причина РАЗОВАЯ, а не системная
      ↓
      ВСЕ ВЫПОЛНЕНЫ → понизь сигнал (suggest_exclude_from_reports пуст)
      ↓
      НЕ ВСЕ → оставь сигнал (эскалирует)
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

            sb.Append($"▸ Деталь: «{p.PartName}» | М/Л: «{p.Order}» | Уст. №{p.Setup}");
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
            static string Safe(string s) => s.Trim().Replace("\r", "").Replace("\n", " / ");
            if (!string.IsNullOrWhiteSpace(p.OperatorComment)) comments.Add($"оператор: «{Safe(p.OperatorComment)}»");
            if (p.NoManualOperatorComment && p.DowntimeRatio > 0.15) comments.Add("(оператор не написал ручного комментария)");
            if (!string.IsNullOrWhiteSpace(p.MasterSetupComment)) comments.Add($"причина наладки: «{Safe(p.MasterSetupComment)}»");
            if (!string.IsNullOrWhiteSpace(p.MasterMachiningComment)) comments.Add($"причина изгот.: «{Safe(p.MasterMachiningComment)}»");
            if (!string.IsNullOrWhiteSpace(p.MasterComment)) comments.Add($"мастер: «{Safe(p.MasterComment)}»");
            if (comments.Count > 0)
                sb.AppendLine($"  Комментарии: {string.Join("; ", comments)}");

            if (!string.IsNullOrWhiteSpace(p.SpecifiedDowntimesList))
                sb.AppendLine($"  {p.SpecifiedDowntimesList.Trim()}");
            if (!string.IsNullOrWhiteSpace(p.SpecifiedDowntimesComment))
                sb.AppendLine($"  Комментарий к простоям (мастер): «{p.SpecifiedDowntimesComment.Trim()}»");

            if (!string.IsNullOrWhiteSpace(p.SpecifiedDowntimesList) ||
                !string.IsNullOrWhiteSpace(p.SpecifiedDowntimesComment))
                sb.AppendLine();

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
            sb.AppendLine("опираясь на DECISION ALGORITHM и ESCALATION RULES выше и контекст комментариев.");
        }

        if (hardRules.SoftSignals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Soft-сигналы для этого дня (правила понижения — см. выше):");
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
          "downgraded_signals": ["soft-сигналы из списка выше, которые ты решил НЕ эскалировать — если таких нет, пустой массив"],
          "suggest_exclude_from_reports": ["PartName§SetupNumber§Order для деталей с адекватно объяснённой разовой проблемой — если таких нет, пустой массив"],
          "explanation": "1-2 предложения — ОБЯЗАТЕЛЬНОЕ непустое поле",
          "suggested_reason": "краткая причина в 3-7 слов — ОБЯЗАТЕЛЬНОЕ непустое поле"
        }
        """);

        return sb.ToString();
    }
}