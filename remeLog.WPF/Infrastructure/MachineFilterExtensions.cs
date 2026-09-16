using System.Collections.Generic;
using System.Linq;
using remeLog.Models;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Общие операции над фильтром станков — чтобы окна не дублировали логику
    /// (PartsInfoWindow, MachineInspectionCalendarWindow).
    /// </summary>
    public static class MachineFilterExtensions
    {
        /// <summary> Текст-сводка для кнопки фильтра: «Станки: N» / «Все станки (N)». </summary>
        public static string Summary(this IEnumerable<MachineFilter> filters)
        {
            var list = filters as IList<MachineFilter> ?? filters.ToList();
            var selected = list.Count(m => m.Filter);
            return list.Count > 0 && selected == list.Count
                ? $"Все станки ({selected})"
                : $"Станки: {selected}";
        }
    }
}
