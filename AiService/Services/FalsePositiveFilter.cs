using AiService.Models;
using System.Text.RegularExpressions;

namespace AiService.Services;

public static class FalsePositiveFilter
{
    private const string SetupMarker = "наладк";
    private const string ProductionMarker = "изготов";

    private static readonly string[] SetupKeywords =
    [
        "б/н",
        "отсутств",
        "не выполнен",
        // Формы «выполнена/выполнено/выполнены» отдельно: «не выполнен» их не покрывает,
        // а именно так модель формулирует запрещённую категорию «наладка не выполнена
        // при наличии заказа» (A/B, Integrex i200 2026-09-21). Позитивное «выполнено N шт»
        // сюда не входит — отсева по нему нет (см. тесты).
        "не выполнена",
        "не выполнено",
        "не выполнены",
        "не проведен",
        "не была",
        "не было",
        "нулев",
        // Расплывчатое «вне нормы» без чисел (пилот 24.09: «КПД наладки вне нормы»
        // про б/н-деталь). Числовые претензии с реальными значениями не задевает:
        // их ловит IsNormBandKpdClaim только внутри диапазона, а тут маркера нет.
        "вне нормы",
        "0 мин",
        "0мин",
        "наладка без",
        "наладка не",
        "наладки не",
        // Перефразировки «факта нет» (кейс QTS350 2026-09-24 v14:
        // «план наладки=86мин при наличии заказа, но факта нет»).
        "факта нет",
        "факт 0",
        "без факта",
    ];

    private static readonly string[] ProductionKeywords =
    [
        "б/и",
        "отсутств",
        "не выполнен",
        "не проведен",
        "не было",
        "нулев",
        "изготовление без",
        "изготовление не",
        "изготовления не",
        "факта нет",
        "факт 0",
        "без факта",
    ];

    // «КПД [частичной] наладки 74%» / «КПД изготовления 87%» / «Аномалия наладки 200%» —
    // первое число с % после упоминания категории. Модель стабильно ошибается в
    // арифметике порогов (в прогонах называла аномалией 72%, 74%, 83%, 197%, 200%),
    // поэтому значения в диапазоне нормы отсеиваются детерминированно.
    private static readonly Regex KpdClaim = new(
        @"(?:кпд|аномал\w*)\s+(?:кпд\s+)?(?<partial>частичн\w*\s+)?(?<cat>наладк|изготовлен)\w*[^0-9%]{0,40}?(?<val>\d+(?:[.,]\d+)?)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // «\bК1\b» — эхо наших же exclude-подсказок про влияние разовой причины на К1
    // (границы слова, чтобы не задеть обозначения вроде «К13» в названиях деталей).
    private static readonly Regex K1Mention = new(
        @"\bк1\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ZeroPercent = new(
        @"\b0\s*%",
        RegexOptions.Compiled);

    private static readonly Regex DowntimeClaim = new(
        @"просто\w*[^0-9%]{0,40}?(?<val>\d+(?:[.,]\d+)?)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Страж противоречия (зеркало «пустого вердикта» в контроллере): модель заявила
    /// необъяснённые проблемы (signals непуст), но requires_review=false. По определению
    /// формата signals — это необъяснённые аномалии, вердикт обязан быть true;
    /// иначе — пропущенная эскалация (пилот 24.09.2026, Goodway 01.06: 67% + пустая
    /// детализация при «Разовом изменении» с вердиктом False). Fail-safe в сторону
    /// эскалации, как HasError: лучше лишний разбор, чем пропуск.
    /// Вызывается в контроллере ПОСЛЕ Apply (по почищенным сигналам), в обоих эндпоинтах.
    /// </summary>
    public static bool IsIncoherentOk(bool requiresReview, List<string> filteredSignals) =>
        !requiresReview && filteredSignals.Count > 0;

    /// <summary>
    /// Удаляет из ответа LLM сигналы-галлюцинации, которые модель стабильно
    /// генерирует вопреки промпту:
    ///  1. «отсутствие наладки/изготовления» для частей, помеченных б/н / б/и —
    ///     если сигнал явно называет деталь, сверяется статус именно этой детали,
    ///     а не дня целиком: иначе одна б/н-деталь гасила бы сигналы про остальные;
    ///  2. аномалии частичной наладки без СИСТЕМНОГО сигнала — модель не должна
    ///     вычислять её сама; при системном сигнале решение остаётся за моделью:
    ///     она проверяет релевантность объяснения МАСТЕРА (официальная позиция
    ///     по регламенту; слова оператора аномалию не закрывают);
    ///  3. «низкий/высокий КПД» со значением в диапазоне нормы
    ///     (наладка 70–200%, изготовление 70–120%, границы включительно);
    ///  4. жалобы на простои ≤50% — у простоев нет порога аномальности, а ниже
    ///     50% система даже не требует комментария мастера, проверять нечего;
    ///     выше 50% релевантность комментария мастера оценивает модель;
    ///  7. «план=0 / отсутствие нормативов» при заказе «Без М/Л» — «Без М/Л» и есть
    ///     единственное разрешённое исключение, нормативов там быть не должно;
    ///     сюда же «КПД 0%» для деталей «Без М/Л» без норматива — при plan=0
    ///     КПД не вычислим, это артефакт, а не аномалия (прогон 21v1: id 43, 57);
    ///  8. «Штучная/длительная работа не подтверждена порогами» — пороги считает
    ///     C# (IsSmallBatch), модели пересчитывать их запрещено; отсев только
    ///     когда ВСЕ детали с этой причиной действительно штучные (21v1: id 417,
    ///     454 — модель галлюцинировала «MachiningTime не указано», 506);
    ///  9. эхо exclude-подсказки «…может повлиять на К1» при разовой причине
    ///     (освоение, несоответствующие заготовки, «разовое …») — это основание
    ///     для исключения из отчёта, а не для эскалации (21v1: id 307, 479);
    /// 10. «освоение без конкретики / не подтверждено историей / требует
    ///     уточнения», когда освоение подтверждено детерминированно
    ///     (<see cref="MasteringAutoApprover.IsConfirmedMastering"/>) — модель
    ///     требует то, чего регламент не требует; формулировки про найденное
    ///     опровержение («уже выполнялась», «противоречит») не трогаются;
    /// 11. «plan=0 / отсутствует норматив» по строке без заказа (Order пустой
    ///     или «Без М/Л») при фактически нулевом плане — норма (DEFINITIONS
    ///     «Норматив отсутствует», зеркало hasOrder из HardRuleEvaluator):
    ///     модель сомневается в таких строках (кейс 21.09.2026 Rontek HTC420).
    ///     Строки с заказом не трогаем — там plan=0 эскалируется
    ///     детерминированным hard (недоработка нормирования).
    /// Пункты 5/6 (исключения для plan=0 при б/н/б/и/частичной наладке) сняты —
    /// норматив привязан к заказу, а не к факту работы, недоработка нормирования
    /// эскалируется всегда. Симптом лечился здесь; корень — remeLog Part.cs
    /// this[columnName] теперь требует объяснения мастера при plan=0 с заказом
    /// независимо от б/н/б/и, так что необъяснённых plan=0-строк в данных быть
    /// не должно (см. память ai-analysis-improvement-plan, 2026-07-21).
    /// Если после фильтрации не осталось ни одного основания для эскалации
    /// (hard rules, soft, клиентские сигналы), флаг <c>ShouldReset</c>
    /// устанавливается — вызывающий код сбрасывает requiresReview.
    /// </summary>
    public static (List<string> FilteredSignals, bool ShouldReset, List<(string Signal, string Reason)> Removed) Apply(
        AnalyzeRequest request,
        HardRuleResult hardRules,
        List<string> notDowngraded,
        List<string> llmSignals)
    {
        var hasNoSetupPart = request.Parts.Any(IsNoSetup);
        var hasNoProductionPart = request.Parts.Any(IsNoProduction);

        var hasClientPartialSignal = request.Parts.Any(p => p.Signals.Any(s =>
            s.Contains("частичн", StringComparison.OrdinalIgnoreCase)
            && s.Contains("наладк", StringComparison.OrdinalIgnoreCase)));

        var hasBezMlPart = request.Parts.Any(p => IsBezMl(p.Order));

        // Если хоть одна НЕштучная деталь объяснена «Штучной/длительной работой»,
        // претензия модели к порогам может относиться к ней — сигнал не трогаем.
        var hasNonSmallBatchShtuchnaya = request.Parts.Any(p =>
            !p.IsSmallBatch
            && (p.MasterSetupComment.Contains("штучн", StringComparison.OrdinalIgnoreCase)
                || p.MasterMachiningComment.Contains("штучн", StringComparison.OrdinalIgnoreCase)));

        var hasConfirmedMastering = request.Parts.Any(MasteringAutoApprover.IsConfirmedMastering);

        var filtered = new List<string>(llmSignals.Count);
        var removed = new List<(string Signal, string Reason)>();

        foreach (var signal in llmSignals)
        {
            var reason =
                hasNoSetupPart && IsNoSetupHallucination(signal, request.Parts) ? "б/н-галлюцинация" :
                hasNoProductionPart && IsNoProductionHallucination(signal, request.Parts) ? "б/и-галлюцинация" :
                !hasClientPartialSignal && IsPartialSetupClaim(signal) ? "частичная наладка без системного сигнала" :
                IsNormBandKpdClaim(signal) ? "КПД в диапазоне нормы" :
                IsTolerableDowntimeClaim(signal) ? "простой ≤50%" :
                hasBezMlPart && IsBezMlNormativeClaim(signal) ? "норматив при заказе «Без М/Л»" :
                IsZeroKpdOnNoNormBezMl(signal, request.Parts) ? "КПД 0% при отсутствии норматива («Без М/Л»)" :
                !hasNonSmallBatchShtuchnaya && IsSmallBatchThresholdClaim(signal) ? "пороги штучной партии пересчитаны моделью" :
                IsK1ExcludeEcho(signal) ? "эхо exclude-подсказки про К1" :
                IsNonSerialProductionKpdClaim(signal, request) ? "КПД изготовления не оценивается (несерийный станок)" :
                IsRefutedDrugoeClaim(signal, request.Parts) ? "«Другое» с детализацией — объяснено" :
                IsMisaddressedReasonDemand(signal, request.Parts) ? "требование подтверждения невыбранной причины" :
                hasConfirmedMastering && IsUnconfirmedMasteringClaim(signal) ? "освоение подтверждено детерминированно" :
                IsNoOrderPlan0Claim(signal, request.Parts) ? "plan=0 без заказа — норма" :
                (string?)null;

            if (reason != null)
                removed.Add((signal, reason));
            else
                filtered.Add(signal);
        }

        if (removed.Count == 0)
            return (llmSignals, false, removed);

        if (filtered.Count > 0)
            return (filtered, false, removed);

        if (hardRules.MustEscalate)
            return (filtered, false, removed);

        if (notDowngraded.Count > 0)
            return (filtered, false, removed);

        if (request.Signals.Count > 0)
            return (filtered, false, removed);

        if (request.Parts.Any(p => p.Signals.Count > 0))
            return (filtered, false, removed);

        return (filtered, true, removed);
    }

    private static bool IsNoSetup(PartContext p) =>
        p.NoSetupHappened
        || (p.SetupRatio == null && p.SetupTimeFact <= 0);

    private static bool IsNoProduction(PartContext p) =>
        p.NoProductionHappened
        || (p.ProductionRatio == null && p.FinishedCount <= 0);

    /// <summary>
    /// Сигнал о «наладки не было» — халлюцинация, когда ни одна деталь в сигнале
    /// не названа явно (день-уровневый сигнал, старое поведение) либо когда
    /// привязка двусмысленна и неверна при любом прочтении: среди названных есть
    /// б/н-строка (про неё претензия запрещена — такой категории нет), а остальные
    /// имеют факт > 0 (про них претензия фактически ложна). Кейс QTS350 2026-09-24:
    /// «Отсутствие наладки при наличии заказа (Кольцо…, уст 1)» матчит ДВЕ строки —
    /// б/н с планом 86 и нормальную с фактом 70; чистый All(IsNoSetup) из-за второй
    /// не срабатывал. Однозначная ссылка на строку с фактом НЕ трогается
    /// (консервативно: «не выполнена» может значить «не завершена»).
    /// Раньше проверка была день-уровневой (hasNoSetupPart без привязки
    /// к конкретной детали) — если сутки содержали хотя бы одну б/н-деталь, сигнал
    /// про СОВСЕМ ДРУГУЮ деталь с реальной наладкой тоже гасился
    /// (кейс 2026-06-30 Rontek HTC650M: «кулачки» наладились 56 мин при плане 0, но два
    /// других изделия того же дня были б/н — сигнал про «кулачки» ошибочно съеден).
    /// </summary>
    private static bool IsNoSetupHallucination(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains(SetupMarker) || !SetupKeywords.Any(kw => lower.Contains(kw)))
            return false;

        var named = NamedParts(lower, parts);
        return named.Count == 0
            || (named.Any(IsNoSetup) && named.All(p => IsNoSetup(p) || p.SetupTimeFact > 0));
    }

    private static bool IsNoProductionHallucination(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains(ProductionMarker) || !ProductionKeywords.Any(kw => lower.Contains(kw)))
            return false;

        var named = NamedParts(lower, parts);
        return named.Count == 0
            || (named.Any(IsNoProduction)
                && named.All(p => IsNoProduction(p) || p.ProductionTimeFact > 0 || p.FinishedCount > 0));
    }

    /// <summary>
    /// Детали, чьё имя явно упомянуто в (уже lower-cased) тексте сигнала.
    /// Короткое имя внутри длинного («Кольцо АРМ2-49.3-01-041» внутри
    /// «Кольцо АРМ2-49.3-01-041 расточка кулачков») — артефакт подстроки, а не
    /// второе упоминание: убираем его, только если вне длинных вхождений его
    /// в сигнале нет. Иначе generic-совпадение тянет за собой чужие строки и
    /// ломает проверки All() (кейс QTS350 2026-09-24: «plan=0 при наличии
    /// заказа» для Без М/Л-строки не срезался из-за строк с заказом).
    /// </summary>
    private static List<PartContext> NamedParts(string lowerSignal, List<PartContext> parts)
    {
        var matched = parts.Where(p => p.PartName.Trim().Length > 0
            && lowerSignal.Contains(p.PartName.Trim().ToLowerInvariant())).ToList();
        if (matched.Count < 2) return matched;
        var names = matched.Select(p => p.PartName.Trim().ToLowerInvariant()).ToList();
        return matched.Where((p, i) =>
        {
            var name = names[i];
            foreach (var other in names)
            {
                if (other.Length <= name.Length || !other.Contains(name)) continue;
                // Короткое имя встречается только внутри длинного — не упоминание.
                if (!lowerSignal.Replace(other, "").Contains(name)) return false;
            }
            return true;
        }).ToList();
    }

    private static bool IsPartialSetupClaim(string signal)
    {
        var lower = signal.ToLowerInvariant();
        // Сигналы о простоях могут упоминать частичную наладку в пояснении —
        // их судьбу решает проверка простоев, а не эта.
        return lower.Contains("частичн") && lower.Contains("наладк")
            && !lower.Contains("просто");
    }

    /// <summary> Все упомянутые в сигнале значения КПД лежат в диапазоне нормы. </summary>
    private static bool IsNormBandKpdClaim(string signal)
    {
        var matches = KpdClaim.Matches(signal);
        if (matches.Count == 0) return false;

        foreach (Match m in matches)
        {
            if (!TryParsePercent(m.Groups["val"].Value, out var val)) return false;
            var isSetup = m.Groups["cat"].Value.StartsWith("наладк", StringComparison.OrdinalIgnoreCase)
                || m.Groups["partial"].Success;
            var upper = isSetup ? 200 : 120;
            if (val < 70 || val > upper) return false;
        }
        return true;
    }

    /// <summary>
    /// Все упомянутые в сигнале проценты простоев ≤50% — заведомо ложный сигнал:
    /// порога аномальности у простоев нет, а комментарий мастера система требует
    /// только при >50% (валидация в remeLog). Выше 50% решение остаётся за моделью
    /// (релевантность комментария мастера).
    /// </summary>
    private static bool IsTolerableDowntimeClaim(string signal)
    {
        var matches = DowntimeClaim.Matches(signal);
        if (matches.Count == 0) return false;

        foreach (Match m in matches)
        {
            if (!TryParsePercent(m.Groups["val"].Value, out var val)) return false;
            if (val > 50) return false;
        }
        return true;
    }

    private static bool IsBezMl(string order) =>
        order.Trim().Equals("Без М/Л", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// «План=0 / отсутствие нормативов» с явной привязкой к «Без М/Л» — модель
    /// применяет правило «недоработка нормирования» к единственному случаю,
    /// который из него исключён.
    /// </summary>
    private static bool IsBezMlNormativeClaim(string signal)
    {
        var lower = signal.ToLowerInvariant();
        return lower.Contains("без м/л")
            && (lower.Contains("норматив") || lower.Contains("нормирован"));
    }

    /// <summary>
    /// «КПД … 0%» для детали с заказом «Без М/Л» без соответствующего норматива:
    /// при plan=0 КПД не вычислим, нулевое значение — артефакт данных.
    /// </summary>
    private static bool IsZeroKpdOnNoNormBezMl(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains("кпд") || !ZeroPercent.IsMatch(lower)) return false;

        foreach (var p in parts)
        {
            if (!IsBezMl(p.Order)) continue;
            var name = p.PartName.Trim();
            if (name.Length == 0 || !lower.Contains(name.ToLowerInvariant())) continue;

            if (lower.Contains(SetupMarker) && p.SetupTimePlan <= 0) return true;
            if (lower.Contains(ProductionMarker) && p.SingleProductionTimePlan <= 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Претензия к «Штучной/длительной работе» — либо к порогам (их считает C#,
    /// <see cref="PartContext.IsSmallBatch"/>, модель регулярно пересчитывает с
    /// ошибками вплоть до галлюцинаций «MachiningTime не указано»), либо
    /// требование конкретики в MasterMachiningDetail (причина по регламенту САМОДОСТАТОЧНА
    /// при попадании в пороги — конкретика не нужна, в отличие от «Другое»/
    /// «Доработка»; модель формулирует это разными словами прогон от прогона —
    /// «порог», «конкретика», «детализация», «релевантное объяснение» — все они
    /// сводятся к одной и той же ошибочной претензии). Вызывающий код применяет
    /// отсев только когда все детали дня с этой причиной штучные.
    /// </summary>
    private static bool IsSmallBatchThresholdClaim(string signal)
    {
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains("штучн")) return false;
        return lower.Contains("порог") || lower.Contains("конкретик")
            || lower.Contains("детализ") || lower.Contains("релевантн");
    }

    /// <summary>
    /// Эхо exclude-подсказки «…может негативно повлиять на К1 оператора» при
    /// разовой причине (освоение, несоответствующие заготовки, «разовое …») —
    /// основание предложить исключение из отчёта, а не эскалировать.
    /// </summary>
    private static bool IsK1ExcludeEcho(string signal) =>
        K1Mention.IsMatch(signal)
        && (signal.Contains("освоен", StringComparison.OrdinalIgnoreCase)
            || signal.Contains("заготовк", StringComparison.OrdinalIgnoreCase)
            || signal.Contains("разов", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// «Освоение … без конкретики / не подтверждено историей / с низкой
    /// уверенностью / требует уточнения» — модель требует то, чего регламент не
    /// требует. Применяется только когда в данных есть детерминированно
    /// подтверждённое освоение (<see cref="MasteringAutoApprover.IsConfirmedMastering"/>).
    /// Формулировки про найденное опровержение («уже выполнялась», «противоречит»,
    /// «опровергнуто») не трогаем — это корректные эскалации.
    /// </summary>
    private static bool IsUnconfirmedMasteringClaim(string signal)
    {
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains("освоен")) return false;
        if (lower.Contains("выполнялась") || lower.Contains("делалась")
            || lower.Contains("противореч") || lower.Contains("опроверг"))
            return false;
        return lower.Contains("конкретик")
            || lower.Contains("не подтвержд")
            || lower.Contains("уточнен")
            || lower.Contains("уверенност")
            || lower.Contains("требует проверк"); // «требует проверки подтверждения» — та же претензия другими словами
    }

    private static bool TryParsePercent(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);

    /// <summary>
    /// Несерийный станок (AnalyzeRequest.IsSerialMachine == false): ЛЮБАЯ претензия
    /// к КПД изготовления — с числами и без («КПД изготовления 44%», «вне нормы») —
    /// показатель не оценивается ни в одном контуре. Состояния б/и, машинное время,
    /// нормативы и противоречия данных не задеваются: в них нет связки КПД+изготовление.
    /// </summary>
    private static bool IsNonSerialProductionKpdClaim(string signal, AnalyzeRequest request)
    {
        if (request.IsSerialMachine != false) return false;
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains(ProductionMarker) || !lower.Contains("кпд")) return false;
        return lower.Contains('%') || lower.Contains("норм");
    }

    /// <summary>
    /// Жалоба «Другое без детализации/подтверждения», когда у названной детали
    /// детализация ЕСТЬ. Модель не видит... точнее, видит (комментарии приложены
    /// с v9), но стабильно пишет «без детализации» про непустой комментарий
    /// (пилот 25.09, QTS350 23.09: «без подтверждения освоения» при детальном
    /// «Другом» про вмятины). Сверка фактом: причина «Другое» + непустая
    /// детализация своей категории = объяснено. Безымянная жалоба режется, только
    /// если ВСЕ «Другое»-строки дня с детализацией (иначе может относиться
    /// к строке без неё); жалоба без единой «Другое»-строки в дне — галлюцинация.
    /// Сюда же — перепутанный адресат: «без подтверждения ОСВОЕНИЯ», когда
    /// у названной детали причина наладки выбрана, НЕ «Освоение», и детализация
    /// есть (освоение тут ни при чём, требовать его подтверждения не с чего).
    /// </summary>
    private static readonly string[] DrugoeMarkers = ["другое", "другой", "другого"];

    private static readonly string[] UnexplainedMarkers =
    [
        "без подтвержден", "без детализац", "без уточнен", "без объяснен",
        "без конкретик", "не подтвержден", "не детализирован", "не объяснен",
        "требует уточнения", "требует уточнить", "требует детализац",
        "не указан", "отсутств", "нужда", "необходимо",
    ];

    /// <summary>
    /// Требование подтвердить причину, которую НИКТО не выбирал, при наличии
    /// у строк детализированной другой причины («КПД наладки=21% без подтверждения
    /// освоения/заготовок», когда выбрано детальное «Другое» — пилот 25.09,
    /// QTS350 23.09, флип туда-обратно на том же входе). Требование направлено
    /// не по адресу: подтверждать нечего. Снимает флипы, делая выходы инвариантными.
    /// Границы (чтобы не съесть легитимное):
    ///  • «Другое» и нормативные причины здесь не разбираются (у них свои правила);
    ///  • причина востребована хоть одной строкой в нужном поле → сохраняем;
    ///  • строка без причины вообще → сохраняем (требование может относиться к ней);
    ///  • привязка — по числу КПД из сигнала (±1.5 п.п.), затем по имени, иначе весь день,
    ///    но тогда все строки обязаны быть с детализированными чужими причинами.
    /// </summary>
    private static readonly (string Stem, bool Setup)[] ReasonStems =
    [
        ("освоен", true), ("типовой", true),
        ("заготов", false), ("штучн", false), ("разов", false),
        ("ученик", true), ("ученик", false),
        ("неопытн", true), ("неопытн", false),
        ("доработ", true), ("доработ", false),
        ("не по техпроцесс", true), ("не по техпроцесс", false),
    ];

    private static bool IsMisaddressedReasonDemand(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        if (!UnexplainedMarkers.Any(m => lower.Contains(m))) return false;

        var setupSide = lower.Contains(SetupMarker);
        var prodSide = lower.Contains(ProductionMarker);
        bool? setupOnly = setupSide && !prodSide ? true
            : prodSide && !setupSide ? false
            : (bool?)null;

        var hits = ReasonStems
            .Where(s => lower.Contains(s.Stem)
                && (setupOnly == null || s.Setup == setupOnly.Value))
            .ToList();
        if (hits.Count == 0) return false;

        var scope = ScopeByKpdNumber(lower, parts, setupOnly)
            ?? (NamedParts(lower, parts) is { Count: > 0 } named ? named : parts);
        if (scope.Count == 0) return false;

        return scope.All(p =>
        {
            List<(string Reason, string Detail)> fields = setupOnly == true
                ? [(p.MasterSetupComment, p.MasterSetupDetail)]
                : setupOnly == false
                    ? [(p.MasterMachiningComment, p.MasterMachiningDetail)]
                    : [(p.MasterSetupComment, p.MasterSetupDetail),
                       (p.MasterMachiningComment, p.MasterMachiningDetail)];
            // Востребованную причину выбрали — требование может относиться к ней.
            if (fields.Any(f => hits.Any(h => MatchesStem(f.Item1, h, setupOnly)))) return false;
            // Иначе: детализированная чужая причина есть — требование не по адресу.
            return fields.Any(f => !string.IsNullOrWhiteSpace(f.Item1)
                && !string.IsNullOrWhiteSpace(EffectiveDetailFor(f, p)));
        });
    }

    private static bool MatchesStem(string reasonText, (string Stem, bool Setup) hit, bool? setupOnly)
    {
        if (string.IsNullOrWhiteSpace(reasonText)) return false;
        var r = reasonText.ToLowerInvariant();
        if (!r.Contains(hit.Stem)) return false;
        // Категория stem должна соответствовать стороне проверки.
        return setupOnly == null || hit.Setup == setupOnly.Value;
    }

    private static string EffectiveDetailFor((string Reason, string Detail) field, PartContext p)
    {
        if (!string.IsNullOrWhiteSpace(field.Detail)) return field.Detail;
        // Архивное поле — только когда оба новых пусты (как в промпте).
        return string.IsNullOrWhiteSpace(p.MasterSetupDetail)
            && string.IsNullOrWhiteSpace(p.MasterMachiningDetail)
            ? p.MasterComment ?? "" : "";
    }

    /// <summary>
    /// Строки, чьё КПД совпадает с числами из сигнала (±1.5 п.п., категория та же).
    /// Пусто — числа ни к чему не привязались (не Sommerfeld-привязка, а null).
    /// </summary>
    private static List<PartContext>? ScopeByKpdNumber(
        string lowerSignal, List<PartContext> parts, bool? setupOnly)
    {
        var matches = KpdClaim.Matches(lowerSignal);
        // KpdClaim работает с исходным регистром; lower уже lower — ок.
        var found = false;
        var scope = new List<PartContext>();
        foreach (Match m in matches)
        {
            if (!TryParsePercent(m.Groups["val"].Value, out var val)) continue;
            var isSetup = m.Groups["cat"].Value.StartsWith("наладк", StringComparison.OrdinalIgnoreCase)
                || m.Groups["partial"].Success;
            if (setupOnly != null && isSetup != setupOnly.Value) continue;
            found = true;
            scope.AddRange(parts.Where(p =>
            {
                var r = isSetup ? p.SetupRatio : p.ProductionRatio;
                return r.HasValue && Math.Abs(r.Value * 100 - val) <= 1.5;
            }));
        }
        if (!found) return null;
        return scope.Distinct().ToList();
    }

    private static bool IsRefutedDrugoeClaim(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        var mentionsDrugoe = DrugoeMarkers.Any(m => lower.Contains(m));
        var claimsUnexplained = UnexplainedMarkers.Any(m => lower.Contains(m));
        if (!claimsUnexplained) return false;

        // Перепутанный адресат (пилот 25.09, QTS350 23.09): «без подтверждения
        // освоения», а у названной детали причина наладки выбрана, не «Освоение»,
        // и детализация есть. Без названных деталей не срабатывает.
        // HasDetail объявлен ниже — local function, вызов выше допустим.
        if (lower.Contains("освоен"))
        {
            var namedHere = NamedParts(lower, parts);
            if (namedHere.Count > 0
                && namedHere.All(p => !string.IsNullOrWhiteSpace(p.MasterSetupComment)
                    && !p.MasterSetupComment.Trim().Equals("Освоение", StringComparison.OrdinalIgnoreCase)
                    && HasDetail(p, true)))
                return true;
        }

        if (!mentionsDrugoe) return false;

        var setupSide = lower.Contains(SetupMarker);
        var prodSide = lower.Contains(ProductionMarker);

        static bool IsDrugoe(PartContext p, bool setup) =>
            (setup ? p.MasterSetupComment : p.MasterMachiningComment)
                .Trim().Equals("Другое", StringComparison.OrdinalIgnoreCase);

        static bool HasDetail(PartContext p, bool setup)
        {
            var detail = setup ? p.MasterSetupDetail : p.MasterMachiningDetail;
            if (!string.IsNullOrWhiteSpace(detail)) return true;
            // Архивное поле — только когда оба новых пусты (как в промпте).
            return string.IsNullOrWhiteSpace(p.MasterSetupDetail)
                && string.IsNullOrWhiteSpace(p.MasterMachiningDetail)
                && !string.IsNullOrWhiteSpace(p.MasterComment);
        }

        var named = NamedParts(lower, parts);
        var scope = named.Count > 0 ? named : parts;
        // Категория из текста сигнала; без неё и при обеих — смотрим обе стороны.
        bool? setupOnly = setupSide && !prodSide ? true
            : prodSide && !setupSide ? false
            : (bool?)null;
        var candidates = scope.Where(p =>
            setupOnly == true ? IsDrugoe(p, true)
            : setupOnly == false ? IsDrugoe(p, false)
            : IsDrugoe(p, true) || IsDrugoe(p, false)).ToList();
        // Жалоба мимо всех «Другое»-строк — не о чем говорить.
        if (candidates.Count == 0) return true;

        return candidates.All(p =>
            setupOnly == true ? HasDetail(p, true)
            : setupOnly == false ? HasDetail(p, false)
            : HasDetail(p, true) || HasDetail(p, false));
    }
    /// без заказа (Order пустой или «Без М/Л» — зеркало hasOrder из HardRule) и
    /// план там действительно нулевой: норма, объяснений не требует.
    /// Безымянная жалоба режется, только если без заказа весь день, — иначе
    /// она может относиться к строке с заказом, где plan=0 — недоработка
    /// нормирования и детерминированный hard.
    /// </summary>
    private static bool IsNoOrderPlan0Claim(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        var mentionsPlan0 = lower.Contains("plan=0") || lower.Contains("план=0") || lower.Contains("план 0")
            || (lower.Contains("норматив") && (lower.Contains("отсутств") || lower.Contains("нет ")
                || lower.Contains("не установлен") || lower.Contains("не указан") || lower.Contains("нулев")));
        if (!mentionsPlan0) return false;

        static bool NoOrderNoNorm(PartContext p) =>
            (string.IsNullOrWhiteSpace(p.Order) || IsBezMl(p.Order))
            && (p.SetupTimePlan <= 0 || p.SingleProductionTimePlan <= 0);

        var named = NamedParts(lower, parts);
        return named.Count > 0 ? named.All(NoOrderNoNorm) : parts.Count > 0 && parts.All(NoOrderNoNorm);
    }
}
