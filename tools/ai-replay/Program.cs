using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// ═══ AiReplay — offline-прогон ИИ-анализа на исторических данных ═══
// Читает request-логи AiService, повторно отправляет запросы в сервис
// (опционально с другой моделью/профилем промпта) и сверяет результат с
// решениями аналитиков из ai_day_reviews. Точность считается идентично
// «точность по промту.sql»: совпадение / AI пропустил / AI лишний флаг.
//
// Запуск: dotnet run -- --log-dir <папка> [опции], см. PrintUsage ниже.

var options = CliOptions.Parse(args);
if (options == null) { CliOptions.PrintUsage(); return 1; }

Console.OutputEncoding = Encoding.UTF8;

// ── 1. Сбор запросов из логов ────────────────────────────────────────────────
Console.WriteLine($"Чтение логов из {options.LogDir} ...");
var files = Directory.EnumerateFiles(options.LogDir, "*.json", SearchOption.AllDirectories).ToList();
if (files.Count == 0) { Console.Error.WriteLine($"В {options.LogDir} не найдено ни одного .json"); return 1; }

// На один (станок, дата) может быть несколько логов — берём самый свежий запрос.
var latest = new Dictionary<string, LogEntry>();
foreach (var path in files)
{
    JsonNode? doc;
    try { doc = JsonNode.Parse(File.ReadAllText(path)); }
    catch { Console.WriteLine($"Пропуск (не парсится): {Path.GetFileName(path)}"); continue; }

    var request = doc?["request"]?.AsObject();
    var machine = request?["machine"]?.GetValue<string>();
    var shiftDate = request?["shiftDate"]?.GetValue<string>();
    if (request == null || string.IsNullOrEmpty(machine) || string.IsNullOrEmpty(shiftDate)) continue;
    if (options.MachineFilter != null
        && !machine.Contains(options.MachineFilter, StringComparison.OrdinalIgnoreCase)) continue;

    var loggedAt = doc!["loggedAt"]?.GetValue<DateTime>() ?? File.GetLastWriteTime(path);
    var key = $"{machine}|{shiftDate}";
    if (latest.TryGetValue(key, out var existing) && existing.LoggedAt >= loggedAt) continue;

    latest[key] = new LogEntry(
        key, machine, shiftDate, loggedAt, request,
        BaselineRequiresReview: doc["response"]?["requiresReview"]?.GetValue<bool>(),
        BaselinePromptVersion: doc["promptVersion"]?.GetValue<string>());
}

var entries = latest.Values.OrderBy(e => e.Machine).ThenBy(e => e.ShiftDate).ToList();
if (options.Limit > 0) entries = entries.Take(options.Limit).ToList();
Console.WriteLine($"Уникальных сутко-станков: {entries.Count}");

// ── 2. Решения аналитиков ────────────────────────────────────────────────────
var labels = new Dictionary<string, AnalystLabel>();
if (options.LabelsCsv != null)
{
    foreach (var label in LabelSources.FromCsv(options.LabelsCsv))
        labels[label.Key] = label;
}
else if (options.ConnectionString != null)
{
    foreach (var label in await LabelSources.FromSqlAsync(options.ConnectionString))
        labels[label.Key] = label;
}
else
{
    Console.WriteLine("ВНИМАНИЕ: ни --connection, ни --labels-csv не заданы — точность посчитана не будет.");
}
Console.WriteLine($"Загружено решений аналитиков: {labels.Count}");

// ── 3. Повторный прогон ──────────────────────────────────────────────────────
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(600) };
var results = new List<ReplayRow>();
var i = 0;
foreach (var e in entries)
{
    i++;
    Console.Write($"[{i}/{entries.Count}] {e.Machine} {e.ShiftDate} ... ");

    // копия запроса, чтобы переопределения не накапливались
    var request = e.Request.DeepClone().AsObject();
    if (options.Model != null) request["model"] = options.Model;
    if (options.PromptProfile != null) request["promptProfile"] = options.PromptProfile;

    bool? replayRR = null;
    string? replayVersion = null, replayExplanation = null, replayError = null;
    try
    {
        using var response = await http.PostAsync(
            $"{options.Url}/api/analysis",
            new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        replayRR = body?["requiresReview"]?.GetValue<bool>();
        replayVersion = body?["promptVersion"]?.GetValue<string>();
        replayExplanation = body?["explanation"]?.GetValue<string>();
        replayError = body?["error"]?.GetValue<string>();
        Console.WriteLine($"requiresReview={replayRR}");
    }
    catch (Exception ex)
    {
        replayError = ex.Message;
        Console.WriteLine($"ОШИБКА: {replayError}");
    }

    labels.TryGetValue(e.Key, out var label);
    results.Add(new ReplayRow(
        e.Machine, e.ShiftDate,
        AnalystDecision: label?.Decision,
        BaselineRequiresReview: e.BaselineRequiresReview,
        BaselinePromptVersion: e.BaselinePromptVersion,
        BaselineVerdict: Verdicts.Get(label?.Decision, e.BaselineRequiresReview),
        ReplayRequiresReview: replayRR,
        ReplayPromptVersion: replayVersion,
        ReplayVerdict: Verdicts.Get(label?.Decision, replayRR),
        ReplayExplanation: replayExplanation,
        AnalystComment: label?.Comment,
        AnalystAiFeedback: label?.AiFeedback,
        Error: replayError));
}

// ── 4. Итоги ─────────────────────────────────────────────────────────────────
var outCsv = options.OutCsv ?? $"replay_results_{DateTime.Now:yyyyMMdd-HHmmss}.csv";
Csv.Write(outCsv, results);
Console.WriteLine($"\nРезультаты: {Path.GetFullPath(outCsv)}");

Console.WriteLine();
Verdicts.PrintSummary("Baseline (как было в проде)", results.Select(r => r.BaselineVerdict));
Verdicts.PrintSummary("Replay   (текущий прогон)  ", results.Select(r => r.ReplayVerdict));

// Расхождение с аналитиком ≠ автоматически ошибка ИИ: аналитик тоже может
// ошибаться, спорные строки разбираются вручную.
var adjudicate = results.Where(r => r.ReplayVerdict is not null and not Verdicts.Match).ToList();
if (adjudicate.Count > 0)
{
    Console.WriteLine($"\nРасхождения с аналитиком (проверить вручную, {adjudicate.Count} шт.):");
    foreach (var r in adjudicate)
        Console.WriteLine($"  {r.Machine} {r.ShiftDate}: аналитик={r.AnalystDecision}, replay={r.ReplayRequiresReview} → {r.ReplayVerdict}");
}

return 0;

// ═══ Типы и утилиты ══════════════════════════════════════════════════════════

record LogEntry(string Key, string Machine, string ShiftDate, DateTime LoggedAt,
    JsonObject Request, bool? BaselineRequiresReview, string? BaselinePromptVersion);

record AnalystLabel(string Key, string Decision, string? Comment, string? AiFeedback);

record ReplayRow(string Machine, string ShiftDate, string? AnalystDecision,
    bool? BaselineRequiresReview, string? BaselinePromptVersion, string? BaselineVerdict,
    bool? ReplayRequiresReview, string? ReplayPromptVersion, string? ReplayVerdict,
    string? ReplayExplanation, string? AnalystComment, string? AnalystAiFeedback, string? Error);

class CliOptions
{
    public string LogDir = "";
    public string Url = "http://localhost:5050";
    public string? Model, PromptProfile, ConnectionString, LabelsCsv, MachineFilter, OutCsv;
    public int Limit;

    public static CliOptions? Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => ++i < args.Length ? args[i]
                : throw new ArgumentException($"Не хватает значения для {args[i - 1]}");
            try
            {
                switch (args[i])
                {
                    case "--log-dir": o.LogDir = Next(); break;
                    case "--url": o.Url = Next().TrimEnd('/'); break;
                    case "--model": o.Model = Next(); break;
                    case "--profile": o.PromptProfile = Next(); break;
                    case "--connection": o.ConnectionString = Next(); break;
                    case "--labels-csv": o.LabelsCsv = Next(); break;
                    case "--machine": o.MachineFilter = Next(); break;
                    case "--limit": o.Limit = int.Parse(Next()); break;
                    case "--out": o.OutCsv = Next(); break;
                    default: Console.Error.WriteLine($"Неизвестный аргумент: {args[i]}"); return null;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return null; }
        }
        if (string.IsNullOrEmpty(o.LogDir)) { Console.Error.WriteLine("Обязателен --log-dir"); return null; }
        if (!Directory.Exists(o.LogDir)) { Console.Error.WriteLine($"Папка не найдена: {o.LogDir}"); return null; }
        return o;
    }

    public static void PrintUsage() => Console.WriteLine("""
        AiReplay — offline-прогон ИИ-анализа на исторических данных.

        dotnet run -- --log-dir <папка с request-логами AiService> [опции]

          --url <адрес>         AiService (по умолчанию http://localhost:5050)
          --model <модель>      переопределить модель Ollama (например qwen3:32b)
          --profile <профиль>   переопределить профиль промпта (prompts/system_prompt.<профиль>.txt)
          --connection <строка> строка подключения к базе stanki (решения аналитиков)
          --labels-csv <файл>   альтернатива SQL: CSV Machine,ShiftDate,Decision[,Comment,AiFeedback]
          --machine <подстрока> фильтр по имени станка
          --limit <N>           ограничить число прогоняемых дней
          --out <файл>          путь к итоговому CSV

        Примеры:
          dotnet run -- --log-dir \\aihost\AiService\request_logs --connection "Server=SQLSRV01;Database=stanki;Integrated Security=true;TrustServerCertificate=true"
          dotnet run -- --log-dir .\request_logs --labels-csv labels.csv --profile candidate
        """);
}

static class Verdicts
{
    public const string Match = "совпадение";
    public const string Missed = "AI пропустил";
    public const string FalseAlarm = "AI лишний флаг";

    public static string? Get(string? decision, bool? requiresReview) => (decision, requiresReview) switch
    {
        (null, _) or (_, null) => null,
        ("ok", false) or ("escalated", true) => Match,
        ("escalated", false) => Missed,
        ("ok", true) => FalseAlarm,
        _ => null,
    };

    public static void PrintSummary(string title, IEnumerable<string?> verdicts)
    {
        var scored = verdicts.Where(v => v != null).ToList();
        if (scored.Count == 0) return;
        var correct = scored.Count(v => v == Match);
        var missed = scored.Count(v => v == Missed);
        var falseAlarm = scored.Count(v => v == FalseAlarm);
        var acc = 100.0 * correct / scored.Count;
        Console.WriteLine($"{title}: N={scored.Count}  Correct={correct}  Missed={missed}  FalseAlarm={falseAlarm}  Accuracy={acc:F1}%");
    }
}

static class LabelSources
{
    public static async Task<List<AnalystLabel>> FromSqlAsync(string connectionString)
    {
        var labels = new List<AnalystLabel>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Machine, CONVERT(varchar(10), ShiftDate, 23) AS ShiftDate,
                   Decision, Comment, AiFeedback
            FROM ai_day_reviews
            WHERE IsFullyReviewed = 1
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var machine = reader.GetString(0);
            var shiftDate = reader.GetString(1);
            labels.Add(new AnalystLabel(
                $"{machine}|{shiftDate}",
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return labels;
    }

    public static List<AnalystLabel> FromCsv(string path)
    {
        var labels = new List<AnalystLabel>();
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return labels;

        var header = Csv.ParseLine(lines[0]);
        int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int machineCol = Col("Machine"), dateCol = Col("ShiftDate"), decisionCol = Col("Decision"),
            commentCol = Col("Comment"), feedbackCol = Col("AiFeedback");
        if (machineCol < 0 || dateCol < 0 || decisionCol < 0)
            throw new InvalidDataException("CSV должен содержать колонки Machine, ShiftDate, Decision");

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = Csv.ParseLine(line);
            string? Cell(int col) => col >= 0 && col < cells.Length && cells[col].Length > 0 ? cells[col] : null;
            var machine = Cell(machineCol); var date = Cell(dateCol); var decision = Cell(decisionCol);
            if (machine == null || date == null || decision == null) continue;
            labels.Add(new AnalystLabel($"{machine}|{date}", decision, Cell(commentCol), Cell(feedbackCol)));
        }
        return labels;
    }
}

static class Csv
{
    public static void Write(string path, List<ReplayRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Machine;ShiftDate;AnalystDecision;BaselineRequiresReview;BaselinePromptVersion;BaselineVerdict;" +
                      "ReplayRequiresReview;ReplayPromptVersion;ReplayVerdict;Changed;NeedsAdjudication;" +
                      "ReplayExplanation;AnalystComment;AnalystAiFeedback;Error");
        foreach (var r in rows)
        {
            var changed = r.BaselineRequiresReview.HasValue && r.ReplayRequiresReview.HasValue
                ? (r.BaselineRequiresReview != r.ReplayRequiresReview).ToString() : "";
            var needsAdjudication = r.ReplayVerdict is not null and not Verdicts.Match ? "1" : "0";
            sb.AppendLine(string.Join(';',
                new[]
                {
                    r.Machine, r.ShiftDate, r.AnalystDecision,
                    r.BaselineRequiresReview?.ToString(), r.BaselinePromptVersion, r.BaselineVerdict,
                    r.ReplayRequiresReview?.ToString(), r.ReplayPromptVersion, r.ReplayVerdict,
                    changed, needsAdjudication,
                    r.ReplayExplanation, r.AnalystComment, r.AnalystAiFeedback, r.Error,
                }.Select(Escape)));
        }
        // BOM — чтобы Excel корректно открыл кириллицу
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuotes = value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    public static string[] ParseLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c is ',' or ';') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return [.. cells.Select(s => s.Trim())];
    }
}
