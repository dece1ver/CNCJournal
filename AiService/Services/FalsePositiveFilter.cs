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
        "не проведен",
        "не была",
        "не было",
        "нулев",
        "0 мин",
        "0мин",
        "наладка без",
        "наладка не",
        "наладки не",
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
                hasConfirmedMastering && IsUnconfirmedMasteringClaim(signal) ? "освоение подтверждено детерминированно" :
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
    /// Сигнал о «наладки не было» — халлюцинация, только когда либо ни одна деталь
    /// в сигнале не названа явно (день-уровневый сигнал, старое поведение), либо
    /// ВСЕ явно названные детали действительно б/н. Раньше проверка была день-уровневой
    /// (hasNoSetupPart без привязки к конкретной детали) — если сутки содержали хотя бы
    /// одну б/н-деталь, сигнал про СОВСЕМ ДРУГУЮ деталь с реальной наладкой тоже гасился
    /// (кейс 2026-06-30 Rontek HTC650M: «кулачки» наладились 56 мин при плане 0, но два
    /// других изделия того же дня были б/н — сигнал про «кулачки» ошибочно съеден).
    /// </summary>
    private static bool IsNoSetupHallucination(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains(SetupMarker) || !SetupKeywords.Any(kw => lower.Contains(kw)))
            return false;

        var named = NamedParts(lower, parts);
        return named.Count == 0 || named.All(IsNoSetup);
    }

    private static bool IsNoProductionHallucination(string signal, List<PartContext> parts)
    {
        var lower = signal.ToLowerInvariant();
        if (!lower.Contains(ProductionMarker) || !ProductionKeywords.Any(kw => lower.Contains(kw)))
            return false;

        var named = NamedParts(lower, parts);
        return named.Count == 0 || named.All(IsNoProduction);
    }

    /// <summary> Детали, чьё имя явно упомянуто в (уже lower-cased) тексте сигнала. </summary>
    private static List<PartContext> NamedParts(string lowerSignal, List<PartContext> parts) =>
        parts.Where(p => p.PartName.Trim().Length > 0
            && lowerSignal.Contains(p.PartName.Trim().ToLowerInvariant())).ToList();

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
}
