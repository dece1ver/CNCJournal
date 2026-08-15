namespace remeLog.Infrastructure.Types
{
    /// <summary>
    /// Названия и длительности смен. Единственное место, где эти значения заданы для
    /// remeLog: названия попадают в БД (parts.Shift, cnc_shifts.Shift) и сравниваются
    /// при разборе смен и построении отчётов, длительность — признак полностью
    /// нерабочей смены. В eLog, который на remeLog.Core не ссылается, те же значения
    /// продублированы в eLog.Infrastructure.Text — менять нужно синхронно.
    /// </summary>
    public static class Shifts
    {
        public const string Day = "День";
        public const string Night = "Ночь";
        public const string All = "Все смены";

        /// <summary>Длительность дневной смены в минутах.</summary>
        public const int DayMinutes = 660;

        /// <summary>Длительность ночной смены в минутах.</summary>
        public const int NightMinutes = 630;

        /// <summary>Суммарная длительность суток по обеим сменам.</summary>
        public const int AllMinutes = DayMinutes + NightMinutes;
    }
}
