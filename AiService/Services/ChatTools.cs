using AiService.Models;
using System.Text;
using System.Text.Json;

namespace AiService.Services;

/// <summary>
/// Инструменты чата-ДОЗОРа (eLog). В отличие от дневного анализа, модель НЕ получает
/// контекст смены в системном промпте: кто её оператор и что он делает, она узнаёт
/// САМА этими инструментами — только когда вопрос требует фактов (см. chat_system.txt).
/// Источник данных — снимок из ChatRequest (клиент присылает свежим каждый раз),
/// прямого доступа в БД у сервиса нет, как и в стадии 1 дневного агента.
/// Правило обрезки то же: каждый результат ≤ MaxResultChars.
/// </summary>
public static class ChatTools
{
    public const int MaxResultChars = 1500;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly IReadOnlyList<ChatTool> Definitions =
    [
        new ChatTool
        {
            Name = "get_shift_info",
            Description = "Кто твой оператор и на какой смене: станок, смена, ФИО оператора, "
                + "начата ли смена, сколько записей деталей. Вызывай, когда спрашивают про "
                + "«мою смену», оператора или станок. Общие вопросы — без вызова.",
            ParametersJson = """{"type":"object","properties":{}}""",
        },
        new ChatTool
        {
            Name = "get_active_part",
            Description = "Что оператор делает прямо сейчас: активная деталь (название, заказ, "
                + "установ, начало наладки, комментарий) и её простои. Вызывай, когда спрашивают "
                + "про «мою деталь», «мой простой», «что у меня / что я делаю».",
            ParametersJson = """{"type":"object","properties":{}}""",
        },
        new ChatTool
        {
            Name = "web_search",
            Description = "Поиск в сети В ХОДЕ диалога: уточнить или проверить факт (цены, "
                + "обозначения пластин, документация). Используй, когда приложенных результатов "
                + "поиска нет или они не про то — например, сам собрал обозначение по таблице "
                + "и хочешь проверить его перед ответом. Один вызов — один запрос.",
            ParametersJson = """{"type":"object","properties":{"query":{"type":"string","description":"Поисковый запрос, до 300 символов"}},"required":["query"]}""",
        },
        new ChatTool
        {
            Name = "get_operator_info",
            Description = "Найти оператора в базе (cnc_operators) по фамилии/инициалам "
                + "(берутся из контекста смены или вопроса: фамилия и первые буквы "
                + "имени и отчества): полные Фамилия Имя Отчество и разряд. Вызывай, "
                + "когда нужно обратиться к оператору по Имени Отчеству, а также при вопросах "
                + "про квалификацию/разряд оператора — разряд есть только в базе. "
                + "Без БД (тесты) инструмент честно отвечает пустотой.",
            ParametersJson = """{"type":"object","properties":{"name":{"type":"string","description":"Фамилия и инициалы из контекста смены или вопроса (первые буквы имени и отчества, НЕ буквы «И» «О»)"}},"required":["name"]}""",
        },
        new ChatTool
        {
            Name = "get_operator_history",
            Description = "Прошлые смены оператора из журнала (таблица Parts, новые сверху): "
                + "дата, станок, деталь, изготовлено штук. Вызывай при вопросах «что делал»"
                + ", «как работал раньше», «прошлые смены», «неделю назад». Без БД честно "
                + "отвечает пустотой — тогда скажи, что данных о прошлых сменах нет.",
            ParametersJson = """{"type":"object","properties":{"name":{"type":"string","description":"Фамилия и инициалы из контекста или вопроса (первые буквы имени и отчества)"},"days":{"type":"integer","description":"Глубина в днях, по умолчанию 30 (7-365)"},"date":{"type":"string","description":"Конкретный день YYYY-MM-DD, если спросили про день («во вторник», «12 сентября»); иначе пусто"}},"required":["name"]}""",
        },
        new ChatTool
        {
            Name = "list_docs",
            Description = "Какие рабочие документы доступны (регламенты, инструкции): "
                + "имена, заголовки, объём. Вызывай первым делом при вопросах про правила, "
                + "регламент, инструкции, «как правильно», «как положено».",
            ParametersJson = """{"type":"object","properties":{}}""",
        },
        new ChatTool
        {
            Name = "search_docs",
            Description = "Искать по текстам документов: возвращает фрагменты с именем "
                + "документа и позицией. Слова — в начальной форме (простой, деталь, "
                + "а не простоя, детали): поиск подстроковый, без стемминга. После — "
                + "при нужде read_doc для дочитывания.",
            ParametersJson = """{"type":"object","properties":{"query":{"type":"string","description":"Поисковые слова, например «отметка простоя мастер»"}},"required":["query"]}""",
        },
        new ChatTool
        {
            Name = "read_doc",
            Description = "Прочитать кусок документа с позиции (символы). Имя — из list_docs, "
                + "можно без .md. Для дочитывания найденного search_docs фрагмента: "
                + "length=6000 — это целый раздел (порядок действий), preview в 500 "
                + "символов из search_docs для ответа недостаточно.",
            ParametersJson = """{"type":"object","properties":{"name":{"type":"string","description":"Имя документа"},"offset":{"type":"integer","description":"Позиция в символах, по умолчанию 0"},"length":{"type":"integer","description":"Сколько символов, по умолчанию 6000 (500-12000)"}},"required":["name"]}""",
        },
        new ChatTool
        {
            Name = "stock_search",
            Description = "Остатки инструмента на складе по выгрузке (номенклатура + количество). "
                + "Вызывай при вопросах «есть ли», «сколько осталось», «что есть из». Слова — "
                + "в начальной форме, обозначения — как есть (880, D2500, CNMG). "
                + "Остатков цеха в данных НЕТ — цифры по цеху не выдумывать.",
            ParametersJson = """{"type":"object","properties":{"query":{"type":"string","description":"Что ищем, например «сверло 880 25»"}},"required":["query"]}""",
        },
        new ChatTool
        {
            Name = "check_db",
            Description = "Проверить доступ к базе данных: сколько строк видит сервис. "
                + "Вызывай при вопросах «есть доступ в бд», «видишь базу», «ты подключён». "
                + "Отвечай строго по результату: есть (со счётчиками) или нет (с причиной). "
                + "Отрицать доступ без вызова — запрещено.",
            ParametersJson = """{"type":"object","properties":{}}""",
        },
        new ChatTool
        {
            Name = "list_operator_downtime_types",
            Description = "Типы простоя, которые оператор отмечает у себя в ЭЖ: список "
                + "приходит из клиента eLog, это единственный источник. Вызывай, когда "
                + "оператор спрашивает, какой простой отметить. ВАЖНО: простой отмечают "
                + "только когда потребность возникла во время работы; если станок уже на "
                + "ремонте, ничего отмечать не надо. Мастерский справочник причин простоя "
                + "целой смены оператору недоступен, и его названия в ответе не упоминаются.",
            ParametersJson = """{"type":"object","properties":{}}""",
        },
        new ChatTool
        {
            Name = "get_shift_review",
            Description = "Проверка СМЕНЫ оператора: сколько минут из длительности смены "
                + "отмечено, сколько не отмечено, отмеченные простои по типам, частичная "
                + "наладка, незавершённые наладки — и что оператору нужно доделать, чтобы к "
                + "мастеру не было вопросов (пороги те же, что в суточном отчёте). Вызывай на "
                + "«замечания по смене», «всё ли правильно в смене», «что проверит мастер», "
                + "«что мне не хватает», «смена нормально закрыта?». Без указания даты — "
                + "сегодняшняя смена из контекста. Считает сервис по формулам отчёта мастера, "
                + "не выдумывай числа сам.",
            ParametersJson = """{"type":"object","properties":{"date":{"type":"string","description":"День YYYY-MM-DD, если спросили про конкретный день; иначе пусто"},"shift":{"type":"string","description":"«День» или «Ночь», если спросили явно; иначе пусто"}}}""",
        },
        new ChatTool
        {
            Name = "get_my_stats",
            Description = "Статистика оператора за период в цифрах журнала: смены, М/Л, "
                + "наладки, детали, время наладки/изготовления, простои по типам, КПД наладки "
                + "и изготовления, доля простоев, выполнены ли условия включения в расчёт "
                + "коэффициента. Считает сервис теми же формулами, что и отчёт мастера, по "
                + "данным только этого оператора. Вызывай на «статистика», «сколько я "
                + "наработал», «итоги за неделю/месяц», «какой у меня КПД». Период: days — "
                + "сколько дней смотреть назад (1-92), либо конкретные from/to "
                + "YYYY-MM-DD. Подбирай days по вопросу: «за неделю» — 7, «за месяц» — 30, "
                + "«за три месяца» — 90, «за год» нельзя (максимум 92). Без days "
                + "считается 30 — это месяц, поэтому «за неделю» без days даст неверный "
                + "период. Называй период так, как его посчитал инструмент. "
                + "Обязательный вызов: цифры без него выдумывать запрещено.",
            ParametersJson = """{"type":"object","properties":{"days":{"type":"integer","description":"Сколько дней смотреть назад, по умолчанию 30 (1-92)"},"from":{"type":"string","description":"Начало периода YYYY-MM-DD, если назвали дату; иначе пусто"},"to":{"type":"string","description":"Конец периода YYYY-MM-DD, иначе пусто"}}}""",
        },
    ];

    public static string Execute(ElogChatContext? ctx, string name, string argsJson)
    {
        var result = name switch
        {
            "get_shift_info" => ShiftInfo(ctx),
            "get_active_part" => ActivePart(ctx),
            "list_operator_downtime_types" => OperatorDowntimeTypes(ctx),
            _ => $"Неизвестный инструмент «{name}». Доступны: {string.Join(", ", Definitions.Select(d => d.Name))}.",
        };
        return result.Length <= MaxResultChars ? result : result[..MaxResultChars] + "…";
    }

    /// <summary>
    /// Типы простоя оператора — список приходит из клиента (eLog), это единственный
    /// источник: свой дублирующий список на сервере разошёлся бы с журналом при
    /// правке клиента. Причины простоя целой смены (справочник мастера) оператор не
    /// выбирает, поэтому сюда они не попадают.
    /// </summary>
    private static string OperatorDowntimeTypes(ElogChatContext? ctx)
    {
        var types = ctx?.OperatorDowntimeTypes ?? [];
        if (types.Count == 0)
            return "Список типов простоя от клиента не пришёл — значит, своих типов " +
                   "перечислить не могу, и выдумывать их запрещено. Скажи оператору, " +
                   "что тип простоя он отмечает у себя в ЭЖ по своей инструкции, и " +
                   "предложи уточнить ситуацию, чтобы подсказать по существу.";
        return "Типы простоя оператора в ЭЖ (закрытый список клиента, выбирай только " +
               "из него, ничего не добавляй):\n" +
               string.Join("\n", types.Where(t => !string.IsNullOrWhiteSpace(t))
                   .Select(t => $" • {t.Trim()}")) +
               "\nПравило ответа: простой отмечают только если потребность возникла " +
               "во время работы; если станок уже был сломан, когда оператор пришёл, " +
               "ничего отмечать не надо — это заполнит мастер. Для «Отказ оборудования» " +
               "и «Поиск и получение инструмента» по инструкции нужна дополнительная " +
               "информация (для склада и для технической службы соответственно) — " +
               "напомни про это.\nНе предлагай и не упоминай причины простоя целой " +
               "смены: у оператора их нет, и называть их в ответе нельзя.";
    }

    private static string ShiftInfo(ElogChatContext? ctx)
    {
        if (ctx == null)
            return "Контекст смены не прислан клиентом. Попроси оператора уточнить станок и смену вручную.";
        var payload = new
        {
            machine = ctx.Machine,
            shift = ctx.Shift,
            operatorName = ctx.OperatorName,
            shiftStarted = ctx.ShiftStarted,
            partsCount = ctx.PartsCount,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string ActivePart(ElogChatContext? ctx)
    {
        var part = ctx?.ActivePart;
        if (part == null || string.IsNullOrWhiteSpace(part.Name))
            return $"Активной детали нет: смена не начата или ни одна деталь сейчас " +
                   $"не в работе (в журнале записей: {ctx?.PartsCount ?? 0}). " +
                   "Так и скажи оператору прямо, предложи начать смену или запустить деталь. " +
                   "Записи журнала, которые не в работе, активной деталью не называй.";
        var sb = new StringBuilder();
        sb.AppendLine($"Деталь: «{part.Name}»" +
                      (string.IsNullOrWhiteSpace(part.Number) ? "" : $" ({part.Number})") +
                      (string.IsNullOrWhiteSpace(part.Order) ? "" : $" | М/Л «{part.Order}»") +
                      $" | уст. №{part.Setup}" +
                      (string.IsNullOrWhiteSpace(part.StartSetupTime) ? "" : $", наладка с {part.StartSetupTime}"));
        if (!string.IsNullOrWhiteSpace(part.OperatorComment))
            sb.AppendLine($"Комментарий оператора: «{part.OperatorComment.Trim()}»");
        if (part.Downtimes.Count > 0)
        {
            sb.AppendLine("Простои:");
            foreach (var d in part.Downtimes)
                sb.AppendLine($" • {d.Name} ({d.Start} → " +
                              $"{(string.IsNullOrWhiteSpace(d.End) ? "идёт сейчас" : d.End)})" +
                              (string.IsNullOrWhiteSpace(d.Comment) ? "" : $" — «{d.Comment.Trim()}»"));
        }
        else
        {
            sb.AppendLine("Простоев нет.");
        }
        return sb.ToString();
    }
}
