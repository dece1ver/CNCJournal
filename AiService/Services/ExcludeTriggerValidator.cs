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

    private static bool IsApprenticeReason(PartContext p) =>
        ApprenticeReason.Equals(p.MasterSetupComment?.Trim(), StringComparison.OrdinalIgnoreCase)
        || ApprenticeReason.Equals(p.MasterMachiningComment?.Trim(), StringComparison.OrdinalIgnoreCase);

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
