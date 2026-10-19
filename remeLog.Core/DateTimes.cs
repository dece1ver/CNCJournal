using System;

namespace remeLog.Core
{
    /// <summary>
    /// Копия <c>GetBreaksBetween</c> из <c>libeLog.Extensions.DateTimes</c> — нужна Part.cs,
    /// который собирается без ссылки на libeLog (WPF).
    /// </summary>
    public static class DateTimes
    {
        /// <summary>Суммарное время перерывов, полностью попадающих в интервал.</summary>
        public static TimeSpan GetBreaksBetween(DateTime startDateTime, DateTime endDateTime, bool calcOnEnd = true)
        {
            var dayShiftFirstBreak = Constants.WorkTime.DayShiftFirstBreak;
            var dayShiftSecondBreak = Constants.WorkTime.DayShiftSecondBreak;
            var dayShiftThirdBreak = Constants.WorkTime.DayShiftThirdBreak;
            var nightShiftFirstBreak = Constants.WorkTime.NightShiftFirstBreak;
            var nightShiftSecondBreak = Constants.WorkTime.NightShiftSecondBreak;
            var nightShiftThirdBreak = Constants.WorkTime.NightShiftThirdBreak;

            if (!calcOnEnd)
            {
                dayShiftFirstBreak = dayShiftFirstBreak.AddMinutes(-14);
                dayShiftSecondBreak = dayShiftSecondBreak.AddMinutes(-29);
                dayShiftThirdBreak = dayShiftThirdBreak.AddMinutes(-14);
                nightShiftFirstBreak = nightShiftFirstBreak.AddMinutes(-29);
                nightShiftSecondBreak = nightShiftSecondBreak.AddMinutes(-29);
                nightShiftThirdBreak = nightShiftSecondBreak.AddMinutes(-29);
            }

            var breaks = TimeSpan.Zero;
            var startTime = new DateTime(1, 1, 1, startDateTime.Hour, startDateTime.Minute, startDateTime.Second);
            var endTime = new DateTime(1, 1, 1, endDateTime.Hour, endDateTime.Minute, endDateTime.Second);
            if (startTime > endTime)
            {
                nightShiftSecondBreak = nightShiftSecondBreak.AddDays(1);
                nightShiftThirdBreak = nightShiftThirdBreak.AddDays(1);
                endTime = endTime.AddDays(1);
            }

            if (dayShiftFirstBreak > startTime && dayShiftFirstBreak <= endTime)
            {
                breaks += TimeSpan.FromMinutes(15);
                if (!calcOnEnd) endTime += TimeSpan.FromMinutes(15);
            }
            if (dayShiftSecondBreak > startTime && dayShiftSecondBreak <= endTime)
            {
                breaks += TimeSpan.FromMinutes(30);
                if (!calcOnEnd) endTime += TimeSpan.FromMinutes(30);
            }
            if (dayShiftThirdBreak > startTime && dayShiftThirdBreak <= endTime)
            {
                breaks += TimeSpan.FromMinutes(15);
                if (!calcOnEnd) endTime += TimeSpan.FromMinutes(15);
            }
            if (nightShiftFirstBreak > startTime && nightShiftFirstBreak <= endTime)
            {
                breaks += TimeSpan.FromMinutes(30);
                if (!calcOnEnd) endTime += TimeSpan.FromMinutes(30);
            }
            if (nightShiftSecondBreak > startTime && nightShiftSecondBreak <= endTime)
            {
                breaks += TimeSpan.FromMinutes(30);
                if (!calcOnEnd) endTime += TimeSpan.FromMinutes(30);
            }
            if (nightShiftThirdBreak > startTime && nightShiftThirdBreak <= endTime)
            {
                breaks += TimeSpan.FromMinutes(30);
            }

            return breaks;
        }
    }
}
