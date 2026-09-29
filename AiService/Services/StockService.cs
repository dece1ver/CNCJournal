using System.Text.RegularExpressions;

namespace AiService.Services;

/// <summary> Строка складской выгрузки: номенклатура и конечный остаток. </summary>
public record StockRow(string Articul, string Name, string Unit, double Qty);

/// <summary>
/// Остатки инструмента на складе: CSV-выгрузка ведомости (Stock:File, по умолчанию
/// docs/stock.csv рядом с сервисом — едет паблишем). Формат: первая строка-коммент
/// `# source=...; warehouse=...; asof=2025-07-17`, дальше header articul;name;unit;qty.
/// Без файла — честная пустота. Перечитывает при изменении на диске (не чаще 30 с).
/// Выгрузки по цеху нет — остатки цеха неизвестны, модель их не выдумывает.
/// </summary>
public class StockService(IConfiguration config, ILogger<StockService> logger)
{
    private readonly object _lock = new();
    private List<StockRow> _rows = [];
    private string _sourceInfo = "";
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public string FilePath =>
        config.GetValue("Stock:File", "docs/stock.csv") ?? "docs/stock.csv";

    public string SourceInfo
    {
        get { Load(); return _sourceInfo; }
    }

    public int RowCount
    {
        get { Load(); return _rows.Count; }
    }

    private string ResolveFile()
    {
        var file = FilePath;
        if (!Path.IsPathRooted(file))
            file = Path.Combine(AppContext.BaseDirectory, file);
        return file;
    }

    private void Load()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCheckUtc) < CheckInterval && _rows.Count > 0)
                return;
            _lastCheckUtc = now;
            var rows = new List<StockRow>();
            var info = "";
            try
            {
                var file = ResolveFile();
                if (!File.Exists(file))
                {
                    logger.LogWarning("Файл остатков {File} отсутствует — тул склада пустой", file);
                    _rows = rows;
                    _sourceInfo = info;
                    return;
                }
                var writeTime = File.GetLastWriteTimeUtc(file);
                if (writeTime == _lastWriteUtc && _rows.Count > 0)
                    return;
                _lastWriteUtc = writeTime;
                foreach (var line in File.ReadLines(file))
                {
                    if (line.StartsWith('#'))
                    {
                        if (string.IsNullOrEmpty(info)) info = line.TrimStart('#').Trim();
                        continue;
                    }
                    var parts = line.Split(';');
                    if (parts.Length < 4) continue;
                    if (parts[1].Trim().Equals("name", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!double.TryParse(parts[3].Trim().Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var qty))
                        continue;
                    rows.Add(new StockRow(
                        parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), qty));
                }
                logger.LogInformation("Остатки загружены: {N} строк ({Info})", rows.Count, info);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Файл остатков недоступен");
            }
            _rows = rows;
            _sourceInfo = info;
        }
    }

    /// <summary>
    /// Поиск по номенклатуре: те же правила, что в документах (подстроки + префикс ≥4).
    /// Токенизация своя: слова от 2 букв + любые группы цифр («4», «25», «880» —
    /// ключевое в обозначениях; общий Tokenize режет короче 3 символов).
    /// Возвращает до maxHits строк (включая нулевые остатки — «нет» тоже ответ).
    /// </summary>
    public IReadOnlyList<StockRow> Search(string query, int maxHits = 5)
    {
        Load();
        var terms = TokenizeStock(query);
        if (terms.Count == 0 || _rows.Count == 0) return [];
        var scored = new List<(StockRow Row, int Score)>();
        foreach (var row in _rows)
        {
            var words = TokenizeStock(row.Name);
            var distinct = 0;
            var total = 0;
            foreach (var t in terms)
            {
                var count = words.Count(w => DocsService.WordsMatch(w, t));
                if (count > 0) distinct++;
                total += count;
            }
            if (distinct == 0) continue;
            // Точное обозначение (880-D2500) весит больше общих слов (сверло).
            var bonus = terms.Any(t => t.Length >= 4 && row.Name.Contains(t, StringComparison.OrdinalIgnoreCase))
                ? 500 : 0;
            scored.Add((row, distinct * 1000 + total + bonus));
        }
        return [.. scored
            .OrderByDescending(s => s.Score)
            .Take(Math.Clamp(maxHits, 1, 10))
            .Select(s => s.Row)];
    }

    internal static List<string> TokenizeStock(string text)
    {
        var words = new HashSet<string>();
        foreach (Match m in Regex.Matches(text.ToLowerInvariant(), @"[а-яёa-z]{2,}|[0-9]+"))
            words.Add(m.Value);
        return [.. words];
    }
}
