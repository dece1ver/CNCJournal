using AiService.Models;
using System.Text;
using System.Text.Json;

namespace AiService.Services;

/// <summary>
/// Инструменты агентского контура дневного анализа (стадия 1).
///
/// ВАЖНО про стадию 1: у AiService нет доступа в БД (нет SqlClient и connection string —
/// сервис stateless, всё приезжает в AnalyzeRequest). Поэтому tools читают данные
/// ИЗ ЗАПРОСА (история/отчёты уже приложены клиентом). Для будущего прямого доступа
/// в БД (cnc_normatives, cnc_deviation_reasons, cnc_shifts) оставлен шов IAgentDataSource:
/// замена источника — без правок цикла и инструментов.
/// Правило обрезки: каждый результат ≤ MaxResultChars, иначе контекст разъезжается.
/// </summary>
public static class AgentTools
{
    public const int MaxResultChars = 1500;

    /// <summary>
    /// Сериализация данных для модели: кириллица как есть (\u-последовательности
    /// жрут токены), без отступов. Данные — JSON, а не проза: модель не должна
    /// выпарсивать «Без М/Л», «КПД=б/н» и минуты из буллетов (кейс QTS350 2026-09-24).
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Есть ли заказ у строки: непустой и не «Без М/Л». Зеркало hasOrder из
    /// HardRuleEvaluator: единственное место решения, модель строку не разбирает
    /// (флаг естьЗаказ в JSON), промпт запрещает ей это делать.
    /// </summary>
    public static bool HasOrder(string? order) =>
        !string.IsNullOrWhiteSpace(order)
        && !order.Trim().Equals("Без М/Л", StringComparison.OrdinalIgnoreCase);

    // ── Описания для поля tools запроса /api/chat ──

    public static readonly IReadOnlyList<ChatTool> Definitions =
    [
        new ChatTool
        {
            Name = "get_reason_semantics",
            Description = "Семантика одной причины из комбобокса (что объясняет, нужна ли детализация). "
                + "Вызывай для КАЖДОЙ причины, встреченной в дне, прежде чем решать, объяснена ли аномалия. "
                + "Не выдумывай смысл причин — только этот источник.",
            ParametersJson = """{"type":"object","properties":{"reason":{"type":"string","description":"Текст причины (причинаОтклоненияВНаладке/причинаОтклоненияВИзготовлении из JSON дня)"},"category":{"type":"string","description":"setup (наладка) или machining (изготовление)"}},"required":["reason","category"]}""",
        },
        new ChatTool
        {
            Name = "get_part_history",
            Description = "История детали (прошлые смены под ДРУГИМИ заказами, JSON) + решения аналитиков по ним. "
                + "Нужна ТОЛЬКО для проверки заявлений «Освоение» / «Некорректные нормативы» / смена УП. "
                + "Для остальных причин историю НЕ запрашивай — она ничего не решает.",
            ParametersJson = """{"type":"object","properties":{"partName":{"type":"string"},"setup":{"type":"integer"}},"required":["partName"]}""",
        },
        new ChatTool
        {
            Name = "get_past_decisions",
            Description = "Последние решения аналитиков по этому станку (как они закрывали спорное). "
                + "Ориентир стиля, а не основание для эскалации: прошлое не создаёт текущей аномалии.",
            ParametersJson = """{"type":"object","properties":{"limit":{"type":"integer","description":"Сколько записей, 1-10"}},"required":[]}""",
        },
        new ChatTool
        {
            Name = "get_normatives",
            Description = "Числа дня по детали из данных (JSON: планНаладки, норматив, машВремя). "
                + "СТАДИЯ 1: читает значения из запроса; прямое чтение cnc_normatives — стадия 2 (нужен доступ AiService в БД).",
            ParametersJson = """{"type":"object","properties":{"partName":{"type":"string"}},"required":["partName"]}""",
        },
        new ChatTool
        {
            Name = "get_shift_report",
            Description = "Суточные отчёты мастера (день/ночь, JSON): мастер, простой minutes, причина, комментарий. "
                + "Для проверки релевантности пары (причина + комментарий) простою.",
            ParametersJson = """{"type":"object","properties":{},"required":[]}""",
        },
    ];

    // ── Исполнение ──

    public static string Execute(AnalyzeRequest req, string name, string argsJson)
    {
        using var doc = TryParse(argsJson);
        var args = doc?.RootElement ?? default;

        var result = name switch
        {
            "get_reason_semantics" => ReasonSemantics(
                req, Get(args, "reason"), Get(args, "category")),
            "get_part_history" => PartHistory(
                req, Get(args, "partName"), GetInt(args, "setup")),
            "get_past_decisions" => PastDecisions(req, GetInt(args, "limit") ?? 5),
            "get_normatives" => Normatives(req, Get(args, "partName")),
            "get_shift_report" => ShiftReport(req),
            _ => $"Неизвестный инструмент «{name}». Доступны: {string.Join(", ", Definitions.Select(d => d.Name))}.",
        };

        if (result.Length > MaxResultChars)
            result = result[..MaxResultChars] + "… [обрезано]";
        return result;
    }

    // ── Каталог причин: единственное место семантики (зеркало cnc_deviation_reasons + регламент) ──
    // Формат: причина → (категории, требует ли комментария, однострочное правило).
    // Держится в синхроне с таблицей cnc_deviation_reasons (RequireComment) вручную до стадии 2 (БД).

    private static readonly Dictionary<string, (string Cats, bool ReqComment, string Rule)> ReasonCatalog =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Освоение"] = ("наладка", false, "Объясняет ЛЮБУЮ аномалию наладки без детализации. Проверка только историей (get_part_history): запись с FinishedCount>0 под другим заказом без смены КД/технологии/УП = опровержение. Истории нет = подтверждено. Изменение УП/КД как причина освоения требует конкретики (что изменено и почему)."),
            ["Изготовление типовой детали"] = ("наладка", false, "Объясняет ТОЛЬКО КПД наладки >200%. Низкий КПД и превышение частичной наладки НЕ объясняет."),
            ["Доработка"] = ("любая", true, "plan=0 и КПД=0 — норма. Обязательна конкретика: что дорабатывается и почему. Пустое «доработка» = не объяснено."),
            ["Другое"] = ("любая", true, "Только если ничего из списка не подходит. Нужна конкретная детализация. Ожидание с другого станка («ждал деталь») — самодостаточно. Штатная операция из норматива (контроль, чистка) — НЕ объяснение, кроме «заложенного времени недостаточно». Вина оператора — объяснено, но не кандидат в exclude."),
            ["Изготовление не по техпроцессу"] = ("любая", true, "ПОДТВЕРЖДЕНИЕ эскалации, не «всё в порядке». В комментарии должно быть названо отклонение: пропущенная/лишняя/заменённая операция, другой станок («деталь с других станков», «заложена N установок», «с универсального участка»)."),
            ["Некорректные нормативы"] = ("любая", false, "ПОДТВЕРЖДЕНИЕ эскалации. Если деталь уже нормально шла с этими нормативами (см. историю) — причина сомнительна, зафиксируй отдельно."),
            ["Отсутствие нормативов"] = ("любая", false, "С заказом — ПОДТВЕРЖДЕНИЕ эскалации. Без заказа («Без М/Л») — норма, объяснений не требует."),
            ["Неопытный оператор"] = ("любая", false, "Про ЧЕЛОВЕКА, не про деталь: история не проверяется. Объясняет наладку И изготовление сразу, без конкретики. В exclude НЕ попадает."),
            ["Работа ученика"] = ("любая", false, "Как «Неопытный оператор», плюс: КПД<100% в категории причины = кандидат в exclude (ученик не должен терять К1)."),
            ["Несоответствующие заготовки"] = ("изготовление", true, "Конкретные числа в комментарии (диаметр, размер) = техническое подтверждение, дальше проверять нечего. Пограничный КПД (1-3 пункта от порога) причину не дисквалифицирует. Кандидат в exclude."),
            ["Разовое изменение времени из-за проблем с инструментом/оборудованием"] = ("изготовление", true, "Нужен конкретный инструмент/событие (не простой, не повтор оператора) + непротиворечивый КПД. Кандидат в exclude. Пороги партий сюда неприменимы."),
            ["Штучная/длительная работа"] = ("изготовление", false, "Пороги: м/в<3мин и ≤10 дет ИЛИ м/в≥3мин и ≤5 дет (проверены кодом, не пересчитывай). В пороге объясняет ЛЮБОЙ КПД кроме ровно 0%. Детализация не требуется."),
            ["Особенности изготовления"] = ("любая", true, "Требует конкретики (напр. деталь по двум установкам на двух паллетах)."),
            ["Небрежное отношение к работе"] = ("любая", true, "С конкретным комментарием — объяснено, без эскалации, в exclude НЕ попадает."),
            ["Некорректное заполнение"] = ("любая", false, "Данные недостоверны → эскалация."),
            ["Двустаночное обслуживание"] = ("любая", false, "Особых условий нет."),
            ["Частичная наладка"] = ("наладка", false, "Переходная наладка: куски одной наладки идут через механизм простоев (указанныеПростои), в факт НЕ входят — умышленно, чтобы недоделанная работа не портила КПД. Сама по себе НЕ проблема и НЕ аномалия: проблема — ТОЛЬКО системный сигнал клиента «КПД частичной наладки … < 70%» (частичная вышла за норматив). Без этого сигнала — не эскалируй, tools по ней больше не вызывай. С ним — проверяй объяснение мастера, а не сам факт кусков."),
        };

    /// <summary>
    /// Точное совпадение с названием причины из каталога (для IsWellFormedExcludeEntry:
    /// голое название в 4-м сегменте exclude-записи — нарушение формата).
    /// </summary>
    internal static bool IsKnownReasonName(string s) =>
        !string.IsNullOrWhiteSpace(s) && ReasonCatalog.ContainsKey(s.Trim());

    private static string ReasonSemantics(AnalyzeRequest req, string reason, string category)
    {
        // Не-аномалии — единый короткий ответ, tools по ним вызывать больше не нужно.
        // (Это и есть «одно место» вместо троекратных повторов в system_prompt.txt:
        // б/н, б/и, plan=0 без заказа, частичная наладка без системного сигнала,
        // числовой КПД без системного сигнала, сам процент простоев — НЕ аномалии,
        // никогда не эскалируй.)
        if (string.IsNullOrWhiteSpace(reason))
            return "Причина не выбрана. Без причины аномалия объясняется только: б/н/б/и (НЕ аномалии всегда), числовой КПД без системного сигнала, plan=0 без заказа, частичная наладка без системного сигнала. Всё остальное без причины = не объяснено.";

        var trimmed = reason.Trim();

        // Несерийный станок: причины изготовления не разбираем — изготовление
        // не оценивается (v7 скелета). Исключение: модель сверяет hard-факт
        // («не по техпроцессу»/нормативы) — по ним отвечаем как обычно.
        if (req.IsSerialMachine == false
            && category.Trim().Equals("machining", StringComparison.OrdinalIgnoreCase)
            && !HardRuleEvaluator.EscalationReasons.Contains(trimmed))
            return $"«{trimmed}» — изготовление на этом станке НЕ оценивается (несерийный): "
                + "аномалии изготовления не проверяй, в signals не пиши, в exclude не предлагай.";

        // Каталог — ПЕРЕД справочником простоев: модель цитирует причины с довесками
        // («частичная наладка: 38 мин», «Другое: ждал деталь»), точное совпадение
        // не срабатывает и «неизвестна каталогу» читается как «подозрительна»
        // (кейс QTS350 2026-09-24: «Необъяснённая частичная наладка» именно отсюда).
        // Нормализация — префиксная, по самому длинному ключу; в ответе помечается.
        var key = ReasonCatalog.ContainsKey(trimmed)
            ? trimmed
            : ReasonCatalog.Keys.OrderByDescending(k => k.Length)
                .FirstOrDefault(k => trimmed.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        var normalizedNote = key != null && !key.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
            ? $" [запрос нормализован к «{key}»]"
            : "";

        // Причина простоя («Отсутствие оператора» и т.п.) — ДРУГОЙ справочник
        // (cnc_downtime_reasons, суточный отчёт), в каталоге отклонений детали её нет
        // и быть не должно. Отвечаем явно, иначе модель читает «неизвестна каталогу»
        // как «подозрительна» и эскалирует (пилот 24.09.2026, SKT21 22.09).
        if (key == null
            && req.DowntimeReasons.Any(r => r.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return $"«{trimmed}» — причина ПРОСТОЯ из закрытого списка (суточный отчёт), "
                + "а не причина отклонения детали: к наладке/изготовлению неприменима, аномалии деталей "
                + "ею не объясняй. В отчёте мастера самодостаточна: комментарий сверх неё требует "
                + "только причина «Другое».";

        if (key == null || !ReasonCatalog.TryGetValue(key, out var entry))
            return $"Причина «{trimmed}» неизвестна каталогу. Известные: {string.Join("; ", ReasonCatalog.Keys)}. Свободный комментарий без причины из каталога аномалию НЕ закрывает (кроме б/н/б/и — это состояния, а не причины).";

        var catOk = entry.Cats == "любая"
            || (category.Trim().Equals("setup", StringComparison.OrdinalIgnoreCase) && entry.Cats.Contains("налад"))
            || (category.Trim().Equals("machining", StringComparison.OrdinalIgnoreCase) && entry.Cats.Contains("изготов"));
        var binding = catOk ? "" : " ВНИМАНИЕ: причина чужой категории — наладку объясняет только причина наладки, изготовление — только причина изготовления. ";
        return $"«{reason.Trim()}» [{entry.Cats}{(entry.ReqComment ? ", требует комментария" : "")}].{normalizedNote}{binding} {entry.Rule}";
    }

    private static string PartHistory(AnalyzeRequest req, string partName, int? setup)
    {
        if (string.IsNullOrWhiteSpace(partName))
            return "Укажи partName.";

        var matches = req.Parts
            .Where(p => p.PartName.Trim().Equals(partName.Trim(), StringComparison.OrdinalIgnoreCase)
                && (!setup.HasValue || p.Setup == setup.Value))
            .ToList();
        if (matches.Count == 0)
            return $"Детали «{partName}» нет в данных дня.";

        var sb = new StringBuilder();
        foreach (var p in matches.Take(3))
        {
            var h = p.PartsHistory;
            // Истории нет = заявление «впервые» ПОДТВЕРЖДЕНО (проверять нечего).
            if (h == null || h.RecordsFound == 0 || h.Lines.Count == 0)
            {
                sb.Append(JsonSerializer.Serialize(new
                {
                    деталь = p.PartName,
                    уст = p.Setup,
                    заказ = p.Order,
                    прошлыхСмен = 0,
                    вывод = "под другими заказами не выполнялась = заявление «впервые» ПОДТВЕРЖДЕНО",
                }, JsonOptions));
            }
            else
            {
            sb.Append(JsonSerializer.Serialize(new
            {
                деталь = p.PartName,
                уст = p.Setup,
                заказ = p.Order,
                прошлыхСмен = h.RecordsFound,
                строки = h.Lines.Take(5).Select(l => new
                {
                    дата = l.ShiftDate,
                    готовоШт = l.FinishedCount,
                    кпдИзготовления = l.ProductionRatio,
                    кпдНаладки = l.SetupRatio,
                    решение = l.AnalystDecision,
                }),
            }, JsonOptions));
            }
            sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static string PastDecisions(AnalyzeRequest req, int limit)
    {
        limit = Math.Clamp(limit, 1, 10);
        var lines = req.Parts
            .SelectMany(p => p.PartsHistory?.Lines ?? [])
            .GroupBy(l => l.ShiftDate)
            .OrderByDescending(g => g.Key)
            .Take(limit)
            .Select(g =>
            {
                var first = g.First();
                var comment = string.IsNullOrWhiteSpace(first.AnalystComment) ? "" : $" — «{first.AnalystComment.Trim()}»";
                var fb = string.IsNullOrWhiteSpace(first.AiFeedback) ? "" : $" [отзыв на ИИ: {first.AiFeedback.Trim()}]";
                return $"{g.Key}: {first.AnalystDecision}{comment}{fb}";
            })
            .ToList();
        return lines.Count == 0
            ? "Прошлых решений аналитика по этому станку в данных нет."
            : "Прошлые решения (стиль, не основание для эскалации): " + string.Join(" | ", lines);
    }

    private static string Normatives(AnalyzeRequest req, string partName)
    {
        if (string.IsNullOrWhiteSpace(partName))
            return "Укажи partName.";
        var matches = req.Parts
            .Where(p => p.PartName.Trim().Equals(partName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(p => new
            {
                деталь = p.PartName,
                уст = p.Setup,
                заказ = p.Order,
                естьЗаказ = HasOrder(p.Order),
                планНаладки = p.SetupTimePlan,
                фактНаладки = p.SetupTimeFact,
                норматив = p.SingleProductionTimePlan,
                машВремя = p.MachiningTime,
                // 0 = норматив НЕ УСТАНОВЛЕН (решает модель по естьЗаказ, не по строке).
                готовоШт = p.FinishedCount,
            })
            .ToList();
        if (matches.Count == 0)
            return $"Детали «{partName}» нет в данных дня.";
        // Источник — данные дня; прямое чтение cnc_normatives — стадия 2.
        return string.Join(" ", matches.Select(m => JsonSerializer.Serialize(m, JsonOptions)));
    }

    private static string ShiftReport(AnalyzeRequest req)
    {
        if (req.ShiftReports.Count == 0)
            return "Отчётов мастера в данных нет — проверять нечего.";
        return string.Join(" ", req.ShiftReports.Select(r =>
        {
            if (!r.ReportExists)
                return JsonSerializer.Serialize(new
                {
                    смена = r.Shift,
                    естьОтчёт = false,
                }, JsonOptions);
            // Простой — НЕотмеченное время (смена минус все отмеченные записи),
            // в КПД не входит: простой объясняет только простой, никогда — КПД.
            return JsonSerializer.Serialize(new
            {
                смена = r.Shift,
                естьОтчёт = true,
                минут = r.ShiftMinutes,
                мастер = string.IsNullOrWhiteSpace(r.Master) ? null : r.Master.Trim(),
                мастерНеУказан = string.IsNullOrWhiteSpace(r.Master),
                простойМин = r.FreshUnspecifiedDowntimes,
                простойДоля = r.ShiftMinutes > 0
                    ? Math.Round(r.FreshUnspecifiedDowntimes / r.ShiftMinutes, 4)
                    : 0,
                простойЦеликом = r.FreshUnspecifiedDowntimes >= r.ShiftMinutes && r.ShiftMinutes > 0,
                причинаПростоя = string.IsNullOrWhiteSpace(r.DowntimeReason) ? null : r.DowntimeReason.Trim(),
                комментарийМастера = string.IsNullOrWhiteSpace(r.MasterComment) ? null : r.MasterComment.Trim(),
                естьСтроки = r.HasParts,
            }, JsonOptions);
        }));
    }

    // ── Разбор аргументов ──

    private static JsonDocument? TryParse(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException) { return JsonDocument.Parse("{}"); }
    }

    private static string Get(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static int? GetInt(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var n)
            ? n
            : null;
}
