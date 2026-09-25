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
    {
        var exact = parts.FirstOrDefault(p =>
            p.PartName == name && p.Setup.ToString() == setup && p.Order == order);
        if (exact != null) return exact;

        var norm = NormalizeName(name);
        if (norm.Length == 0) return null;
        var candidates = parts.Where(p =>
            p.Setup.ToString() == setup && p.Order == order
            && NormalizeName(p.PartName) == norm).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
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
        var reason = string.Join("§", seg.Skip(3)).Trim();
        if (reason.Length == 0) return false;
        return !AgentTools.IsKnownReasonName(reason);
    }

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
