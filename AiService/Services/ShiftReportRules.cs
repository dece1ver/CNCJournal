using AiService.Models;

namespace AiService.Services;

/// <summary>
/// Результат детерминированной проверки суточных отчётов мастера (cnc_shifts).
/// HardSignals — нарушения, требующие ОБЯЗАТЕЛЬНОЙ эскалации (LLM не отменяет):
/// аналог HardRuleResult для построчных правил. SoftSignals — рекомендательные
/// (сейчас только S2 про частичную наладку): в ответ попадают, эскалацию не форсируют.
/// Семантическая релевантность пары (причина + комментарий), S1, добавится с промптом.
/// </summary>
public record ShiftReportRuleResult(
    List<string> HardSignals,
    List<string> SoftSignals)
{
    public bool MustEscalate => HardSignals.Count > 0;
}

public static class ShiftReportRuleEvaluator
{
    // Порог обязательности причины простоя — зеркалит DailyReportWindowViewModel:
    // UnspecifiedDayDowntimesNeedAttention => ratio is > 0.1 or < -0.1.
    private const double DowntimeReasonRatioThreshold = 0.1;

    // Допуск переполнения журнала сверх смены (ранний приход/задержка).
    // Ниже −10% длительности — hard R3, комментарии не объясняют.
    private const double OverflowToleranceRatio = 0.1;

    // Допуск расхождения снимка простоя из БД со свежим пересчётом (R4, минуты).
    // Формула детерминирована: любое расхождение сверх допуска — правки журнала
    // после сохранения отчёта, отчёт устарел.
    private const double StaleEpsilonMinutes = 1.0;

    // «Другое» (R2) требует детализации по регламенту («Требования к заполнению
    // и контролю», таблица причин). Минимум осмысленности, проверяемый кодом, —
    // длина; релевантность текста позже проверит модель (S1).
    private const int MinOtherCommentLength = 10;
    private const string OtherReason = "Другое";

    // Порог внимания к частичной наладке (S2) — зеркалит
    // DailyReportWindowViewModel.DayPartialSetupNeedAttention (ratio > 0.3).
    private const double PartialSetupAttentionRatio = 0.3;

    public static ShiftReportRuleResult Evaluate(AnalyzeRequest req)
    {
        var hard = new List<string>();
        var soft = new List<string>();

        // Старый клиент отчёты не шлёт — проверка пропускается молча.
        if (req.ShiftReports.Count == 0)
            return new ShiftReportRuleResult(hard, soft);

        var knownReasons = req.DowntimeReasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rep in req.ShiftReports)
        {
            var tag = $"[{rep.Shift}]";
            void AddHard(string signal) => hard.Add($"{tag} {signal}");

            if (!rep.ReportExists)
            {
                AddHard("Нет суточного отчёта мастера за смену — день закрывать нельзя");
                continue;
            }

            if (string.IsNullOrWhiteSpace(rep.Master))
                AddHard("В отчёте мастера не указан мастер");

            if (rep.ShiftMinutes <= 0)
            {
                AddHard("Некорректная длительность смены в запросе — проверка отчёта невозможна");
                continue;
            }

            var fresh = rep.FreshUnspecifiedDowntimes;
            var ratio = fresh / rep.ShiftMinutes;
            var reason = rep.DowntimeReason?.Trim() ?? string.Empty;
            var comment = rep.MasterComment?.Trim() ?? string.Empty;

            // R3: невозможный простой. Переполнение сверх допуска — hard безусловно.
            if (fresh < -OverflowToleranceRatio * rep.ShiftMinutes)
                AddHard($"Переполнение журнала {fresh:0}мин сверх смены ({ratio:0%}) — никакие комментарии не объясняют");
            else if (fresh > rep.ShiftMinutes)
                AddHard($"Неотмеченный простой {fresh:0}мин больше длительности смены — противоречие данных");

            // R1: причина обязательна при |доли простоя| свыше порога (зеркало VM).
            if ((ratio > DowntimeReasonRatioThreshold || ratio < -DowntimeReasonRatioThreshold)
                && string.IsNullOrWhiteSpace(reason))
                AddHard($"Неотмеченный простой {fresh:0}мин ({ratio:0%}) без причины из списка");

            // R2: «Другое» только с детализацией (регламент, таблица причин).
            if (OtherReason.Equals(reason, StringComparison.OrdinalIgnoreCase)
                && comment.Length < MinOtherCommentLength)
                AddHard("Причина «Другое» без объяснения с детализацией в комментарии мастера");

            // R4 (вариант A): снимок устарел — журнал правили после сохранения отчёта.
            if (Math.Abs(rep.StoredUnspecifiedDowntimes - fresh) > StaleEpsilonMinutes)
                AddHard($"Отчёт устарел: простой на момент сохранения {rep.StoredUnspecifiedDowntimes:0}мин, " +
                        $"сейчас {fresh:0}мин — мастеру пересохранить отчёт");

            // R5: смена целиком в простое подтверждается отсутствием записей.
            if (fresh >= rep.ShiftMinutes && rep.HasParts)
                AddHard("Смена целиком в простое, но в журнале есть записи — противоречие данных");

            // R6: причина обязана быть из закрытого справочника. Пустой справочник
            // (старый клиент) — судить не по чему, правило пропускается.
            if (!string.IsNullOrWhiteSpace(reason)
                && knownReasons.Count > 0
                && !knownReasons.Contains(reason))
                AddHard($"Причина «{reason}» вне справочника причин простоя");

            // S2: частичная наладка свыше порога — рекомендательно, не блокирует.
            if (rep.PartialSetupRatio > PartialSetupAttentionRatio)
                soft.Add($"{tag} Частичная наладка {rep.PartialSetupRatio:0%} > 30% — " +
                         "рекомендуется отразить в комментарии мастера");
        }

        return new ShiftReportRuleResult([.. hard.Distinct()], [.. soft.Distinct()]);
    }

    /// <summary>
    /// Чистит сырые shift_report_issues от модели: trim, пустые выкидываются,
    /// дубли схлопываются. Непустой итог (S1) эскалирует день — см. контроллер.
    /// </summary>
    public static List<string> CleanModelIssues(IEnumerable<string>? issues)
    {
        if (issues == null) return [];
        return [.. issues
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct()];
    }

    /// <summary>
    /// Модель регулярно кладёт проблемы суточного отчёта в общий signals
    /// (другими словами, чем в shift_report_issues) — тогда они дублируются
    /// в блоке «Данные» и ломают раскладку «каждому блоку своё». Такие эхо
    /// детерминированно перекладываются в shift-вопросы: в signals им не место.
    /// Эвристика узкая (требует связки с мастером/отчётом либо дневного простоя
    /// с требованием причины — материала R1-порога), построчные сигналы
    /// про смены без мастера не задевает.
    /// </summary>
    public static (List<string> DataSignals, List<string> ShiftEchoes) SplitShiftEchoes(
        IEnumerable<string>? signals)
    {
        var data = new List<string>();
        var echoes = new List<string>();
        if (signals == null) return (data, echoes);

        foreach (var s in signals)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            (IsShiftReportEcho(s) || IsDayDowntimeReasonDemand(s) ? echoes : data).Add(s.Trim());
        }
        return (data, echoes);
    }

    /// <summary>
    /// Требование причины дневного простоя («Дневной простой 22 мин (3% смены)
    /// без указания причины», кейс 21.09.2026) — вопрос суточного отчёта (R1,
    /// порог 10%), а не данных: судьбу решает DropUnneededReasonDemands
    /// по свежему пересчёту смены, в блоке «Данные» ему не место.
    /// </summary>
    private static bool IsDayDowntimeReasonDemand(string signal)
    {
        var lower = signal.ToLowerInvariant();
        if (!(lower.StartsWith("дневн") || lower.StartsWith("ночн"))) return false;
        if (!lower.Contains("просто") || !lower.Contains("причин")) return false;
        return lower.Contains("без ") || lower.Contains("не указан") || lower.Contains("неуказан")
            || lower.Contains("отсутств") || lower.Contains("нет ");
    }

    private static bool IsShiftReportEcho(string signal)
    {
        var lower = signal.ToLowerInvariant();
        // Прямые маркеры отчёта/мастера/смены...
        if (lower.Contains("отчёт мастера") || lower.Contains("отчет мастера")
            || (lower.Contains("смен") && lower.Contains("мастер"))
            || (lower.Contains("суточн") && lower.Contains("отч")))
            return true;
        // ...и косвенные: модель пишет про отчёт без слова «мастер»
        // («Отчёт устарел: простой увеличился…»). Построчные сигналы слова
        // «отчёт» не содержат (exclude-подсказки живут в отдельном поле).
        return (lower.Contains("отчёт") || lower.Contains("отчет"))
            && (lower.Contains("смен") || lower.Contains("суточн") || lower.Contains("прост"));
    }

    /// <summary>
    /// Режет требования комментария, когда причина простоя самодостаточна.
    /// На уровне суточного отчёта комментарий обязателен только при «Другое»
    /// (см. R2 и валидацию DailyReportWindow: мастер и причина; комментарий —
    /// опционален). Модель стабильно требует комментарий и при причине из списка
    /// («Отсутствие оператора» при простое целиком), вняв запрету в промпте дважды
    /// проигнорировала — поэтому правилом, а не текстом. Режутся жалобы
    /// на отсутствие комментария и претензии к релевантности («без релевантного
    /// комментария», кейс 22.09.2026 Mazak) — но последние только при ПУСТОМ
    /// комментарии: релевантным может быть лишь имеющийся текст, а пустому
    /// при самодостаточной причине добавить нечего. Претензии к содержанию
    /// имеющегося комментария («комментарий не о простое») и вопросы без
    /// привязки к смене сохраняются.
    /// </summary>
    public static (List<string> Kept, List<string> Dropped) DropSelfSufficientCommentDemands(
        AnalyzeRequest req, List<string> modelIssues)
    {
        var kept = new List<string>();
        var dropped = new List<string>();
        if (modelIssues.Count == 0) return (kept, dropped);

        var byShift = req.ShiftReports
            .Where(r => r.ReportExists && r.ShiftMinutes > 0)
            .ToDictionary(r => r.Shift.Trim().ToLowerInvariant(), r => r);

        foreach (var issue in modelIssues)
        {
            if (ShiftTag(issue) is { } tag
                && byShift.TryGetValue(tag, out var rep)
                && IsSelfSufficientReason(rep.DowntimeReason)
                && (IsCommentDemand(issue)
                    || (IsCommentRelevanceComplaint(issue)
                        && string.IsNullOrWhiteSpace(rep.MasterComment))))
            {
                dropped.Add(issue);
                continue;
            }
            kept.Add(issue);
        }
        return (kept, dropped);
    }

    /// <summary> Причина, которой по регламенту добавить нечего (всё, кроме «Другое»). </summary>
    private static bool IsSelfSufficientReason(string? reason)
    {
        var r = reason?.Trim() ?? "";
        return r.Length > 0 && !OtherReason.Equals(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Жалоба именно на отсутствие комментария (а не на его содержание). </summary>
    private static bool IsCommentDemand(string issue)
    {
        var lower = issue.ToLowerInvariant();
        if (!lower.Contains("комментар")) return false;
        return lower.Contains("отсутств")
            || lower.Contains("нет комментар")
            || lower.Contains("без комментар")
            || lower.Contains("требу")
            || lower.Contains("нуж")
            || lower.Contains("не указан")
            || lower.Contains("пуст")
            || lower.Contains("добавить");
    }

    /// <summary>
    /// Претензия к релевантности комментария («без релевантного/детального
    /// комментария»). От жалобы на отсутствие отличается тем, что при НЕПУСТОМ
    /// комментарии это genuine S1-вопрос — вызывающий код режет её, только если
    /// комментировать нечего (комментарий пуст, а причина самодостаточна).
    /// </summary>
    private static bool IsCommentRelevanceComplaint(string issue)
    {
        var lower = issue.ToLowerInvariant();
        if (!lower.Contains("комментар")) return false;
        return lower.Contains("релевант")
            || lower.Contains("детальн")
            || lower.Contains("не о прост")
            || lower.Contains("содержани");
    }

    /// <summary>
    /// Режет требования причины простоя, когда она не требуется: доля простоя
    /// в пределах ±10% смены (зеркало R1 и валидации DailyReportWindow) либо
    /// причина уже указана, а модель галлюцинирует её отсутствие. Релевантность
    /// имеющейся причины («нерелевантна», «не о простое») — другая претензия,
    /// сохраняется. Без привязки к смене — консервативно сохраняется.
    /// </summary>
    public static (List<string> Kept, List<string> Dropped) DropUnneededReasonDemands(
        AnalyzeRequest req, List<string> modelIssues)
    {
        var kept = new List<string>();
        var dropped = new List<string>();
        if (modelIssues.Count == 0) return (kept, dropped);

        var byShift = req.ShiftReports
            .Where(r => r.ReportExists && r.ShiftMinutes > 0)
            .ToDictionary(r => r.Shift.Trim().ToLowerInvariant(), r => r);

        foreach (var issue in modelIssues)
        {
            if (IsReasonDemand(issue)
                && ShiftTag(issue) is { } tag
                && byShift.TryGetValue(tag, out var rep))
            {
                var ratio = rep.FreshUnspecifiedDowntimes / rep.ShiftMinutes;
                var belowThreshold = ratio is <= DowntimeReasonRatioThreshold
                    and >= -DowntimeReasonRatioThreshold;
                if (belowThreshold || !string.IsNullOrWhiteSpace(rep.DowntimeReason))
                {
                    dropped.Add(issue);
                    continue;
                }
            }
            kept.Add(issue);
        }
        return (kept, dropped);
    }

    /// <summary> Жалоба именно на отсутствие причины (а не на её содержание). </summary>
    private static bool IsReasonDemand(string issue)
    {
        var lower = issue.ToLowerInvariant();
        if (!lower.Contains("причин")) return false;
        return lower.Contains("не указан")
            || lower.Contains("неуказан") // «причина неуказана» слитно
            || lower.Contains("без указан") // «без указания причины» (кейс 21.09.2026)
            || lower.Contains("отсутств")
            || lower.Contains("без причин")
            || lower.Contains("нет причин")
            || lower.Contains("требу")
            || lower.Contains("нуж")
            || lower.Contains("пуст");
    }

    /// <summary>
    /// Режет выдумки модели про отчёт, которого нет: «устарел», «мастер
    /// не указан», претензии к релевантности — когда проверять нечего.
    /// Триггер тот же, что рисует иконки наличия отчёта на главной
    /// (ReportExists из чтения cnc_shifts): два случая — блоков «Отчёт мастера»
    /// в запросе нет вообще (тогда любой shift-вопрос модели ни на что
    /// не опирается) и смена с признаком «отчёта нет» (тогда по этой смене
    /// режется всё, включая пересказ отсутствия своими словами: детерминированный
    /// hard «Нет суточного отчёта…» по такой смене есть всегда, а нечёткий дедуп
    /// в MergeIssues ловит пересказы на грани и может пропустить).
    /// Без привязки к смене — консервативно сохраняется.
    /// </summary>
    public static (List<string> Kept, List<string> Dropped) DropIssuesWithoutReport(
        AnalyzeRequest req, List<string> modelIssues)
    {
        var kept = new List<string>();
        var dropped = new List<string>();
        if (modelIssues.Count == 0) return (kept, dropped);

        // Блоков отчёта в запросе нет — модели нечего проверять.
        if (req.ShiftReports.Count == 0)
        {
            dropped.AddRange(modelIssues);
            return (kept, dropped);
        }

        // Условие зеркалит PromptBuilder.AppendShiftReports («отчёта нет»).
        var missingShifts = req.ShiftReports
            .Where(r => !r.ReportExists || r.ShiftMinutes <= 0)
            .Select(r => r.Shift.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (missingShifts.Count == 0)
        {
            kept.AddRange(modelIssues);
            return (kept, dropped);
        }

        foreach (var issue in modelIssues)
        {
            if (ShiftTag(issue) is { } tag && missingShifts.Contains(tag))
            {
                dropped.Add(issue);
                continue;
            }
            kept.Add(issue);
        }
        return (kept, dropped);
    }

    /// <summary>
    /// Детерминированная замена объяснения, когда эскалация вызвана ТОЛЬКО
    /// суточным отчётом: модель в этом случае регулярно перечисляет проблемы
    /// отчёта в explanation вопреки требованию краткости (v16+), а по записям
    /// сказать нечего — день чист. Возвращает null, если замена не нужна.
    /// </summary>
    public static string? ShiftOnlyExplanation(bool dataCaused, bool shiftCaused) =>
        (!dataCaused && shiftCaused)
            ? "Явных отклонений в записях не обнаружено. Есть вопросы к суточному отчёту мастера."
            : null;

    /// <summary>
    /// Итоговый список вопросов к отчёту мастера для ответа: детерминированные
    /// hard + soft, затем несогласия модели (S1). Порядок стабилен для UI-блока.
    /// Модельные формулировки, повторяющие детерминированный hard другими словами
    /// («[Ночь] Нет суточного отчёта» vs «Смена Ночь: отсутствие суточного отчёта»),
    /// выкидываются нечётким сравнением — детерминированный текст авторитетнее.
    /// </summary>
    public static List<string> MergeIssues(ShiftReportRuleResult det, IEnumerable<string>? modelIssues)
    {
        var merged = new List<string>(det.HardSignals);
        merged.AddRange(det.SoftSignals);
        var seen = new HashSet<string>(merged, StringComparer.OrdinalIgnoreCase);
        foreach (var m in CleanModelIssues(modelIssues))
        {
            if (seen.Contains(m) || IsCoveredByDet(m, det.HardSignals)) continue;
            merged.Add(m);
            seen.Add(m);
        }
        return merged;
    }

    /// <summary>
    /// Модельный вопрос покрыт детерминированным hard той же смены, если делит
    /// с ним не меньше половины значащих слов (минимум 2). Сравнение по нормали:
    /// регистр/ё/пунктуация сняты, префиксы смен не участвуют (смена сверена отдельно).
    /// Консервативно: непохожие и короткие формулировки всегда сохраняются.
    /// </summary>
    private static bool IsCoveredByDet(string modelIssue, List<string> detHards)
    {
        var mShift = ShiftTag(modelIssue);
        var mWords = SignificantWords(modelIssue);
        if (mWords.Count == 0) return false;
        var need = Math.Max(2, mWords.Count / 2);

        foreach (var h in detHards)
        {
            var hShift = ShiftTag(h);
            if (mShift != null && hShift != null && mShift != hShift) continue;
            var hWords = SignificantWords(h).ToHashSet(StringComparer.Ordinal);
            if (mWords.Count(w => hWords.Contains(w)) >= need) return true;
        }
        return false;
    }

    /// <summary> Смена из префикса: «[День] …», «Смена День: …» или вольности модели
    /// («Ночная смена: …», «Дневной простой …» — прилагательное тоже привязывает
    /// вопрос к смене; «…в ночную смену…» внутри строки — запасной вариант,
    /// кейс 22.09.2026 Mazak). Иначе null. </summary>
    private static string? ShiftTag(string signal)
    {
        var lower = signal.Trim().ToLowerInvariant();
        foreach (var (names, tag) in new[]
                 {
                     (new[] { "день", "дня" }, "день"),
                     (new[] { "ночь", "ночи" }, "ночь"),
                 })
        {
            if (names.Any(n => lower.StartsWith($"[{n}]") || lower.StartsWith($"смена {n}:")))
                return tag;
        }
        if (lower.StartsWith("дневн")) return "день";
        if (lower.StartsWith("ночн")) return "ночь";
        if (lower.Contains("дневную смену") || lower.Contains("дневной смене")
            || lower.Contains("за дневную смену") || lower.Contains("в дневную смену")) return "день";
        if (lower.Contains("ночную смену") || lower.Contains("ночной смене")
            || lower.Contains("за ночную смену") || lower.Contains("в ночную смену")) return "ночь";
        return null;
    }

    private static List<string> SignificantWords(string text) =>
        System.Text.RegularExpressions.Regex.Split(
                text.ToLowerInvariant().Replace('ё', 'е'), @"[^a-zа-я0-9]+")
            .Where(w => w.Length >= 4)
            .Distinct()
            .ToList();

    /// <summary>
    /// Построчная сводка проверки для UI-блока «Отчёт мастера»: по каждой смене —
    /// факты (простой, причина) и детерминированный вердикт. Показывается всегда,
    /// даже когда вопросов нет: видно, что отчёт проверен. Семантический вердикт
    /// модели (S1) живёт отдельно — в <see cref="MergeIssues"/>.
    /// </summary>
    public static List<string> Summarize(AnalyzeRequest req, ShiftReportRuleResult det)
    {
        var lines = new List<string>();
        if (req.ShiftReports.Count == 0) return lines;

        foreach (var rep in req.ShiftReports)
        {
            var tag = $"[{rep.Shift}]";
            if (!rep.ReportExists || rep.ShiftMinutes <= 0)
            {
                lines.Add($"{tag} Нет отчёта мастера");
                continue;
            }

            var ratio = rep.FreshUnspecifiedDowntimes / rep.ShiftMinutes;
            var idle = rep.FreshUnspecifiedDowntimes >= rep.ShiftMinutes ? ", простой целиком" : "";
            var reason = string.IsNullOrWhiteSpace(rep.DowntimeReason)
                ? "причина не указана"
                : $"причина «{rep.DowntimeReason.Trim()}»";
            var verdict = det.HardSignals.Any(s => s.StartsWith(tag)) ? " — вопрос ниже" : " — ок";
            lines.Add($"{tag} Простой {rep.FreshUnspecifiedDowntimes:0} мин ({ratio:0%}){idle}, {reason}{verdict}");
        }

        return lines;
    }

    /// <summary>
    /// Выжившие после слияния модельные вопросы (без детерминированных hard/soft):
    /// именно они попадают в общий Signals. Срезанные fuzzy-дедупом дубли из Signals
    /// тоже исчезают — иначе диалогу нечем их отфильтровать (Except по ShiftReportIssues
    /// их не находит) и они висят в «Данных».
    /// </summary>
    public static List<string> ModelSurvivors(ShiftReportRuleResult det, List<string> merged)
    {
        var detSet = det.HardSignals.Concat(det.SoftSignals)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. merged.Where(s => !detSet.Contains(s))];
    }

    /// <summary>
    /// Вердикт модели считается причиной по данным, только если она назвала проблемы
    /// по данным: сигналы — уже почищенные (эхо вынуты, галлюцинации срезаны).
    /// Голый requires_review без сигналов (все претензии — к отчёту) заголовок
    /// «Данных» не зажигает. Формула самой эскалации не меняется — только атрибуция.
    /// </summary>
    public static bool LlmDataCaused(bool requiresReview, IReadOnlyList<string> cleanedSignals) =>
        requiresReview && cleanedSignals.Count > 0;

    /// <summary>
    /// Кто стал причиной эскалации — данные записей или суточный отчёт.
    /// S2-soft в ShiftReportIssues эскалацию не форсирует, поэтому жёлтая подсветка
    /// UI-блоков идёт по этим флагам, а не по непустоте массивов. resetEffective=true
    /// (пост-фильтр всё снял) гасит обе причины. Вердикт модели входит как
    /// signals-backed (см. <see cref="LlmDataCaused"/>), а не голый.
    /// </summary>
    public static (bool Data, bool ShiftReport) EscalationCauses(
        HardRuleResult hardRules, List<string> notDowngraded,
        bool llmDataCaused, bool llmHasError, bool resetEffective,
        ShiftReportRuleResult shiftRules, List<string> modelShiftIssues)
    {
        if (resetEffective) return (false, false);
        var data = hardRules.MustEscalate || notDowngraded.Count > 0
            || llmDataCaused || llmHasError;
        var shift = shiftRules.MustEscalate || modelShiftIssues.Count > 0;
        return (data, shift);
    }

    /// <summary>
    /// Итоговая уверенность вердикта. При детерминированной эскалации (hard
    /// по данным или по отчёту мастера) и при сбое парсинга (HasError) решение
    /// принимает код, а не модель — уверенность 1.0, как в DegenerateEscalation:
    /// система уверена, что человеку надо посмотреть. Иначе — самооценка модели
    /// как пришла (soft notDowngraded и модельные S1-вопросы её не трогают).
    /// </summary>
    public static double ComputeConfidence(
        bool hardMustEscalate, bool shiftMustEscalate, bool hasError, double llmConfidence) =>
        (hardMustEscalate || shiftMustEscalate || hasError) ? 1.0 : llmConfidence;
}
