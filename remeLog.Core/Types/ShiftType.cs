using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Types
{
    /// <summary>Смена. Значения совпадают с длительностью смены в минутах.</summary>
    public enum ShiftType
    {
        All = Shifts.AllMinutes,
        Day = Shifts.DayMinutes,
        Night = Shifts.NightMinutes
    }
}
