using DocumentFormat.OpenXml.Spreadsheet;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using libeLog.Extensions;
using libeLog.Models;
using libeLog;
using System.Security;


namespace remeLog.Infrastructure.Extensions
{
    public static class Part
    {
        public static double AverageReplacementTimeRatio(this IEnumerable<Models.Part> parts)
        {
            var validReplacementTimesRatios = parts.Where(p => p.PartReplacementTime != 0 && !double.IsNaN(p.PartReplacementTime) && !double.IsPositiveInfinity(p.PartReplacementTime)).Select(p => p.PartReplacementTime);
            return validReplacementTimesRatios.Any() ? validReplacementTimesRatios.Average() : 0.0;
        }

        public static double AverageSetupRatio(this IEnumerable<Models.Part> parts)
        {
            var validSetupRatios = parts
                .Where(p => p.SetupRatio > 0 && !double.IsNaN(p.SetupRatio) && !double.IsPositiveInfinity(p.SetupRatio))
                .Select(p => p.SetupRatio <= AppSettings.MaxSetupLimit ? p.SetupRatio : AppSettings.MaxSetupLimit)
                .DefaultIfEmpty(0.0);

            return validSetupRatios.Any() ? validSetupRatios.Average() : 0.0;
        }

        public static double AverageSetupRatioInclurePartialSetups(this IEnumerable<Models.Part> parts)
        {
            List<double> setups = new();
            foreach (var p in parts)
            {
                if (p.SetupRatio > 0 && !double.IsNaN(p.SetupRatio) && !double.IsPositiveInfinity(p.SetupRatio))
                {
                    setups.Add(p.SetupRatio <= AppSettings.MaxSetupLimit ? p.SetupRatio : AppSettings.MaxSetupLimit);
                } 
                else if (p.SetupTimePlanForReport > 0 && p.PartialSetupTime > 0 && p.PartialSetupTime > p.SetupTimePlanForReport)
                {
                    var ratio = p.SetupTimePlanForReport / p.PartialSetupTime;
                    setups.Add(ratio <= AppSettings.MaxSetupLimit ? ratio : AppSettings.MaxSetupLimit);
                }
            }
            return setups.Any() ? setups.Average() : 0.0;
        }

        public static double AverageSetupRatioIncludeDowntimes(this IEnumerable<Models.Part> parts)
        {
            var validSetupRatios = parts
                .Where(p => p.SetupRatioIncludeDowntimes > 0 && !double.IsNaN(p.SetupRatioIncludeDowntimes) && !double.IsPositiveInfinity(p.SetupRatioIncludeDowntimes))
                .Select(p => p.SetupRatioIncludeDowntimes <= AppSettings.MaxSetupLimit ? p.SetupRatioIncludeDowntimes : AppSettings.MaxSetupLimit)
                .DefaultIfEmpty(0.0);

            return validSetupRatios.Any() ? validSetupRatios.Average() : 0.0;
        }

        public static double SetupRatio(this IEnumerable<Models.Part> parts)
        {
            double factSum = 0;
            foreach (var part in parts)
            {
                // убрать условие для учета без нормативов
                if (part.SetupTimePlanForCalc != 0) factSum += part.SetupTimeFact + part.PartialSetupTime;
            }
            var factPlanSum = SetupTimePlanForReport(parts);
            return factSum == 0 ? 0 : factPlanSum / factSum;
        }

        public static double AverageProductionRatio(this IEnumerable<Models.Part> parts)
        {
            var validProductionRatios = parts.Where(p => p.ProductionRatio > 0 && !double.IsNaN(p.ProductionRatio) && !double.IsPositiveInfinity(p.ProductionRatio)).Select(p => p.ProductionRatio);
            return validProductionRatios.Any() ? validProductionRatios.Average() : 0.0;
        }

        public static double ProductionRatio(this IEnumerable<Models.Part> parts)
        {
            double planSum = 0;
            double factSum = 0;
            foreach (var part in parts.Where(p => p.ProductionTimePlanForCalc > 0))
            {
                factSum += part.ProductionTimeFact;
                planSum += part.FinishedCountFact * part.ProductionTimePlanForCalc;
            }
            return factSum == 0 ? 0 : planSum / factSum;
        }

        public static double ProductionRatioIncludeDowntimes(this IEnumerable<Models.Part> parts)
        {
            double planSum = 0;
            double factSum = 0;
            foreach (var part in parts.Where(p => p.ProductionTimePlanForCalc > 0))
            {
                factSum += part.ProductionTimeFact + part.MachiningDowntimes;
                planSum += part.FinishedCountFact * part.ProductionTimePlanForCalc;
            }
            return factSum == 0 ? 0 : planSum / factSum;
        }

        /// <summary>
        /// Отмеченные простои за период
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shift">Фильтр по смене</param>
        /// <returns></returns>
        public static double SpecifiedDowntimes(this IEnumerable<Models.Part> parts, DateTime fromDate, DateTime toDate, Shift shift)
        {
            double sum = 0;
            var filteredParts = shift.Type is ShiftType.All ? parts : parts.Where(p => p.Shift == shift.Name);
            foreach (var part in filteredParts)
            {
                sum += part.SetupDowntimes + part.MachiningDowntimes;
            }
            var totalWorkMinutes = (toDate.AddDays(1) - fromDate).TotalDays * shift.Minutes;
            return sum;
        }

        /// <summary>
        /// Вычисляет суммарное время простоев для указанных деталей в заданном временном интервале и смене.
        /// </summary>
        /// <param name="parts">Коллекция деталей.</param>
        /// <param name="fromDate">Дата начала интервала.</param>
        /// <param name="toDate">Дата окончания интервала.</param>
        /// <param name="shiftType">Тип смены (например, дневная, ночная или все смены).</param>
        /// <returns>Суммарное время простоев в минутах.</returns>
        public static double SpecifiedDowntimes(this IEnumerable<Models.Part> parts, ShiftType shiftType)
        {
            double sum = 0;
            var filteredParts = shiftType is ShiftType.All ? parts : parts.Where(p => p.Shift == new Shift(shiftType).Name);
            foreach (var part in filteredParts)
            {
                sum += part.SetupDowntimes + part.MachiningDowntimes;
            }
            return sum;
        }

        /// <summary>
        /// Соотношение отмеченных простоев к общему времени смены
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shift">Фильтр по смене</param>
        /// <returns></returns>
        public static double SpecifiedDowntimesRatio(this IEnumerable<Models.Part> parts, Shift shift) 
        {
            double sum = 0;
            var filteredParts = shift.Type is ShiftType.All ? parts : parts.Where(p => p.Shift == shift.Name);
            foreach (var part in filteredParts)
            {
                sum += part.SetupDowntimes + part.MachiningDowntimes;
            }
            return sum / parts.FullWorkedTime().TotalMinutes;
        }

        /// <summary>
        /// Соотношение отмеченных простоев к общему времени смены
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shiftType">Фильтр по смене</param>
        /// <returns></returns>
        public static double SpecifiedDowntimesRatio(this IEnumerable<Models.Part> parts, ShiftType shiftType)
        {
            return parts.SpecifiedDowntimesRatio(new Shift(shiftType));
        }

        /// <summary>
        /// Соотношение отмеченных простоев к общему времени
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="downtimeType">Тип простоя</param>
        /// <returns></returns>
        public static double SpecifiedDowntime(this IEnumerable<Models.Part> parts, Downtime downtimeType)
        {
            double sum = 0;
            foreach (var part in parts)
            {
                switch (downtimeType)
                {
                    case Downtime.Maintenance:
                        sum += part.MaintenanceTime;
                        break;
                    case Downtime.ToolSearching:
                        sum += part.ToolSearchingTime;
                        break;
                    case Downtime.ToolChanging:
                        sum += part.ToolChangingTime;
                        break;
                    case Downtime.Mentoring:
                        sum += part.MentoringTime;
                        break;
                    case Downtime.ContactingDepartments:
                        sum += part.ContactingDepartmentsTime;
                        break;
                    case Downtime.FixtureMaking:
                        sum += part.FixtureMakingTime;
                        break;
                    case Downtime.HardwareFailure:
                        sum += part.HardwareFailureTime;
                        break;
                    case Downtime.CreateNcProgram:
                        sum += part.CreateNcProgramTime;
                        break;
                    case Downtime.Special:
                        sum += part.SpecialDowntimeTime;
                        break;
                }
            }
            return sum;
        }

        /// <summary>
        /// Соотношение отмеченных простоев к общему времени
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="downtimeType">Тип простоя</param>
        /// <returns></returns>
        public static double SpecifiedDowntimeRatio(this IEnumerable<Models.Part> parts, Downtime downtimeType)
        {
            return parts.SpecifiedDowntime(downtimeType) / parts.FullWorkedTime().TotalMinutes;
        }

        /// <summary>
        /// Соотношение отмеченных простоев к общему времени
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="excludeDowntimeTypes">Типы простоев для исключения</param>
        /// <returns></returns>
        public static double SpecifiedDowntimesRatioExcluding(this IEnumerable<Models.Part> parts, IEnumerable<Downtime> excludeDowntimeTypes)
        {
            double sum = 0;
            foreach (var part in parts)
            {
                if (!excludeDowntimeTypes.Contains(Downtime.CreateNcProgram))
                    sum += part.CreateNcProgramTime;
                if (!excludeDowntimeTypes.Contains(Downtime.Maintenance))
                    sum += part.MaintenanceTime;
                if (!excludeDowntimeTypes.Contains(Downtime.ToolSearching))
                    sum += part.ToolSearchingTime;
                if (!excludeDowntimeTypes.Contains(Downtime.ToolChanging))
                    sum += part.ToolChangingTime;
                if (!excludeDowntimeTypes.Contains(Downtime.Mentoring))
                    sum += part.MentoringTime;
                if (!excludeDowntimeTypes.Contains(Downtime.ContactingDepartments))
                    sum += part.ContactingDepartmentsTime;
                if (!excludeDowntimeTypes.Contains(Downtime.FixtureMaking))
                    sum += part.FixtureMakingTime;
                if (!excludeDowntimeTypes.Contains(Downtime.HardwareFailure))
                    sum += part.HardwareFailureTime;
                if (!excludeDowntimeTypes.Contains(Downtime.Special))
                    sum += part.SpecialDowntimeTime;
            }
            return sum / parts.FullWorkedTime().TotalMinutes;
        }

        /// <summary>
        /// Соотношение отмеченных простоев к общему времени
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="excludeDowntimeType">Тип простоя для исключения</param>
        /// <returns></returns>
        public static double SpecifiedDowntimesRatioExcluding(this IEnumerable<Models.Part> parts, Downtime excludeDowntimeType)
        {
            return parts.SpecifiedDowntimesRatioExcluding(new List<Downtime>() { excludeDowntimeType });
        }

        /// <summary>
        /// Соотношение частичных наладок к общему времени смены
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shiftType">Фильтр по смене</param>
        /// <returns></returns>
        public static double PartialSetupRatio(this IEnumerable<Models.Part> parts, DateTime fromDate, DateTime toDate, ShiftType shiftType)
        {
            var sum = parts.Where(p => shiftType == ShiftType.All || p.Shift == new Shift(shiftType).Name)
                 .Sum(p => p.PartialSetupTime);
            var totalWorkMinutes = (toDate.AddDays(1) - fromDate).TotalDays * (int)shiftType;
            return sum / totalWorkMinutes;
        }

        /// <summary>
        /// Время частичных наладок в минутах
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shiftType">Фильтр по смене</param>
        /// <returns></returns>
        public static double PartialSetup(this IEnumerable<Models.Part> parts, DateTime fromDate, DateTime toDate, ShiftType shiftType) 
            => parts
            .Where(p => shiftType == ShiftType.All || p.Shift == new Shift(shiftType).Name)
            .Sum(p => p.PartialSetupTime);

        /// <summary>
        /// Неотмеченные простои в минутах
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shift">Фильтр по смене</param>
        /// <returns></returns>
        public static double UnspecifiedDowntimes(this IEnumerable<Models.Part> parts, DateTime fromDate, DateTime toDate, Shift shift)
        {
            double sum = 0;
            var filteredParts = shift.Type is ShiftType.All ? parts : parts.Where(p => p.Shift == shift.Name);
            foreach (var part in filteredParts)
            {
                sum += part.SetupTimeFact + part.ProductionTimeFact + part.SetupDowntimes + part.MachiningDowntimes + part.PartialSetupTime;
            }
            var totalWorkMinutes = (toDate.AddDays(1) - fromDate).TotalDays * shift.Minutes;
            return totalWorkMinutes - sum;
        }

        /// <summary>
        /// Неотмеченные простои в минутах
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shift">Фильтр по смене</param>
        /// <returns></returns>
        public static double UnspecifiedDowntimes(this IEnumerable<Models.Part> parts, DateTime fromDate, DateTime toDate, ShiftType shiftType)
        {
            double sum = 0;
            var filteredParts = shiftType is ShiftType.All ? parts : parts.Where(p => p.Shift == new Shift(shiftType).Name);
            foreach (var part in filteredParts)
            {
                sum += part.SetupTimeFact + part.ProductionTimeFact + part.SetupDowntimes + part.MachiningDowntimes + part.PartialSetupTime;
            }
            var totalWorkMinutes = (toDate.AddDays(1) - fromDate).TotalDays * (int)shiftType;
            return totalWorkMinutes - sum;
        }

        /// <summary>
        /// Соотношение неотмеченных простоев к общему времени смены
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shift">Фильтр по смене</param>
        /// <returns></returns>
        public static double UnspecifiedDowntimesRatio(this IEnumerable<Models.Part> parts, DateTime fromDate, DateTime toDate, Shift shift)
        {
            double sum = 0;
            var filteredParts = shift.Type is ShiftType.All ? parts : parts.Where(p => p.Shift == shift.Name);
            foreach (var part in filteredParts)
            {
                sum += part.SetupTimeFact + part.ProductionTimeFact + part.SetupDowntimes + part.MachiningDowntimes + part.PartialSetupTime;
            }
            var totalWorkMinutes = Util.GetWorkDaysBeetween(fromDate, toDate) * shift.Minutes;
            return (totalWorkMinutes - sum) / totalWorkMinutes;
        }

        /// <summary>
        /// Соотношение неотмеченных простоев к общему времени смены
        /// </summary>
        /// <param name="parts">Список изготовлений</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="shift">Фильтр по смене</param>
        /// <returns></returns>
        public static double UnspecifiedDowntimesRatio(this IEnumerable<Models.Part> parts, DateTime fromDate, DateTime toDate, ShiftType shiftType)
        {
            double sum = 0;
            var filteredParts = shiftType is ShiftType.All ? parts : parts.Where(p => p.Shift == new Shift(shiftType).Name);
            foreach (var part in filteredParts)
            {
                sum += part.SetupTimeFact + part.ProductionTimeFact + part.SetupDowntimes + part.MachiningDowntimes + part.PartialSetupTime;
            }
            var totalWorkMinutes = Util.GetWorkDaysBeetween(fromDate, toDate) * (int)shiftType;
            return (totalWorkMinutes - sum) / totalWorkMinutes;
        }

        /// <summary>
        /// Вычисляет общее время настройки для отчета, суммируя планируемое время настройки для уникальных комбинаций
        /// <paramref name="PartName"/> и <paramref name="Order"/>, при условии, что есть хоть какое-то время наладки.
        /// </summary>
        /// <param name="parts">Коллекция деталей, для которых будет вычисляться время настройки.</param>
        /// <returns>Общее планируемое время настройки для отчета.</returns>
        public static double SetupTimePlanForReport(this IEnumerable<Models.Part> parts)
        {
            var sum = 0.0;
            HashSet<(string, string)> counted = new();
            foreach (var part in parts)
            {
                if (part.SetupTimeFact + part.PartialSetupTime > 0 && counted.Add((part.PartName, part.Order)))
                {
                    sum += part.SetupTimePlan;
                }
            }
            return sum;
        }

        /// <summary>
        /// Сумма затраченного времени на детали от начала наладки до завершения изготовления.
        /// </summary>
        /// <param name="parts">Коллекция деталей</param>
        /// <returns></returns>
        public static TimeSpan FullWorkedTime(this IEnumerable<Models.Part> parts)
        {
            var sum = new TimeSpan();
            foreach (var part in parts)
            {
                sum += part.EndMachiningTime - part.StartSetupTime - (TimeSpan.FromMinutes(DateTimes.GetPartialBreakBetween(part.StartSetupTime, part.EndMachiningTime)));
            }
            return sum;
        }

        /// <summary>
        /// Сумма затраченного времени на детали от начала наладки до завершения изготовления.
        /// </summary>
        /// <param name="part">Деталь</param>
        /// <returns></returns>
        public static TimeSpan FullWorkedTime(this Models.Part part)
        {
            return part.EndMachiningTime - part.StartSetupTime - (TimeSpan.FromMinutes(DateTimes.GetPartialBreakBetween(part.StartSetupTime, part.EndMachiningTime)));
        }

        /// <summary>
        /// Рассчитывает суммарное фактическое время наладки для коллекции деталей (включая частичные).
        /// </summary>
        /// <param name="parts">Коллекция деталей, для которых выполняется расчет.</param>
        /// <returns>Суммарное фактическое время настройки <see cref="SetupTimeFact"/> всех деталей в коллекции.</returns>
        public static TimeSpan TotalSetupTime(this IEnumerable<Models.Part> parts)
        {
            return parts.Any() ? TimeSpan.FromMinutes(parts.Sum(p => p.SetupTimeFact + p.PartialSetupTime)) : TimeSpan.Zero;
        }

        /// <summary>
        /// Рассчитывает суммарное фактическое время производства для коллекции деталей.
        /// </summary>
        /// <param name="parts">Коллекция деталей, для которых выполняется расчет.</param>
        /// <returns>Суммарное фактическое время производства <see cref="ProductionTimeFact"/> всех деталей в коллекции.</returns>
        public static TimeSpan TotalProductionTime(this IEnumerable<Models.Part> parts)
        {
            return parts.Any() ? TimeSpan.FromMinutes(parts.Sum(p => p.ProductionTimeFact)) : TimeSpan.Zero;
        }

        /// <summary>
        /// Рассчитывает суммарное фактическое время простоев для коллекции деталей.
        /// </summary>
        /// <param name="parts">Коллекция деталей, для которых выполняется расчет.</param>
        /// <returns>Суммарное фактическое время простоев в изготовлении <see cref="MachiningDowntimes"/> и в наладке <see cref="SetupDowntimes"/> всех деталей в коллекции.</returns>
        public static TimeSpan TotalDowntimesTime(this IEnumerable<Models.Part> parts)
        {
            return parts.Any() ? TimeSpan.FromMinutes(parts.Sum(p => p.MachiningDowntimes + p.SetupDowntimes)) : TimeSpan.Zero;
        }

        /// <summary>
        /// Рассчитывает среднее время наладки для коллекции деталей (без частичных наладок).
        /// </summary>
        /// <param name="parts">Коллекция деталей, для которых выполняется расчет.</param>
        /// <returns>Среднее арифметическое время в наладке <see cref="SetupDowntimes"/> тех деталей в коллекции, где осуществлялась полноценная наладка.</returns>
        public static TimeSpan AverageSetupTime(this IEnumerable<Models.Part> parts)
        {
            return parts.Where(p => p.SetupTimeFact > 0).Any() ? TimeSpan.FromMinutes(parts.Where(p => p.SetupTimeFact > 0).Average(p => p.SetupTimeFact)) : TimeSpan.Zero;
        }

        /// <summary>
        /// Вычисляет ограничение на время наладки для заданной детали на основе планового времени наладки, коэффициента и запасного лимита.
        /// </summary>
        /// <param name="part">Деталь, для которой вычисляется ограничение на время наладки.</param>
        /// <param name="coefficient">Необязательный коэффициент для корректировки времени наладки.</param>
        /// <param name="limit">Запасной лимит, если плановое время наладки и коэффициент не заданы.</param>
        /// <returns>Вычисленное ограничение на время наладки.</returns>
        /// <exception cref="InvalidOperationException">Выбрасывается, если не выполнены условия для вычисления ограничения.</exception>
        public static (double Value, string Info) SetupLimit(this Models.Part part, double? coefficient, int? limit)
        {
            if (part.SetupTimePlanForCalc > 0 && coefficient.HasValue)
            {
                return (part.SetupTimePlanForCalc * coefficient.Value, $"От норматива:\n{GetCoefficientDescription(part.SetupTimePlanForCalc, coefficient.Value)}");
            }
            else if (part.SetupTimePlanForCalc <= 0 && limit.HasValue)
            {
                return ((double)limit, $"Предустановленный лимит на станок:\n{limit} мин");
            }
            throw new InvalidOperationException($"Невозможно вычислить ограничение на время наладки для детали {part.PartName} {part.Order} {part.Setup} уст. от {part.StartSetupTime.ToString(Constants.DateTimeFormat)}");

            static string GetCoefficientDescription(double originalValue, double coefficient)
            {
                double result = originalValue * coefficient;

                double difference = result - originalValue;
                string description;

                if (Math.Abs(difference) < 0.001)
                {
                    description = $"{originalValue} мин без изменений";
                }
                else if (difference > 0)
                {
                    double percentage = (difference / originalValue) * 100;
                    description = $"{originalValue} мин + {percentage:F0}%";
                }
                else
                {
                    double percentage = (Math.Abs(difference) / originalValue) * 100;
                    description = $"{originalValue} мин - {percentage:F0}%";
                }

                return description;
            }
        }
    }
}
