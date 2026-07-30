using System;

namespace remeLog.Core
{
    /// <summary>
    /// Копии констант из libeLog.Constants, нужных Part/Extensions — не переиспользуем
    /// libeLog напрямую (WPF-зависимость), а libeLog.Constants нужен eLog как есть.
    /// </summary>
    public static class Constants
    {
        public const string DateTimeFormat = "dd.MM.yyyy HH:mm";

        public static class WorkTime
        {
            public static readonly DateTime DayShiftFirstBreak = new(1, 1, 1, 9, 0, 0);
            public static readonly DateTime DayShiftSecondBreak = new(1, 1, 1, 12, 30, 0);
            public static readonly DateTime DayShiftThirdBreak = new(1, 1, 1, 15, 15, 0);
            public static readonly DateTime NightShiftFirstBreak = new(1, 1, 1, 22, 30, 0);
            public static readonly DateTime NightShiftSecondBreak = new(1, 1, 1, 1, 30, 0);
            public static readonly DateTime NightShiftThirdBreak = new(1, 1, 1, 4, 30, 0);
        }

        /// <summary>Суммарное время перерывов, частично попадающих в интервал.</summary>
        public static double GetPartialBreakBetween(DateTime startDateTime, DateTime endDateTime)
        {
            if (endDateTime == DateTime.MinValue) return 0;
            var dayShiftFirstBreak = WorkTime.DayShiftFirstBreak.AddMinutes(-15);
            var dayShiftSecondBreak = WorkTime.DayShiftSecondBreak.AddMinutes(-30);
            var dayShiftThirdBreak = WorkTime.DayShiftThirdBreak.AddMinutes(-15);
            var nightShiftFirstBreak = WorkTime.NightShiftFirstBreak.AddMinutes(-30);
            var nightShiftSecondBreak = WorkTime.NightShiftSecondBreak.AddMinutes(-30);
            var nightShiftThirdBreak = WorkTime.NightShiftThirdBreak.AddMinutes(-30);

            var startTime = new DateTime(1, 1, 1, startDateTime.Hour, startDateTime.Minute, startDateTime.Second);
            var endTime = new DateTime(1, 1, 1, endDateTime.Hour, endDateTime.Minute, endDateTime.Second);
            if (startTime > endTime)
            {
                nightShiftSecondBreak = nightShiftSecondBreak.AddDays(1);
                nightShiftThirdBreak = nightShiftThirdBreak.AddDays(1);
                endTime = endTime.AddDays(1);
            }

            var breaks = 0.0;
            breaks += GetPartial(startTime, endTime, dayShiftFirstBreak, 15);
            breaks += GetPartial(startTime, endTime, dayShiftSecondBreak, 30);
            breaks += GetPartial(startTime, endTime, dayShiftThirdBreak, 15);
            breaks += GetPartial(startTime, endTime, nightShiftFirstBreak, 30);
            breaks += GetPartial(startTime, endTime, nightShiftSecondBreak, 30);
            breaks += GetPartial(startTime, endTime, nightShiftThirdBreak, 30);
            return breaks;
        }

        private static double GetPartial(DateTime startDateTime, DateTime endDateTime, DateTime breakTime, double duration)
        {
            var endBreakTime = breakTime.AddMinutes(duration);
            if (startDateTime < breakTime && endDateTime > breakTime && endDateTime <= endBreakTime)
                return (endDateTime - breakTime).TotalMinutes;
            if (startDateTime > breakTime && startDateTime < endBreakTime && endDateTime >= endBreakTime)
                return (endBreakTime - startDateTime).TotalMinutes;
            if (startDateTime > breakTime && endDateTime < endBreakTime)
                return (endDateTime - startDateTime).TotalMinutes;
            if (startDateTime <= breakTime && endDateTime >= endBreakTime)
                return (endBreakTime - breakTime).TotalMinutes;
            return 0.0;
        }
    }
}
