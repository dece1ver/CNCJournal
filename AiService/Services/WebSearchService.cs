using AiService.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiService.Services;

/// <summary> Один результат веб-поиска для инъекции в контекст чата. </summary>
public record WebSearchResult(string Title, string Url, string Snippet);

/// <summary>
/// Поиск в сети для производственного чата. Локальная qwen3:14b через Ollama
/// в интернет НЕ ходит — свежее (цены, документация, новинки) модель берёт
/// только отсюда: сервис ходит в поисковик по HTTP, сниппеты кладутся в промпт,
/// модель отвечает строго по ним и прикладывает ссылки.
///
/// Fail-open: любая ошибка (нет сети в цеху, упал SearXNG, таймаут) → пустой
/// список + warning в лог. Чат при этом работает как обычно, без пометки WebUsed.
/// Ключ API — только из конфига/env (WebSearch:ApiKey или WEBSEARCH_API_KEY),
/// в логи и промпты не попадает.
/// </summary>
public class WebSearchService(IConfiguration config, ILogger<WebSearchService> logger)
{
    private readonly HttpClient _http = new();

    public bool Enabled => config.GetValue("WebSearch:Enabled", false);

    private string Provider => config.GetValue("WebSearch:Provider", "searxng") ?? "searxng";
    private int TimeoutSeconds => config.GetValue("WebSearch:TimeoutSeconds", 10);
    private int MaxResults => config.GetValue("WebSearch:MaxResults", 5);

    /// <summary>
    /// Эвристика «вопросу нужен свежий/внешний факт»: явные маркеры поиска
    /// и темы, которых заведомо нет в весах (цены, новинки, годы).
    /// Даже без галки клиента сервер поищет сам, если поиск включён в конфиге.
    /// </summary>
    public static bool NeedsWeb(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        string[] markers =
        [
            "найди", "поищи", "погугли", "в интернете", "в сети", "в инете",
            "цена", "цены", "цену", "стоимость", "сколько стоит", "стоит", "стоят",
            "почём", "почем ", "купить", "поставщик",
            "курс", "доллар", "евро", "валют", "бирж", "котировк", "центробанк", "цб рф",
            "погода", "прогноз погоды",
            "документац", "паспорт", "руководство", "инструкц",
            "производитель", "официальн", "сайт",
            "новый", "новинка", "обзор", "сравн", "аналог", "замена",
            "гост", "iso", "актуальн", "что нового",
        ];
        return markers.Any(m => t.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>
    /// Нужен ли пред-поиск (до модели) для этого вопроса. Отдельная чистая
    /// функция, чтобы правило проверялось тестом: главное в ней — отказ от
    /// поиска по служебному триггеру автоприветствия.
    /// </summary>
    public static bool ShouldPreSearch(
        string query, bool allowWebSearch, bool isAutostart)
    {
        // Приветствие собирается из инструментов и снимка смены, а сеть в нём
        // не нужна. Без этой проверки пред-поиск срабатывал на каждом открытии
        // чата (см. NeedsWeb: год в «наладка с 26.05.2026» ловился как «2026»),
        // и оператор видел в приветствии пять ссылок на CRM-сервисы.
        if (isAutostart) return false;
        return allowWebSearch || NeedsWeb(query);
    }

    /// <summary>
    /// Поправка модели оператором («наврал», «неправда», «ты ошибся»): это про НАШИ
    /// данные, сеть тут не поможет, а мусорные сниппеты уводят ответ в сторону
    /// (кейс «смена не запущена, всё наврал» → инструкции по сломанному станку).
    /// Пред-поиск для таких вопросов пропускается (см. ChatController),
    /// модель исправляется инструментами по протоколу из системного промпта.
    /// </summary>
    public static bool IsCorrection(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        string[] markers =
        [
            "наврал", "наврала", "врёшь", "врешь", "враньё", "вранье",
            "неправда", "не правда", "неправильно", "не правильно", "неверно",
            "ты ошиб", "я не так", "всё выдумал", "все выдумал", "выдумал",
            "это не так", "совсем не так", "мимо",
        ];
        return markers.Any(m => t.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>
    /// Вопрос про наличие на складе («есть ли», «остаток», «наличие»): пред-поиск
    /// по нему даёт ссылки магазинов, по которым модель заявляет «на складе есть»,
    /// не вызвав stock_search (кейс AEKR-D2.25X25: в нашей выгрузке его нет вообще).
    /// Наличие — только из тула, сеть тут не источник. Пропускается (см. ChatController).
    /// </summary>
    public static bool IsStockQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        string[] markers =
        [
            "есть ли", " есть", "имеется", "на складе", "наличие", "остаток",
            "осталось", "остались", "в наличии", "что есть из", "что есть на",
        ];
        return markers.Any(m => t.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>
    /// Личный вопрос смены: про себя/смену («я», «меня», «моя» + смена/деталь/
    /// станок/работал/зовут/оператор). Такие вопросы решаются ТОЛЬКО инструментами;
    /// пред-поиск по ним даёт мусор, по которому модель отвечает вместо вызова
    /// тулзов (кейсы «прошлый вторник» по поиску, «дни работы» → калькуляторы
    /// рабочих дней из сети). Пред-поиск пропускается (см. ChatController).
    /// </summary>
    public static bool IsShiftPersonal(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        // Нормализация: пунктуация в пробелы («а я?» → «а я »), иначе короткие
        // слова-маркеры не находятся на границах.
        var norm = " " + Regex.Replace(
            text.ToLowerInvariant(), @"[^а-яa-zё0-9]+", " ") + " ";
        string[] self =
        [
            " я ", " меня ", " мне ", " мной ", " мною ", " моя ", " моё ", " мое ",
            " мой ", " мои ", " моих ", " мы ", " наша ", " наше ", " наши ",
        ];
        var t = text.ToLowerInvariant();
        string[] shift =
        [
            "смен", "детал", "просто", "станок", "станке", "станка",
            "работал", "работаю", "делал", "делаю", "делает", "зовут",
            "оператор", "цех", "цеху", "мастер", "квалификац", "разряд",
            "наладк", "журнал", "запис",
        ];
        return self.Any(s => norm.Contains(s, StringComparison.Ordinal))
            && shift.Any(s => t.Contains(s, StringComparison.Ordinal));
    }

    /// <summary>
    /// Запрос для поисковика по диалогу: короткие уточнения («а так?», «почему?»,
    /// «а подробнее?») сами по себе ищут мусор (кейс «а так?» → пунктуация вместо
    /// курса доллара). Идём назад по вопросам пользователя до первого содержательного
    /// (≥ ShortQueryChars) и склеиваем весь короткий хвост с ним: смысл уточнений
    /// только в нём («а я?» + «поподробней» ищутся как «а я? поподробней», а не как
    /// «поподробней» про орфографию).
    /// </summary>
    public static string BuildSearchQuery(ChatRequest request)
    {
        const int ShortQueryChars = 40;
        var users = (request.Messages ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Content)
                && !(m.Role ?? "").Trim().Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Content.Trim())
            .ToList();
        if (users.Count == 0) return "";
        var tail = new List<string> { users[^1] };
        for (var i = users.Count - 2; i >= 0 && tail[^1].Length < ShortQueryChars; i--)
            tail.Add(users[i]);
        tail.Reverse();
        return string.Join(" ", tail);
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(query)) return [];
        if (query.Length > 300) query = query[..300];

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var results = Provider.ToLowerInvariant() switch
            {
                "brave" => await SearchBraveAsync(query, cts.Token),
                "duckduckgo" => await SearchDuckDuckGoAsync(query, cts.Token),
                _ => await SearchSearXngAsync(query, cts.Token),
            };
            return FilterByDomains(results.Take(MaxResults).ToList());
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Веб-поиск: таймаут {Timeout}с для «{Query}»: {Msg}",
                TimeoutSeconds, Trunc(query), ex.Message);
            return [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Веб-поиск недоступен для «{Query}» — чат без сети", Trunc(query));
            return [];
        }
    }

    // ── Провайдеры ──

    /// <summary> Self-hosted SearXNG — основной для цеховой сети.
    /// GET {BaseUrl}/search?q=...&amp;format=json&amp;language=ru </summary>
    private async Task<List<WebSearchResult>> SearchSearXngAsync(string query, CancellationToken ct)
    {
        var baseUrl = (config.GetValue<string>("WebSearch:BaseUrl") ?? "http://localhost:8888").TrimEnd('/');
        var url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json&language=ru";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = new List<WebSearchResult>();
        if (doc.RootElement.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in arr.EnumerateArray())
            {
                var title = Prop(r, "title");
                var link = Prop(r, "url");
                var snippet = Prop(r, "content");
                if (!string.IsNullOrWhiteSpace(link))
                    results.Add(new WebSearchResult(Trunc(title, 150), link, Trunc(snippet, 400)));
            }
        }
        logger.LogInformation("Веб-поиск (searxng): «{Query}» → {N}", Trunc(query), results.Count);
        return results;
    }

    /// <summary> Brave Search API: нужен ключ (WebSearch:ApiKey или env WEBSEARCH_API_KEY). </summary>
    private async Task<List<WebSearchResult>> SearchBraveAsync(string query, CancellationToken ct)
    {
        var key = config.GetValue<string>("WebSearch:ApiKey")
            ?? Environment.GetEnvironmentVariable("WEBSEARCH_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            logger.LogWarning("Веб-поиск (brave): нет ключа WebSearch:ApiKey — пропуск");
            return [];
        }
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={MaxResults}&search_lang=ru");
        req.Headers.Add("X-Subscription-Token", key);
        req.Headers.Add("Accept", "application/json");
        using var response = await _http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = new List<WebSearchResult>();
        if (doc.RootElement.TryGetProperty("web", out var web)
            && web.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in arr.EnumerateArray())
            {
                var link = Prop(r, "url");
                if (!string.IsNullOrWhiteSpace(link))
                    results.Add(new WebSearchResult(
                        Trunc(Prop(r, "title"), 150), link, Trunc(Prop(r, "description"), 400)));
            }
        }
        logger.LogInformation("Веб-поиск (brave): «{Query}» → {N}", Trunc(query), results.Count);
        return results;
    }

    /// <summary> DuckDuckGo Instant Answers — слабый (только мгновенные ответы,
    /// без полноценного поиска), оставлен как бесплатный фолбэк без ключей. </summary>
    private async Task<List<WebSearchResult>> SearchDuckDuckGoAsync(string query, CancellationToken ct)
    {
        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&lang=ru";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = new List<WebSearchResult>();
        var abs = Prop(doc.RootElement, "AbstractText");
        if (!string.IsNullOrWhiteSpace(abs))
            results.Add(new WebSearchResult(
                Trunc(Prop(doc.RootElement, "Heading"), 150),
                Prop(doc.RootElement, "AbstractURL"),
                Trunc(abs, 400)));
        if (doc.RootElement.TryGetProperty("RelatedTopics", out var topics)
            && topics.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in topics.EnumerateArray())
            {
                if (!t.TryGetProperty("Text", out _)) continue; // вложенные категории пропускаем
                var link = Prop(t, "FirstURL");
                if (!string.IsNullOrWhiteSpace(link))
                    results.Add(new WebSearchResult("", link, Trunc(Prop(t, "Text"), 400)));
                if (results.Count >= MaxResults) break;
            }
        }
        logger.LogInformation("Веб-поиск (duckduckgo): «{Query}» → {N}", Trunc(query), results.Count);
        return results;
    }

    // ── Вспомогательное ──

    private List<WebSearchResult> FilterByDomains(List<WebSearchResult> results)
    {
        var allowed = config.GetSection("WebSearch:AllowDomains").Get<string[]>();
        if (allowed == null || allowed.Length == 0) return results;
        var set = allowed.Select(d => d.Trim().ToLowerInvariant()).ToHashSet();
        return results.Where(r =>
        {
            if (!Uri.TryCreate(r.Url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.ToLowerInvariant();
            return set.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
        }).ToList();
    }

    private static string Prop(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static string Trunc(string s, int max = 80) =>
        s.Length <= max ? s : s[..max] + "…";
}
