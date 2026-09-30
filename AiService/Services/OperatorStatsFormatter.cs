using AiService.Models;

namespace AiService.Services;

// ═══ Тексты для ДОЗОра: числа и выводы считает сервис ═══
// Модель получает готовые числа и готовые пометки «что требует действий
// оператора». Её работа — пересказать это человеку, не пересчитывая ничего
// и не добавляя своих оценок.

public static class OperatorStatsFormatter
{
    /// <summary> Проверка смены оператора: что отмечено и что стоит доделать. </summary>
    public static string FormatShiftReview(ShiftReviewResult? review, string operatorName)
    {
        if (review == null)
            return "Данных по этой смене нет: в журнале нет ни одной записи оператора " +
                   $"{operatorName} за {(review is null ? "указанную дату" : "")}. Честно скажи " +
                   "оператору, что записей нет, и предложи начать смену или запустить деталь. " +
                   "Не выдумывай цифры.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Данные по смене полные, дополнять их нечем — отвечай по ним, " +
                      "не вызывай ради этого другие инструменты.");
        sb.AppendLine($"Смена {review.Date:dd.MM.yyyy} «{review.Shift}», станок {review.Machine}, " +
                      $"оператор {review.Operator}. Длительность смены: {review.ShiftMinutes} мин.");
        if (review.Records == 0)
        {
            sb.AppendLine("Записей в журнале за эту смену нет. Значит, время не отмечено: " +
                          "либо смена не начата, либо оператор отработал не всю смену. " +
                          "По инструкции, если оператор отработал смену частично (ремонт " +
                          "оборудования, отсутствие работы), можно ничего не отмечать — так и скажи.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"Отмечено времени: {review.MarkedMinutes:0} из {review.ShiftMinutes} мин. " +
                      $"Не отмечено: {review.UnmarkedMinutes:0} мин.");
        sb.AppendLine($"Отмеченные простои: {review.SpecifiedDowntimeMinutes:0} мин " +
                      $"({review.SpecifiedDowntimeRatio:0.#%} рабочего времени).");
        if (review.PartialSetupMinutes > 0)
            sb.AppendLine($"Частичная наладка: {review.PartialSetupMinutes:0} мин " +
                          $"({review.PartialSetupRatio:0.#%} длительности смены).");
        if (review.OpenRecords > 0)
            sb.AppendLine($"Незавершённых наладок/деталей: {review.OpenRecords}.");
        if (review.DowntimeRemarks.Count > 0)
            sb.AppendLine("Простои по типам: " + string.Join(", ",
                review.DowntimeRemarks.Select(d => $"{d.Type} {d.Minutes:0} мин")) + ".");

        sb.AppendLine();
        sb.AppendLine("ЧТО ТРЕБУЕТ ДЕЙСТВИЙ ОПЕРАТОРА (пороги те же, что у мастера):");
        var actions = new List<string>();
        var unmarkedRatio = review.ShiftMinutes > 0
            ? review.UnmarkedMinutes / review.ShiftMinutes : 0;
        if (unmarkedRatio > OperatorStatsService.UnmarkedAttentionRatio)
            actions.Add(
                $"Не отмечено {review.UnmarkedMinutes:0} мин ({unmarkedRatio:0.#%}) — больше 10%: " +
                "мастер обязан указать причину простоя, иначе запись не закроют. Закрой время " +
                "простоями или завершением наладки/изготовления. Если ты отработал не всю смену " +
                "(ремонт, отсутствие работы) — по инструкции можно не отмечать ничего, так и скажи.");
        if (review.SpecifiedDowntimeRatio > OperatorStatsService.DowntimeCommentRatio)
            actions.Add(
                $"Простои {review.SpecifiedDowntimeRatio:0.#%} рабочего времени — больше половины: " +
                "мастер обязан написать по ним комментарий. Проверь, не отмечено ли лишнего.");
        if (review.PartialSetupRatio > OperatorStatsService.PartialSetupAttentionRatio)
            actions.Add(
                $"Частичная наладка {review.PartialSetupRatio:0.#%} смены — больше 30%: мастер " +
                "попросит объяснить причину в комментарии.");
        foreach (var d in review.DowntimeRemarks.Where(d => d.CommentMissing))
            actions.Add(
                $"Простой «{d.Type}» ({d.Minutes:0} мин): по инструкции для такого простоя нужна " +
                "дополнительная информация — для склада или для технической службы.");
        foreach (var (part, description) in review.RowAnomalies.Take(8))
            actions.Add($"{part}: {description} — мастер потребует причину или комментарий.");
        if (review.RowAnomalies.Count > 8)
            actions.Add($"…и ещё строк: {review.RowAnomalies.Count - 8}.");
        if (actions.Count == 0)
            actions.Add("Явных проблем нет: время отмечено, простои в норме, мастеру нечего требовать.");
        foreach (var a in actions)
            sb.AppendLine(" • " + a);

        if (review.RowAnomalies.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ЗАМЕЧАНИЯ МАСТЕРА ПО СТРОКАМ (как он их увидит):");
            foreach (var (part, description) in review.RowAnomalies.Take(20))
                sb.AppendLine($" • {part}: {description}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary> Статистика за период — только числа отчёта, без оценок. </summary>
    public static string FormatPeriodStats(PeriodStatsResult? stats, string operatorName)
    {
        if (stats == null)
            return $"Данных по оператору {operatorName} за этот период в журнале нет. " +
                   "Так и скажи: записей нет, цифры выдумывать нельзя.";

        var sb = new System.Text.StringBuilder();
        var machine = stats.Machine is { Length: > 0 } m ? $", станок {m}" : "";
        sb.AppendLine($"Статистика по данным журнала: {stats.Operator}{machine}, " +
                      $"период {stats.From:dd.MM.yyyy} — {stats.To:dd.MM.yyyy} " +
                      $"({stats.WorkDays} рабочих дней).");
        sb.AppendLine($"Смен с записями: {stats.WorkedShifts}. М/Л: {stats.Orders}. " +
                      $"Наладок: {stats.Setups}. Деталей изготовлено: {stats.FinishedCount:0}.");
        sb.AppendLine($"Время: наладка {stats.SetupFactMinutes:0} мин, изготовление " +
                      $"{stats.ProductionFactMinutes:0} мин, отмеченные простои " +
                      $"{stats.SpecifiedDowntimeMinutes:0} мин " +
                      $"({stats.SpecifiedDowntimeRatio:0.#%} от времени работы).");
        sb.AppendLine($"КПД наладки: {Ratio(stats.SetupRatio)}.");
        sb.AppendLine($"КПД изготовления: {Ratio(stats.ProductionRatio)}.");
        if (stats.DowntimeByType.Count > 0)
            sb.AppendLine("Простои по типам, мин: " + string.Join(", ",
                stats.DowntimeByType.Select(d => $"{d.Type} {d.Minutes:0}")));
        sb.AppendLine($"Условия включения в расчёт коэффициента: смен с записями " +
                      $"{stats.WorkedShifts} (нужно ≥ {stats.MinShiftsForCoefficient}) — " +
                      $"{(stats.ShiftsOk ? "выполнено" : "не выполнено")}; учтено " +
                      $"{stats.IncludedTimeRatio:0.#%} рабочего времени (нужно ≥ 30%) — " +
                      $"{(stats.IncludedTimeOk ? "выполнено" : "не выполнено")}.");
        if (stats.ExcludedFromReports > 0)
            sb.AppendLine($"Записей, исключённых мастером из отчётов: {stats.ExcludedFromReports} " +
                          "— в расчёт не входят.");
        sb.AppendLine("Как считано: теми же формулами и по тем же данным, что отчёт мастера " +
                      "(время наладки/изготовления — за вычетом регламентных перерывов и " +
                      "отмеченных простоев; КПД — по нормативам из техкарты, без исправленных; " +
                      "доля простоев — от времени работы). Сравнивать с таблицей коэффициентов " +
                      "не нужно: коэффициент по разряду считает мастер, оператору его не показывают.");
        return sb.ToString().TrimEnd();
    }

    private static string Ratio(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) || value == 0
            ? "нет данных (нет нормативов)"
            : $"{value:0.#%}";
}
