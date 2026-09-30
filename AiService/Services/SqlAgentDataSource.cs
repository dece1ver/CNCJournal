using AiService.Models;
using Microsoft.Data.SqlClient;

namespace AiService.Services;

// ═══ Источник стадии 2: прямые read-only запросы в SQL ═══
// Та же машина (см. AGENTS.md «Требование к серверу»). Строка подключения —
// только Database:ConnectionString прод-конфига, в репозитории пустая.
// Правила: только SELECT, все параметры — SqlParameter (динамического SQL нет,
// список дат для IN строится из параметров @d0…@dN — зеркало клиента),
// CommandTimeout из конфига. Любой сбой БД → null/пусто + warning в лог,
// цикл агента НЕ падает (fallback — данные запроса).

/// <summary> SQL-источник фактологии. Создаётся DI-фабрикой в Program.cs
/// только при непустой строке; иначе — RequestAgentDataSource. </summary>
public sealed class SqlAgentDataSource(IConfiguration config, ILogger<SqlAgentDataSource> logger) : IAgentDataSource
{
    private readonly string _connectionString =
        config.GetValue("Database:ConnectionString", "");
    private readonly int _timeoutSecs =
        Math.Clamp(config.GetValue("Database:CommandTimeoutSeconds", 15), 5, 120);
    private readonly int _maxRecords =
        Math.Clamp(config.GetValue("Agent:HistoryMaxRecords", 5), 1, 50);
    private readonly int _maxDaysBack =
        Math.Clamp(config.GetValue("Agent:HistoryMaxDaysBack", 720), 30, 3000);
    private readonly int _maxRows =
        Math.Clamp(config.GetValue("Database:MaxRows", 200), 10, 2000);
    private readonly int _operatorHistoryRows =
        Math.Clamp(config.GetValue("Chat:OperatorHistoryRows", 15), 1, 50);

    public async Task<PartsHistoryDto?> GetPartHistoryAsync(
        AnalyzeRequest request, string partName, int? setup, CancellationToken ct)
    {
        // Order нужен, чтобы исключить ТЕКУЩИЙ заказ (зеркало клиента: история —
        // только другие заказы). Берём из строк дня; нет совпадения — tool выше
        // уже ответил «нет в данных дня», сюда не дойдёт.
        var orders = request.Parts
            .Where(p => p.PartName.Trim().Equals(partName.Trim(), StringComparison.OrdinalIgnoreCase)
                && (!setup.HasValue || p.Setup == setup.Value))
            .Select(p => p.Order)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orders.Count == 0 || !TryParseDate(request.ShiftDate, out var beforeDate))
            return null;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            var rows = new List<HistoryRow>();
            foreach (var order in orders)
            {
                ct.ThrowIfCancellationRequested();
                rows.AddRange(await ReadOrderHistoryAsync(
                    connection, request.Machine, partName, order, setup, beforeDate, ct));
            }
            if (rows.Count == 0)
                return null;
            var top = rows
                .OrderByDescending(r => r.ShiftDate)
                .Take(_maxRecords)
                .ToList();
            var reviews = await ReadReviewsAsync(
                connection, request.Machine, top.Select(r => r.ShiftDate.Date).Distinct().ToList(), ct);
            return HistorySummarizer.Summarize(top, reviews);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "БД недоступна (история {Part}), fallback — данные запроса",
                partName);
            return null;
        }
    }

    public async Task<IReadOnlyList<OperatorInfo>> FindOperatorsAsync(string query, CancellationToken ct)
    {
        var parsed = OperatorQueryParser.Parse(query);
        if (parsed == null)
            return [];
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            const string sql =
                "SELECT TOP 20 FirstName, LastName, Patronymic, Qualification, IsActive " +
                "FROM cnc_operators " +
                "WHERE (LastName = @last OR LastName LIKE @lastPrefix) " +
                "  AND (@fi IS NULL OR FirstName LIKE @fi + '%') " +
                "  AND (@pi IS NULL OR Patronymic LIKE @pi + '%') " +
                "ORDER BY CASE WHEN LastName = @last THEN 0 ELSE 1 END, " +
                "  IsActive DESC, LastName, FirstName;";
            var result = new List<OperatorInfo>();
            await using var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = _timeoutSecs;
            cmd.Parameters.AddWithValue("@last", parsed.LastName);
            cmd.Parameters.AddWithValue("@lastPrefix", OperatorQueryParser.EscapeLike(parsed.LastName) + "%");
            cmd.Parameters.AddWithValue("@fi", (object?)parsed.FirstInitial ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pi", (object?)parsed.PatronymicInitial ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct) && result.Count < _maxRows)
            {
                result.Add(new OperatorInfo(
                    reader.GetString(1),
                    reader.GetString(0),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    !reader.IsDBNull(4) && reader.GetBoolean(4)));
            }
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "БД недоступна (оператор {Query}), отвечаем пустотой", query);
            return [];
        }
    }

    public async Task<OperatorHistoryDto?> GetOperatorHistoryAsync(
        string operatorQuery, int daysBack, DateOnly? date, CancellationToken ct)
    {
        // В Parts оператор — строкой «Фамилия Имя Отчество» (бывают короткие
        // «Фамилия Имя» и опечатки с двойным пробелом — нормализуем).
        // Фильтр по фамилии — в SQL (префикс), по инициалам — в коде:
        // LIKE по инициалам с точками ненадёжен.
        var parsed = OperatorQueryParser.Parse(operatorQuery);
        if (parsed == null)
            return null;
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            // Берём на одну строку больше лимита: так видно, что выдача усечена,
            // и это можно сказать модели прямо (иначе она принимает усечённый
            // список за полный и выводит «данных за эти дни нет»).
            // Конкретный день («во вторник», «12 сентября») — только его строки,
            // иначе окно назад. Про другие периоды модель не рассказывает.
            var sql = date.HasValue
                ? "SELECT TOP (@MaxRows) ShiftDate, Machine, PartName, [Order], Setup, " +
                  "FinishedCount, Operator " +
                  "FROM Parts " +
                  "WHERE Operator LIKE @like " +
                  "  AND ShiftDate = @Date " +
                  "ORDER BY StartSetupTime DESC;"
                : "SELECT TOP (@MaxRows) ShiftDate, Machine, PartName, [Order], Setup, " +
                  "FinishedCount, Operator " +
                  "FROM Parts " +
                  "WHERE Operator LIKE @like " +
                  "  AND ShiftDate >= @MinDate " +
                  "ORDER BY ShiftDate DESC, StartSetupTime DESC;";
            var all = new List<(string Stored, OperatorHistoryLine Line)>();
            await using var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = _timeoutSecs;
            cmd.Parameters.AddWithValue("@MaxRows", _operatorHistoryRows + 1);
            cmd.Parameters.AddWithValue("@like", OperatorQueryParser.EscapeLike(parsed.LastName) + "%");
            if (date.HasValue)
                cmd.Parameters.AddWithValue("@Date", date.Value.ToDateTime(TimeOnly.MinValue));
            else
                cmd.Parameters.AddWithValue("@MinDate", DateTime.Today.AddDays(-Math.Clamp(daysBack, 1, 365)));
            var truncated = false;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (all.Count >= _operatorHistoryRows)
                {
                    truncated = true;
                    break;
                }
                var stored = reader.IsDBNull(6) ? "" : reader.GetString(6);
                var line = new OperatorHistoryLine(
                    reader.GetDateTime(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Convert.ToInt32(reader.GetValue(4)),
                    reader.IsDBNull(5) ? 0 : reader.GetDouble(5));
                all.Add((stored, line));
            }
            // Строго по инициалам; если всё выкосило (мусорные инициалы в запросе),
            // отдаём однофамильцев с пометкой — лучше уточняющий вопрос, чем «нет данных».
            var strict = all
                .Where(x => OperatorInitialsMatch(x.Stored, parsed.FirstInitial, parsed.PatronymicInitial))
                .ToList();
            var approximate = strict.Count == 0 && all.Count > 0;
            var use = approximate ? all : strict;
            if (use.Count == 0)
                return null;
            return new OperatorHistoryDto(
                use[0].Stored switch { "" => operatorQuery.Trim(), var s => s },
                [.. use.Select(x => x.Line)],
                approximate,
                // Усечение — это свойство выборки по фамилии до сверки инициалов:
                // какая именно из отброшенных строк принадлежала оператору, здесь
                // не известно, поэтому честно говорим «записей больше».
                truncated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "БД недоступна (история {Query}), отвечаем пустотой", operatorQuery);
            return null;
        }
    }

    /// <summary>
    /// Сверка инициалов с хранимым «Фамилия Имя Отчество»: короткие формы
    /// («Фамилия Имя») проходят, только если отчество не требуется. Чистая функция.
    /// </summary>
    public static bool OperatorInitialsMatch(string stored, string? firstInitial, string? patronymicInitial)
    {
        var tokens = stored.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;
        if (firstInitial != null
            && (tokens.Length < 2 || !tokens[1].StartsWith(firstInitial, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (patronymicInitial != null
            && (tokens.Length < 3 || !tokens[2].StartsWith(patronymicInitial, StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    public async Task<DbStatusDto> GetDbStatusAsync(CancellationToken ct)
    {
        // Доказательство доступа цифрами: сколько строк видит сервис.
        // Любой сбой — Available=false с текстом, цикл не падает.
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var partsCmd = new SqlCommand("SELECT COUNT(*) FROM Parts", connection);
            partsCmd.CommandTimeout = _timeoutSecs;
            var parts = Convert.ToInt64(await partsCmd.ExecuteScalarAsync(ct));
            await using var opsCmd = new SqlCommand("SELECT COUNT(*) FROM cnc_operators", connection);
            opsCmd.CommandTimeout = _timeoutSecs;
            var ops = Convert.ToInt64(await opsCmd.ExecuteScalarAsync(ct));
            return new DbStatusDto(true, null, parts, ops);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "БД недоступна (проверка доступа)");
            return new DbStatusDto(false, ex.Message, 0, 0);
        }
    }

    // Чтения cnc_downtime_reasons здесь намеренно нет: справочник причин простоя
    // ЦЕЛОЙ смены нужен мастеру, а не оператору, и в чат оператора он не должен
    // попадать (см. IAgentDataSource).

    // Зеркало PartsDb.ReadPartsHistoryAsync: TOP N по (деталь, чужой заказ,
    // станок, установка, окно дат), сортировка ShiftDate/StartSetupTime DESC.
    // Колонки — только нужные свёртке (без SELECT *).
    private async Task<List<HistoryRow>> ReadOrderHistoryAsync(
        SqlConnection connection, string machine, string partName, string order,
        int? setup, DateTime beforeDate, CancellationToken ct)
    {
        const string sql =
            "SELECT TOP (@MaxRecords) ShiftDate, StartSetupTime, StartMachiningTime, " +
            "FinishedCount, SetupTimePlan, SetupTimeFact, SingleProductionTimePlan, " +
            "ProductionTimeFact, MachiningReasonOverride, MasterMachiningComment " +
            "FROM Parts " +
            "WHERE PartName = @PartName " +
            "  AND [Order] <> @Order " +
            "  AND Machine = @Machine " +
            "  AND (@Setup IS NULL OR Setup = @Setup) " +
            "  AND ShiftDate < @BeforeDate " +
            "  AND ShiftDate >= @MinDate " +
            "ORDER BY ShiftDate DESC, StartSetupTime DESC;";
        var rows = new List<HistoryRow>();
        await using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = _timeoutSecs;
        cmd.Parameters.AddWithValue("@MaxRecords", _maxRecords);
        cmd.Parameters.AddWithValue("@PartName", partName.Trim());
        cmd.Parameters.AddWithValue("@Order", order);
        cmd.Parameters.AddWithValue("@Machine", machine);
        cmd.Parameters.AddWithValue("@Setup", (object?)setup ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@BeforeDate", beforeDate.Date);
        cmd.Parameters.AddWithValue("@MinDate", beforeDate.Date.AddDays(-_maxDaysBack));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct) && rows.Count < _maxRows)
        {
            var shiftDate = reader.GetDateTime(0);
            var finished = reader.GetDouble(4);
            var setupPlan = reader.GetDouble(5);
            var setupFact = reader.GetDouble(6);
            var prodPlan = reader.GetDouble(7);
            var prodFact = reader.GetDouble(8);
            var separateStart = reader.GetDateTime(1) != reader.GetDateTime(2);
            // Эффективная причина изготовления: переопределение аналитика,
            // иначе отметка мастера (зеркало Part.EffectiveMachiningReason).
            var overrideName = reader.IsDBNull(9) ? "" : reader.GetString(9);
            var masterName = reader.IsDBNull(10) ? "" : reader.GetString(10);
            var reason = !string.IsNullOrWhiteSpace(overrideName) ? overrideName : masterName;
            rows.Add(new HistoryRow(
                shiftDate,
                finished,
                HistorySummarizer.SetupRatio(setupPlan, setupFact),
                HistorySummarizer.ProductionRatio(finished, separateStart, prodPlan, prodFact),
                reason));
        }
        return rows;
    }

    // Зеркало второй половины ReadPartsHistoryAsync: решения аналитиков
    // по датам (ai_day_reviews). Параметры дат — @d0…@dN, как у клиента.
    private async Task<Dictionary<DateTime, ReviewRow>> ReadReviewsAsync(
        SqlConnection connection, string machine, List<DateTime> dates, CancellationToken ct)
    {
        var result = new Dictionary<DateTime, ReviewRow>();
        var capped = dates.Take(60).ToList();
        if (capped.Count == 0)
            return result;
        var dateParams = capped.Select((_, i) => "@d" + i).ToArray();
        var sql =
            "SELECT ShiftDate, Decision, Comment, AiExplanation, AiFeedback " +
            "FROM ai_day_reviews " +
            "WHERE Machine = @Machine " +
            "  AND ShiftDate IN (" + string.Join(", ", dateParams) + ");";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = _timeoutSecs;
        cmd.Parameters.AddWithValue("@Machine", machine);
        for (var i = 0; i < capped.Count; i++)
            cmd.Parameters.AddWithValue(dateParams[i], capped[i]);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var date = reader.GetDateTime(0).Date;
            result[date] = new ReviewRow(
                date,
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        return result;
    }

    private static bool TryParseDate(string? s, out DateTime date) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);
}

/// <summary>
/// Разбор «Фамилия И.О.» в (фамилия + инициалы). Формы: «Крафт А.С.»,
/// «Крафт А С», «Крафт АС», «Крафт», а также полное ФИО «Крафт Алексей
/// Сергеевич» — модель передаёт его после get_operator_info. Null — фамилии
/// нет, искать нечего. Чистая функция — покрыта тестами.
/// </summary>
public static class OperatorQueryParser
{
    public record ParsedQuery(string LastName, string? FirstInitial, string? PatronymicInitial);

    public static ParsedQuery? Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;
        var tokens = query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;
        var last = tokens[0].Trim().Trim('.');
        if (last.Length == 0)
            return null;
        // Инициалы берём по СЛОВАМ, а не склейкой букв запроса: склейка давала
        // для «Крафт Алексей Сергеевич» инициалы А и «л» (вторая буква слова
        // «Алексей»), отчество не проходило сверку, строка отбрасывалась как
        // «однофамилец», и модель отвечала «данных нет» при существующей записи
        // (кейс «что делал Крафт во вторник»). Слово короче двух букв — это и
        // инициал, две буквы подряд — два инициала («АС»), длиннее — первая буква.
        var initials = new List<string>(2);
        foreach (var token in tokens.Skip(1))
        {
            var word = string.Concat(token.Where(char.IsLetter));
            if (word.Length == 0) continue;
            if (word.Length <= 2)
                initials.AddRange(word.Select(c => c.ToString()));
            else
                initials.Add(word[..1]);
            if (initials.Count >= 2) break;
        }
        return new ParsedQuery(
            last,
            initials.Count > 0 ? initials[0] : null,
            initials.Count > 1 ? initials[1] : null);
    }

    /// <summary> Экранирование спецсимволов LIKE ([, %, _). </summary>
    public static string EscapeLike(string s) =>
        s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
