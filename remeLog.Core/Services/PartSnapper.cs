using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace remeLog.Core.Services
{
    /// <summary>
    /// Чистая логика слепки деталей встык в рамках смены (кнопка-магнит
    /// в PartsInfoWindow): начало следующей детали тянется к концу предыдущей.
    /// День и ночь не смешиваются, к границам смены не тянем.
    /// Частичная наладка тянется вместе с началом на всю новую наладку, чтобы
    /// её факт не изменился; остальные простои не трогаем.
    /// </summary>
    public static class PartSnapper
    {
        public sealed record SnapPlan(
            Part Prev, Part Next,
            DateTime OldStart, DateTime NewStart, int GapMinutes,
            double OldPartial, double NewPartial);

        public sealed record SnapResult(
            IReadOnlyList<SnapPlan> Plans,
            int SkippedInvariant, int SkippedPartial);

        /// <param name="maxGapMinutes">Максимальный зазор/перекрытие для слепки.</param>
        public static SnapResult Plan(IEnumerable<Part> parts, int maxGapMinutes)
        {
            var plans = new List<SnapPlan>();
            var skippedInvariant = 0;
            var skippedPartial = 0;
            foreach (var group in parts.GroupBy(p => p.Shift))
            {
                var ordered = group.OrderBy(p => p.StartSetupTime).ToList();
                for (var i = 1; i < ordered.Count; i++)
                {
                    var prev = ordered[i - 1];
                    var next = ordered[i];
                    var gap = (int)Math.Round((next.StartSetupTime - prev.EndMachiningTime).TotalMinutes);
                    if (gap == 0 || Math.Abs(gap) > maxGapMinutes) continue;
                    // Конец предыдущей не должен уехать за начало изготовления
                    // следующей — иначе ломается инвариант наладки.
                    if (prev.EndMachiningTime > next.StartMachiningTime) { skippedInvariant++; continue; }
                    var newStart = prev.EndMachiningTime;
                    var newPartial = next.PartialSetupTime;
                    if (Math.Abs(newPartial) > 1e-9)
                    {
                        var shift = (newStart - next.StartSetupTime).TotalMinutes;
                        var breaksDelta = (DateTimes.GetBreaksBetween(newStart, next.StartMachiningTime)
                                           - DateTimes.GetBreaksBetween(next.StartSetupTime, next.StartMachiningTime)).TotalMinutes;
                        newPartial += -shift - breaksDelta;
                        // В минус частичную не уводим — такой стык скип.
                        if (newPartial < 0) { skippedPartial++; continue; }
                    }
                    plans.Add(new SnapPlan(prev, next, next.StartSetupTime, newStart, gap, next.PartialSetupTime, newPartial));
                }
            }
            return new SnapResult(plans, skippedInvariant, skippedPartial);
        }

        /// <summary> Применяет план к строкам (сеттеры ставят NeedUpdate). </summary>
        public static void Apply(SnapResult result)
        {
            foreach (var plan in result.Plans)
            {
                plan.Next.StartSetupTime = plan.NewStart;
                if (Math.Abs(plan.Next.PartialSetupTime - plan.NewPartial) > 1e-9)
                    plan.Next.PartialSetupTime = plan.NewPartial;
            }
        }
    }
}
