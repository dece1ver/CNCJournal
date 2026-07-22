using AiService.Models;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace AiService.Services;

public record PromptBuildResult(string Prompt, string Version);

public class PromptBuilder
{
    private const string PromptsSubfolder = "prompts";
    private const string DefaultSystemPromptFile = "system_prompt.txt";
    private const string SoftSignalPromptFile = "soft_signal_explanation.txt";
    private const string MasterCheckPromptFile = "master_check.txt";

    private static readonly string AssemblyLocation = Assembly.GetExecutingAssembly().Location;
    private static readonly string? AssemblyDir = Path.GetDirectoryName(AssemblyLocation);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger<PromptBuilder> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, CachedPrompt> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CachedPrompt
    {
        public string Content = "";
        public string Version = "unknown";
        public bool Exists;
        public DateTime WriteTimeUtc;
        public DateTime LastCheckUtc = DateTime.MinValue;
    }

    public PromptBuilder(ILogger<PromptBuilder> logger)
    {
        _logger = logger;
        // Прогрев: базовые промпты обязаны существовать (файл или embedded resource),
        // иначе падаем при старте, а не на первом запросе.
        var (_, _, defaultExists) = GetPrompt(DefaultSystemPromptFile, allowEmbeddedFallback: true);
        if (!defaultExists)
            throw new FileNotFoundException($"Не найден базовый промпт '{DefaultSystemPromptFile}' (ни внешний файл, ни embedded resource).");
        GetPrompt(SoftSignalPromptFile, allowEmbeddedFallback: true);
        GetPrompt(MasterCheckPromptFile, allowEmbeddedFallback: true);
    }

    /// <summary>
    /// Возвращает содержимое и версию промпта по имени файла с кэшированием и
    /// hot-reload (проверка изменений не чаще, чем раз в CheckInterval).
    /// </summary>
    private (string Content, string Version, bool Exists) GetPrompt(string fileName, bool allowEmbeddedFallback)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(fileName, out var entry))
            {
                entry = new CachedPrompt();
                _cache[fileName] = entry;
            }

            var now = DateTime.UtcNow;
            if ((now - entry.LastCheckUtc) >= CheckInterval)
            {
                entry.LastCheckUtc = now;
                RefreshEntry(fileName, entry, allowEmbeddedFallback);
            }

            return (entry.Content, entry.Version, entry.Exists);
        }
    }

    private void RefreshEntry(string fileName, CachedPrompt entry, bool allowEmbeddedFallback)
    {
        var externalPath = Path.Combine(AssemblyDir ?? AppContext.BaseDirectory, PromptsSubfolder, fileName);

        if (File.Exists(externalPath))
        {
            var writeTimeUtc = File.GetLastWriteTimeUtc(externalPath);
            if (entry.Exists && writeTimeUtc == entry.WriteTimeUtc) return;

            var raw = File.ReadAllText(externalPath);
            var stripped = StripVersionLine(raw);
            entry.Content = stripped?.content ?? raw;
            entry.Version = stripped?.version ?? writeTimeUtc.ToString("yyyy-MM-dd-HH:mm");
            entry.WriteTimeUtc = writeTimeUtc;
            entry.Exists = true;
            _logger.LogInformation("Промпт '{FileName}' version {Version} загружен из файла: {Path}",
                fileName, entry.Version, externalPath);
            return;
        }

        if (entry.Exists && !allowEmbeddedFallback)
        {
            // Файл профиля удалили на лету — перестаём его использовать.
            entry.Exists = false;
            entry.Content = "";
            _logger.LogWarning("Промпт '{FileName}' исчез с диска — будет использоваться базовый", fileName);
            return;
        }

        if (entry.Exists || !allowEmbeddedFallback) return;

        var resourceName = $"AiService.prompts.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null) return;

        using var reader = new StreamReader(stream);
        var embeddedRaw = reader.ReadToEnd();
        var embeddedStripped = StripVersionLine(embeddedRaw);
        entry.Content = embeddedStripped?.content ?? embeddedRaw;
        entry.Version = embeddedStripped?.version ?? "unknown";
        entry.Exists = true;
        _logger.LogInformation("Промпт '{FileName}' version {Version} загружен из embedded resource",
            fileName, entry.Version);
    }

    private static (string content, string version)? StripVersionLine(string text)
    {
        var reader = new StringReader(text);
        var firstLine = reader.ReadLine();
        if (firstLine != null && firstLine.StartsWith(";; version:"))
        {
            var version = firstLine[";; version:".Length..].Trim();
            var rest = reader.ReadToEnd();
            return (rest, version);
        }
        return null;
    }

    /// <summary>
    /// Выбор системного промпта по профилю станка (cnc_machines.AiPromptProfile,
    /// передаётся клиентом в PromptProfile). Пустой/некорректный профиль или
    /// отсутствующий файл — базовый system_prompt.txt. Версия профильного промпта
    /// автоматически получает суффикс @профиль, чтобы в ai_day_reviews точность
    /// считалась раздельно по профилям.
    /// </summary>
    private (string Content, string Version) ResolveSystemPrompt(string? profile)
    {
        var normalized = NormalizeProfile(profile);
        if (normalized != null)
        {
            var (content, version, exists) = GetPrompt($"system_prompt.{normalized}.txt", allowEmbeddedFallback: false);
            if (exists)
                return (content, $"{version}@{normalized}");
            _logger.LogDebug("Файл промпта для профиля '{Profile}' не найден — используется базовый", normalized);
        }

        var (defContent, defVersion, _) = GetPrompt(DefaultSystemPromptFile, allowEmbeddedFallback: true);
        return (defContent, defVersion);
    }

    private static string? NormalizeProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return null;
        var trimmed = profile.Trim().ToLowerInvariant();
        return Regex.IsMatch(trimmed, @"^[a-z0-9_\-]{1,50}$") ? trimmed : null;
    }

    // ═══ История детали: показывается модели только когда есть заявление, которое
    // история может подтвердить или опровергнуть («Требования к заполнению и
    // контролю», п.4.3). Во всех остальных случаях история — источник галлюцинаций
    // («раньше наладка была, а сегодня б/н») и в промпт не попадает.
    //   «Освоение» — деталь впервые на станке; опровергается записью с FinishedCount>0.
    //   «Некорректные нормативы»/«Отсутствие нормативов» — «если деталь уже нормально
    //   выполнялась с этими нормативами — норматив корректен»: история опровергает.
    //   Свободный текст про освоение/УП/нормативы — та же логика по слабой ветке.
    private static readonly string[] HistorySensitiveReasons =
    [
        "Освоение",
        "Некорректные нормативы",
        "Отсутствие нормативов",
    ];

    private static bool HistoryIsRelevant(PartContext p)
    {
        foreach (var reason in HistorySensitiveReasons)
        {
            if (reason.Equals(p.MasterSetupComment?.Trim(), StringComparison.OrdinalIgnoreCase)
                || reason.Equals(p.MasterMachiningComment?.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var text = $"{p.MasterComment} {p.OperatorComment}".ToLowerInvariant();
        if (text.Contains("осво") || text.Contains("осваив")) return true;   // освоение / освоена / осваивает
        if (text.Contains("вперв") || text.Contains("первый раз")) return true;
        if (text.Contains("норматив") || text.Contains("некорректн")) return true;
        if (text.Contains("управляющ") || Regex.IsMatch(text, @"\bуп\b")) return true; // написание/правка УП
        return false;
    }

    public PromptBuildResult Build(AnalyzeRequest req, HardRuleResult hardRules)
    {
        var (systemPrompt, promptVersion) = ResolveSystemPrompt(req.PromptProfile);

        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);

        if (hardRules.SoftSignals.Count > 0)
        {
            var (softPrompt, _, softExists) = GetPrompt(SoftSignalPromptFile, allowEmbeddedFallback: true);
            if (softExists)
            {
                sb.AppendLine();
                sb.AppendLine(softPrompt);
            }
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
            AppendPartBlock(sb, p);

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
          "suggest_exclude_from_reports": ["PartName§SetupNumber§Order§Причина для деталей с адекватно объяснённой разовой проблемой ИЛИ подтверждённым «Освоением» с КПД наладки < 100% — если таких нет, пустой массив. Причина — 3-10 слов КОНКРЕТНО про эту деталь, НЕ общий вывод по суткам; для освоения укажи, что КПД ниже 100% может негативно повлиять на К1 оператора"],
          "explanation": "1-2 предложения — ОБЯЗАТЕЛЬНОЕ непустое поле",
          "suggested_reason": "краткая причина в 3-7 слов — ОБЯЗАТЕЛЬНОЕ непустое поле"
        }
        """);

        return new PromptBuildResult(sb.ToString(), promptVersion);
    }

    /// <summary>
    /// Компактный промпт проверки ОДНОЙ записи: релевантно ли комментарии мастера
    /// объясняют присланные клиентом аномалии. Использует тот же формат блока
    /// детали, что и полный анализ (AppendPartBlock), — модели знаком формат,
    /// а форматы не разъезжаются.
    /// </summary>
    public PromptBuildResult BuildMasterCheck(VerifyPartRequest req)
    {
        var (systemPrompt, promptVersion, _) = GetPrompt(MasterCheckPromptFile, allowEmbeddedFallback: true);

        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);

        sb.AppendLine();
        sb.AppendLine($"Станок: {req.Machine}");
        sb.AppendLine($"Дата: {req.ShiftDate}");
        sb.AppendLine();

        AppendPartBlock(sb, req.Part);

        sb.AppendLine("Проверяемые аномалии:");
        foreach (var a in req.Anomalies)
        {
            var comment = FieldValue(req.Part, a.Field);
            sb.AppendLine($" • [{FieldTitle(a.Field)}] {a.Description} — объясняется комментарием: «{comment.Trim()}»");
        }

        sb.AppendLine();
        sb.AppendLine("Ответь СТРОГО в этом формате, без markdown, без преамбулы:");
        sb.AppendLine("""
        {
          "ok": true или false,
          "remark": "при ok=false — 1-2 предложения, ЧТО именно не объяснено и по какой аномалии; при ok=true пустая строка"
        }
        """);

        return new PromptBuildResult(sb.ToString(), promptVersion);
    }

    private static string FieldValue(PartContext p, string field) => field switch
    {
        "MasterSetupComment" => p.MasterSetupComment,
        "MasterMachiningComment" => p.MasterMachiningComment,
        "MasterComment" => p.MasterComment,
        "SpecifiedDowntimesComment" => p.SpecifiedDowntimesComment,
        _ => "",
    };

    private static string FieldTitle(string field) => field switch
    {
        "MasterSetupComment" => "причина наладки",
        "MasterMachiningComment" => "причина изготовления",
        "MasterComment" => "комментарий мастера",
        "SpecifiedDowntimesComment" => "комментарий к простоям",
        _ => field,
    };

    private static void AppendPartBlock(StringBuilder sb, PartContext p)
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
        if (p.PartsHistory is { RecordsFound: > 0 } ph && HistoryIsRelevant(p))
        {
            sb.AppendLine($"  История этой детали ({ph.RecordsFound} прошлых смен, от новых к старым):");
            foreach (var line in ph.Lines)
            {
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

                if (!string.IsNullOrWhiteSpace(line.AiExplanation)
                    && line.AnalystDecision == "escalated")
                {
                    sb.AppendLine($"      AI тогда: {line.AiExplanation}");
                }

                if (!string.IsNullOrWhiteSpace(line.AiFeedback)
                    && line.AnalystDecision == "escalated")
                {
                    sb.AppendLine($"      Коррекция аналитика: {line.AiFeedback}");
                }
            }
        }

        sb.AppendLine();
    }
}
