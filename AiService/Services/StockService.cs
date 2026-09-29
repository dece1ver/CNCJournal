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
    /// Поиск по номенклатуре. Токенизация своя: слова от 2 букв + любые группы
    /// цифр («4», «25», «880» — ключевое в обозначениях).
    ///
    /// Обозначение важнее слов. Запрос вида «CNMG120408» или «AEKR-D2.25X25»
    /// сверяется по нормализованной строке (латиница и цифры слитно): точное
    /// совпадение идёт первым, а строки с ДРУГИМ обозначением («CNMG120404» на
    /// вопрос про 120408) не выдаются вовсе. Пока такие строки вылезали в выдачу
    /// по совпадению слов, оператор получал в ответ на «сколько CNMG120408?»
    /// чужую геометрию 04 вместо своей 08 (кейс кампании C.4). Возвращает до
    /// maxHits строк (включая нулевые остатки — «нет» тоже ответ).
    /// </summary>
    public IReadOnlyList<StockRow> Search(string query, int maxHits = 5)
    {
        Load();
        var terms = TokenizeStock(query);
        if (terms.Count == 0 || _rows.Count == 0) return [];
        // Обозначение из запроса: только латинские буквы и цифры, поэтому
        // «сверло Ø8» кодом не считается (русские слова выпадают), а «880-D2500»,
        // «CNMG 12 04 08», «AEKR-D2.25X25» — считаются.
        var code = QueryCode(query);
        var codeQuery = LooksLikeCode(code);
        // В вопросе есть числа (диаметр, размер, количество) — строка, где ни одно
        // из них не совпало, в выдачу не попадает: на «сверло Ø8» так отсеиваются
        // Ø9,8 и Ø6,8, которые держались на одном только слове «сверло»
        // (кейс кампании C.2). Для запросов без чисел (например, «сверло») фильтр
        // выключен — там слово и есть главный признак.
        var numericTerms = terms.Where(t => t.All(char.IsDigit)).ToList();
        var scored = new List<(StockRow Row, int Score)>();
        foreach (var row in _rows)
        {
            var words = TokenizeStock(row.Name);
            var distinct = 0;
            var total = 0;
            var numericMatched = 0;
            foreach (var t in terms)
            {
                var count = words.Count(w => StockWordsMatch(w, t));
                if (count > 0) distinct++;
                total += count;
                if (count > 0 && t.All(char.IsDigit)) numericMatched++;
            }
            var exact = codeQuery && NormalizeCode(row.Name).Contains(code, StringComparison.Ordinal);
            // Вопрос звучит как обозначение, а точного совпадения нет — честная
            // пустота, а не «похожие позиции» из чужих артикулов (кейс AEKR-D2.25X25
            // → 327R12-22 250VM-TH из-за совпадения по цифрам «25»).
            if (codeQuery && !exact) continue;
            if (!exact && numericTerms.Count > 0 && numericMatched == 0) continue;
            if (distinct == 0 && !exact) continue;
            // Точное совпадение обозначения приоритетнее любых совпадений слов.
            var bonus = exact ? 5000 : 0;
            scored.Add((row, distinct * 1000 + total + bonus));
        }
        return [.. scored
            .OrderByDescending(s => s.Score)
            .Take(Math.Clamp(maxHits, 1, 10))
            .Select(s => s.Row)];
    }

    /// <summary>
    /// Обозначение из запроса: латинские буквы и цифры, слитно, без разделителей
    /// («AEKR-D2.25X25» → «aekrd225x25», «сверло Ø8» → пусто). Русские слова
    /// отбрасываются, иначе обычный вопрос со словом «сверло» сошёл бы за код.
    /// </summary>
    public static string QueryCode(string query) =>
        new(string.Concat(CodeRunRx.Matches(query)
            .SelectMany(m => m.Value)
            .Select(char.ToLowerInvariant)
            .Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9')));

    /// <summary> Обозначение строки выгрузки: буквы и цифры, слитно. </summary>
    internal static string NormalizeCode(string text) =>
        new(string.Concat(text.ToLowerInvariant().Where(char.IsLetterOrDigit)));

    /// <summary> Похоже ли на обозначение: есть и латинские буквы, и цифры, 6+. </summary>
    internal static bool LooksLikeCode(string code) =>
        code.Length >= 6 && code.Any(c => c is >= 'a' and <= 'z') && code.Any(char.IsDigit);

    /// <summary>
    /// Совпадение слова с термом для склада. Отличие от документов в числах:
    ///  • «8,5» и «8.0» — ЦЕЛЫЕ токены, и «8» совпадает с ними по целой части
    ///    (номинал диаметра), но НЕ с «800»/«908» — иначе на вопрос «сверло Ø8»
    ///    в выдачу лезли Ø9,8 и Ø6,8 (кейс кампании C.2);
    ///  • смешанное слово (d2400l25) по-прежнему ищется подстрокой — так
    ///    находятся обозначения вида «880-D2400» по запросу «сверло 880 24»;
    ///  • буквенные термы — по общим правилам документов.
    /// </summary>
    public static bool StockWordsMatch(string word, string term)
    {
        if (!term.All(char.IsDigit))
            return DocsService.WordsMatch(word, term);
        if (word == term) return true;
        if (word.Any(char.IsLetter))
            return word.Contains(term, StringComparison.Ordinal);
        var head = word.Split(',', '.')[0];   // «9,8» → «9», «8,5» → «8»
        return head == term;
    }

    /// <summary> Фрагменты вида «CNMG120408», «880D2500» (2+ латинских символов/цифр). </summary>
    private static readonly Regex CodeRunRx = new(
        @"[A-Za-z0-9]{2,}", RegexOptions.Compiled);

    internal static List<string> TokenizeStock(string text)
    {
        var words = new HashSet<string>();
        // ё приводим к е: оператор пишет «свёрл», в выгрузке «Сверло» — иначе
        // слово не совпадает и тул отдавал метчики вместо свёрл (кейс C.2).
        // Дробные числа — целым токеном: «Ø9,8» это 9.8, а не «9» и «8».
        var normalized = text.ToLowerInvariant().Replace('ё', 'е');
        foreach (Match m in Regex.Matches(normalized, @"[а-яa-z]{2,}|\d+[.,]\d+|\d+"))
            words.Add(m.Value);
        return [.. words];
    }
}
