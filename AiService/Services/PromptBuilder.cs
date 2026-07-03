using AiService.Models;
using System.Reflection;
using System.Text;

namespace AiService.Services;

public class PromptBuilder
{
    private const string PromptsSubfolder = "prompts";

    private static readonly string AssemblyLocation = Assembly.GetExecutingAssembly().Location;
    private static readonly string? AssemblyDir = Path.GetDirectoryName(AssemblyLocation);

    private readonly string _systemPrompt;
    private readonly string _softSignalExplanation;
    private readonly ILogger<PromptBuilder> _logger;

    public string PromptVersion { get; }

    public PromptBuilder(ILogger<PromptBuilder> logger)
    {
        _logger = logger;

        var (sysPrompt, sysVersion) = LoadPrompt("system_prompt.txt");
        _systemPrompt = sysPrompt;

        var (softPrompt, _) = LoadPrompt("soft_signal_explanation.txt");
        _softSignalExplanation = softPrompt;

        PromptVersion = sysVersion;
    }

    private (string, string) LoadPrompt(string fileName)
    {
        var externalPath = Path.Combine(AssemblyDir ?? AppContext.BaseDirectory, PromptsSubfolder, fileName);

        if (File.Exists(externalPath))
        {
            var fileRaw = File.ReadAllText(externalPath);
            var fileStripped = StripVersionLine(fileRaw);
            var fileContent = fileStripped?.content ?? fileRaw;
            var fileVersion = fileStripped?.version ?? File.GetLastWriteTime(externalPath).ToString("yyyy-MM-dd-HH:mm");
            _logger.LogInformation("Prompt '{FileName}' version {Version} loaded from external: {Path}",
                fileName, fileVersion, externalPath);
            return (fileContent, fileVersion);
        }

        var resourceName = $"AiService.prompts.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        var raw = reader.ReadToEnd();
        var stripped = StripVersionLine(raw);
        var content = stripped?.content ?? raw;
        var version = stripped?.version ?? "unknown";
        _logger.LogInformation("Prompt '{FileName}' version {Version} loaded from embedded resource",
            fileName, version);
        return (content, version);
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

    public string Build(AnalyzeRequest req, HardRuleResult hardRules)
    {
        var sb = new StringBuilder();
        sb.AppendLine(_systemPrompt);

        if (hardRules.SoftSignals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(_softSignalExplanation);
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
