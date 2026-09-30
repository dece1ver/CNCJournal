using AiService.Models;
using System.Text.RegularExpressions;

namespace AiService.Services;

/// <summary>
/// Валидация причин в suggest_exclude_from_reports. Список кандидатов совещательный,
/// но галка по умолчанию стоит — выдуманное основание превращается в неверное
/// исключение из К1. Проверяются только триггерные формулировки с детерминированной
/// почвой (триггеры 3–4 промпта); свободный текст — на усмотрение аналитика.
/// </summary>
public static partial class ExcludeTriggerValidator
{
    private const string MasteringReason = "Освоение";
    private const string ApprenticeReason = "Работа ученика";

    /// <summary>
    /// Локальная копия правила remeLog.Core.Extensions.Strings
    /// (AiService не ссылается на remeLog.Core): скобочные комментарии,
    /// кавычки и лишние пробелы — вон, сравнение в нижнем регистре.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var cleaned = Parens().Replace(name, string.Empty);
        cleaned = cleaned.Replace("\"", "");
        return Spaces().Replace(cleaned, " ").ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Поиск строки суток для кандидата: сначала точное совпадение
    /// (имя + установка + заказ), затем нормализованное имя при единственном
    /// кандидате. Зеркалит AiExcludeSuggestion.FindRow на клиенте.
    /// </summary>
    public static PartContext? FindPart(
        List<PartContext> parts, string name, string setup, string order)
        => FindAllParts(parts, name, setup, order) is { Count: 1 } only ? only[0].Part : null;

    /// <summary>
    /// ВСЕ строки суток по ключу «имя§установка§заказ» с их номерами (1-based).
    /// Ключ не уникален внутри дня: одна и та же деталь/установка/заказ может быть
    /// разбита на несколько строк (ночная и дневная, повторная обработка) — тогда
    /// «первая по списку» не значит «проблемная» (кейс 30.09.2026, SKT21 25.09
    /// «Винт позиционирующий…»: строк две с одинаковым ключом, проблема во второй).
    /// </summary>
    public static List<(int Ordinal, PartContext Part)> FindAllParts(
        List<PartContext> parts, string name, string setup, string order)
    {
        var exact = parts
            .Select((p, i) => (Ordinal: i + 1, Part: p))
            .Where(x => x.Part.PartName == name
                && x.Part.Setup.ToString() == setup
                && x.Part.Order == order)
            .ToList();
        if (exact.Count > 0)
            return exact;

        var norm = NormalizeName(name);
        if (norm.Length == 0)
            return [];
        return parts
            .Select((p, i) => (Ordinal: i + 1, Part: p))
            .Where(x => x.Part.Setup.ToString() == setup
                && x.Part.Order == order
                && NormalizeName(x.Part.PartName) == norm)
            .ToList();
    }

    /// <summary> Хвост «§#N» — порядковый номер строки в сутках (для клиента). </summary>
    public static string WithOrdinal(string entry, int ordinal)
    {
        var seg = (entry ?? "").Split('§').ToList();
        while (seg.Count > 3 && OrdinalPattern().IsMatch(seg[^1].Trim()))
            seg.RemoveAt(seg.Count - 1);
        seg.Add("#" + ordinal);
        return string.Join('§', seg);
    }

    /// <summary> Причина из записи без хвоста «#N» (иначе номер попадёт в текст). </summary>
    public static string ReasonOf(string entry)
    {
        var seg = (entry ?? "").Split('§');
        var tail = seg.Length > 3 ? seg[3..] : [];
        if (tail.Length > 0 && OrdinalPattern().IsMatch(tail[^1].Trim()))
            tail = tail[..^1];
        return string.Join("§", tail).Trim();
    }

    /// <summary>
    /// Выбор строки среди одинаковых ключей по доказательствам, а не по порядку:
    /// причина grounded в текстах строки (4), клиентский сигнал на строке (2),
    /// «частичная» в причине при частичной наладке строки (2), КПД вне диапазона (1).
    /// 0 — строка не тянет запись (значит, запись уйдёт в непривязанные).
    /// </summary>
    public static int ExcludeRowScore(PartContext p, string reason)
    {
        var r = (reason ?? "").ToLowerInvariant();
        var score = 0;
        if (HasGroundedReason(p, reason) && HasGrounds(p, reason)) score += 4;
        if (p.Signals.Count > 0) score += 2;
        if (r.Contains("частичн") && p.PartialSetup > 0) score += 2;
        if (p.SetupRatio is < 0.7 or > 2.0) score += 1;
        return score;
    }

    /// <summary>
    /// Единственная строка-«нарушитель» среди совпавших по ключу. null — выбрать
    /// нельзя (ничья или ноль очков): вызывающий код не привязывает запись вообще,
    /// лучше кандидат без строки, чем галка на неверной строке.
    /// </summary>
    public static (int Ordinal, PartContext Part)? PickRowForExclude(
        List<(int Ordinal, PartContext Part)> candidates, string reason)
    {
        var scored = candidates
            .Select(c => (c.Ordinal, c.Part, Score: ExcludeRowScore(c.Part, reason)))
            .OrderByDescending(x => x.Score)
            .ToList();
        if (scored.Count == 0 || scored[0].Score <= 0) return null;
        if (scored.Count > 1 && scored[1].Score == scored[0].Score) return null;
        return (scored[0].Ordinal, scored[0].Part);
    }

    /// <summary>
    /// Основания триггеров: «освоение» — только подтверждённое (мастером по
    /// <see cref="MasteringAutoApprover.IsConfirmedMastering"/> либо оператором
    /// без опровержения историей); «работа ученика» — только при выбранной
    /// причине; «неопытный оператор» в триггер не входит никогда (регламент).
    /// </summary>
    public static bool HasGrounds(PartContext part, string reason)
    {
        var r = (reason ?? "").Trim().ToLowerInvariant();
        var claimsMastering = r.StartsWith("освоение");
        var claimsApprentice = r.Contains("ученик");
        var claimsInexperienced = r.Contains("неопытный");

        if (claimsMastering
            && !MasteringAutoApprover.IsConfirmedMastering(part)
            && !IsPlausibleOperatorMastering(part))
            return false;
        if (claimsApprentice && !IsApprenticeReason(part)) return false;
        if (claimsInexperienced && !claimsApprentice && !claimsMastering) return false;
        return true;
    }

    /// <summary>
    /// Форматная годность записи «PartName§Setup§Order§Причина»: сегментов ≥4,
    /// Setup — чистая цифра (модель пишет «Уст.1» — такая запись не привязывается
    /// к строке и обходит все проверки как «консервативно сохранённая»),
    /// причина — конкретика про деталь, а не голое название причины из списка
    /// («Другое», «Освоение» — формат нарушен, см. скелет v8).
    /// Короткие (<4 сегментов) записи не трогаем — legacy-мягкость FilterExcludeSuggestions.
    /// </summary>
    public static bool IsWellFormedExcludeEntry(string entry)
    {
        var seg = (entry ?? "").Split('§');
        if (seg.Length < 4) return true;
        if (!int.TryParse(seg[1].Trim(), out _)) return false;
        var reason = ReasonOf(entry);
        if (reason.Length == 0) return false;
        return !AgentTools.IsKnownReasonName(reason);
    }

    [GeneratedRegex(@"^#\d{1,3}$")]
    private static partial Regex OrdinalPattern();

    private static bool IsApprenticeReason(PartContext p) =>
        ApprenticeReason.Equals(p.MasterSetupComment?.Trim(), StringComparison.OrdinalIgnoreCase)
        || ApprenticeReason.Equals(p.MasterMachiningComment?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Причина опирается на тексты: значимые слова причины (≥4 букв) должны хотя бы
    /// наполовину встречаться в детализации/комментарии/названии детали. Ловит
    /// конфабуляцию модели («недостаток заготовки с внутренней резьбой» при вмятинах
    /// и реставрации в комментарии — пилот 25.09, QTS350 23.09): основание для
    /// исключения из К1 нельзя выдумывать, галка по умолчанию стоит.
    /// Паронимы («переточка» vs «заточка») проходят, пока половина слов на месте;
    /// короткие (<4 сегментов) записи не трогаем — их режет IsWellFormedExcludeEntry.
    /// </summary>
    public static bool HasGroundedReason(PartContext part, string reason)
    {
        var words = SignificantReasonWords(reason);
        if (words.Count == 0) return true;
        var sources = string.Join(" / ",
        [
            part.MasterSetupDetail,
            part.MasterMachiningDetail,
            part.MasterComment,
            part.OperatorComment,
            part.PartName,
        ]).ToLowerInvariant().Replace('ё', 'е');
        var hit = words.Count(w => sources.Contains(w));
        return hit * 2 >= words.Count;
    }

    private static List<string> SignificantReasonWords(string? reason) =>
        System.Text.RegularExpressions.Regex.Split(
                (reason ?? "").ToLowerInvariant().Replace('ё', 'е'), @"[^a-zа-я0-9]+")
            .Where(w => w.Length >= 4)
            .Distinct()
            .ToList();

    /// <summary>
    /// Слабая ветка освоения из промпта: оператор упоминает освоение/первый раз,
    /// а история деталь не опровергает (ни одной записи с выпуском).
    /// </summary>
    private static bool IsPlausibleOperatorMastering(PartContext p)
    {
        var op = (p.OperatorComment ?? "").ToLowerInvariant();
        if (!op.Contains("осво") && !op.Contains("впервые") && !op.Contains("первый раз"))
            return false;
        return p.PartsHistory == null || !p.PartsHistory.Lines.Any(l => l.FinishedCount > 0);
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parens();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
