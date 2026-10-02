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
    /// Возвращает до maxHits фрагментов (~700 символов вокруг совпадений,
    /// перекрывающиеся окна склеиваются).
    /// </summary>
    public IReadOnlyList<DocHit> Search(string query, int maxHits = 5,
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
            // Позиции вхождений всех совпавших слов. Раньше бралась только
            // первая, и на вопрос «почему не отмечается простой "Написание УП"»
            // выдавался обрывок из середины инструкции без нужного предложения
            // («Все простои кроме "Написание УП" могут быть отмечены и в
            // наладке, и в изготовлении. Данный простой только для этапа
            // наладки»), и модель честно отвечала «в документах не найдено».
            var positions = new List<int>();
            var matched = new List<string>();
            foreach (var t in terms)
            {
                var found = false;
                foreach (var w in words.Distinct())
                {
                    if (!WordsMatch(w, t)) continue;
                    found = true;
                    foreach (var at in AllIndexes(lower, w))
                        positions.Add(at);
                }
                if (found) matched.Add(t);
            }
            if (positions.Count == 0) continue;

            // Окна вокруг совпадений; перекрывающиеся склеиваем, чтобы не
            // отдать одни и те же строки дважды.
            const int Window = 700;
            var byStart = positions.OrderBy(x => x).Distinct()
                .Select(at => Math.Max(0, at - Window / 2))
                .ToList();
            var windows = new List<(int Start, int End)>();
            foreach (var start in byStart)
            {
                var end = Math.Min(text.Length, start + Window);
                if (windows.Count > 0 && start <= windows[^1].End)
                {
                    var last = windows[^1];
                    windows[^1] = (last.Start, Math.Max(last.End, end));
                }
                else
                {
                    windows.Add((start, end));
                }
            }

            // Ранжируем окна, а не документы: важно, сколько разных слов
            // запроса попало именно в этот кусок.
            foreach (var (start, end) in windows)
            {
                var slice = lower[start..end];
                var sliceWords = Tokenize(slice);
                var distinct = matched.Count(t => sliceWords.Any(w => WordsMatch(w, t)));
                if (distinct == 0) continue;
                var total = sliceWords.Count(w =>
                    matched.Any(t => WordsMatch(w, t)));
                scored.Add((new DocHit(name, SectionAt(text, start), start,
                    text.Substring(start, end - start)), distinct * 1000 + total));
            }
        }
        return [.. scored
            .OrderByDescending(s => s.Score)
            .Take(Math.Clamp(maxHits, 1, 10))
            .Select(s => s.Hit)];
    }

    /// <summary>Все позиции вхождения слова в тексте.</summary>
    private static IEnumerable<int> AllIndexes(string haystack, string needle)
    {
        if (needle.Length == 0) yield break;
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);
        var guard = 0;
        while (at >= 0 && guard++ < 40)
        {
            yield return at;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
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
