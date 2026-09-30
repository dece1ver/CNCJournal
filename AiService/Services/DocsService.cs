using System.Text.RegularExpressions;

namespace AiService.Services;

/// <summary> Один .md-документ из каталога. </summary>
public record DocInfo(string Name, string Title, int Chars);

/// <summary> Попадание поиска: документ, раздел, позиция и фрагмент с контекстом. </summary>
public record DocHit(string Name, string Section, int OffsetChars, string Snippet);

/// <summary>
/// Рабочие документы для чата-ДОЗОРа (регламенты, инструкции): .md-файлы каталога
/// Docs:Directory (по умолчанию docs рядом с сервисом — едет паблишем).
/// Без каталога/файлов — честная пустота, цикл не падает.
/// Перечитывает файлы, если изменились на диске (проверка не чаще 30 с).
/// </summary>
public class DocsService(IConfiguration config, ILogger<DocsService> logger)
{
    private readonly object _lock = new();
    private Dictionary<string, string> _docs = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public string Directory =>
        config.GetValue("Docs:Directory", "docs") ?? "docs";

    private string ResolveDir()
    {
        var dir = Directory;
        if (!Path.IsPathRooted(dir))
            dir = Path.Combine(AppContext.BaseDirectory, dir);
        return dir;
    }

    private Dictionary<string, string> Load()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCheckUtc) < CheckInterval && _docs.Count > 0)
                return _docs;
            _lastCheckUtc = now;
            var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var dir = ResolveDir();
                if (!System.IO.Directory.Exists(dir))
                {
                    logger.LogWarning("Каталог документов {Dir} отсутствует — тулзы документов пустые", dir);
                    _docs = loaded;
                    return _docs;
                }
                foreach (var file in System.IO.Directory.GetFiles(dir, "*.md"))
                {
                    try
                    {
                        loaded[Path.GetFileName(file)] = File.ReadAllText(file);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Не прочитался документ {File}", file);
                    }
                }
                logger.LogInformation("Документов загружено: {N} из {Dir}", loaded.Count, dir);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Каталог документов недоступен");
            }
            _docs = loaded;
            return _docs;
        }
    }

    /// <summary> Каталог: имя + первая строка-заголовок + объём. </summary>
    public IReadOnlyList<DocInfo> List(IReadOnlyList<string>? allowed = null)
    {
        var docs = Load();
        return [.. docs
            .Where(kv => Allowed(kv.Key, allowed))
            .Select(kv => new DocInfo(
                kv.Key, TitleOf(kv.Value) ?? kv.Key, kv.Value.Length))];
    }

    /// <summary> Фильтр корпуса: null — все документы (аналитика), список — только
    /// свои (оператору отдаём лишь инструкцию оператора).</summary>
    private static bool Allowed(string name, IReadOnlyList<string>? allowed) =>
        allowed is null || allowed.Any(a => name.Equals(a, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Поиск по текстам: слова запроса от 3 букв, score = разных слов + вхождения.
    /// Сопоставление с лёгким стеммингом: равенство/вхождение либо общий префикс
    /// ≥5 букв («простой» находит «простои», «отмечать» — «отметка»).
    /// Возвращает до maxHits фрагментов (~500 символов вокруг первого вхождения).
    /// </summary>
    public IReadOnlyList<DocHit> Search(string query, int maxHits = 3,
        IReadOnlyList<string>? allowed = null)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0) return [];
        var docs = Load();
        var scored = new List<(DocHit Hit, int Score)>();
        foreach (var (name, text) in docs)
        {
            if (!Allowed(name, allowed)) continue;
            var lower = text.ToLowerInvariant();
            var words = Tokenize(lower);
            var distinct = 0;
            var total = 0;
            var firstAt = int.MaxValue;
            foreach (var t in terms)
            {
                var count = 0;
                foreach (var w in words)
                {
                    if (!WordsMatch(w, t)) continue;
                    count++;
                    if (count == 1)
                    {
                        var at = lower.IndexOf(w, StringComparison.Ordinal);
                        if (at >= 0 && at < firstAt) firstAt = at;
                    }
                }
                if (count > 0) distinct++;
                total += count;
            }
            if (distinct == 0) continue;
            if (firstAt == int.MaxValue) firstAt = 0;
            var from = Math.Max(0, firstAt - 150);
            var len = Math.Min(500, text.Length - from);
            scored.Add((new DocHit(name, SectionAt(text, firstAt), from, text.Substring(from, len)), distinct * 1000 + total));
        }
        return [.. scored
            .OrderByDescending(s => s.Score)
            .Take(Math.Clamp(maxHits, 1, 10))
            .Select(s => s.Hit)];
    }

    public static bool WordsMatch(string word, string term) =>
        word.Contains(term, StringComparison.Ordinal)
        || (word.Length >= 5 && term.Contains(word, StringComparison.Ordinal))
        || CommonPrefix(word, term) >= 4;

    public static int CommonPrefix(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        return n;
    }

    /// <summary>
    /// Кусок документа с позиции (для дочитывания после поиска).
    /// Имя — точное, без .md или подстрока; length clamp 500–12000.
    /// </summary>
    public (string Name, string Text, int Total, bool Found) Read(
        string name, int offsetChars, int length, IReadOnlyList<string>? allowed = null)
    {
        var docs = Load();
        var key = docs.Keys.FirstOrDefault(k =>
                k.Equals(name, StringComparison.OrdinalIgnoreCase)
                || k.Equals(name + ".md", StringComparison.OrdinalIgnoreCase))
            ?? docs.Keys.FirstOrDefault(k =>
                k.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (key == null || !Allowed(key, allowed))
            return ("", "", 0, false);
        var text = docs[key];
        // Окно по умолчанию — 6000 симв.: это целиком раздел инструкции оператора
        // (порядок действий), а не обрывок в 500 символов, из которого модель
        // раньше додумывала процедуру.
        length = Math.Clamp(length <= 0 ? 6000 : length, 500, 12000);
        var from = Math.Clamp(offsetChars, 0, text.Length);
        var len = Math.Min(length, text.Length - from);
        return (key, text.Substring(from, len), text.Length, true);
    }

    public static string? TitleOf(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim().TrimStart('#').Trim();
            if (t.Length > 0) return t.Length <= 120 ? t : t[..120] + "…";
        }
        return null;
    }

    /// <summary>
    /// Раздел документа для позиции: ближайший выше заголовок (# или «N. ...»).
    /// Нужно, чтобы модель не выдавала процедуру мастера за процедуру оператора.
    /// </summary>
    public static string SectionAt(string text, int offset)
    {
        var section = "";
        var pos = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var title = MatchSectionTitle(line);
            if (title != null && pos <= offset)
                section = title;
            if (pos > offset) break;
            pos += rawLine.Length + 1;
        }
        return section;
    }

    internal static string? MatchSectionTitle(string line)
    {
        var m = Regex.Match(line, @"^(#{1,3}\s+|\d+\\?\.\s+)(.{3,100})");
        return m.Success ? m.Groups[2].Value.Trim() : null;
    }

    public static List<string> Tokenize(string query)
    {
        var words = new HashSet<string>();
        foreach (Match m in Regex.Matches(query.ToLowerInvariant(), @"[а-яa-zё0-9]{3,}"))
            words.Add(m.Value);
        return [.. words];
    }
}
