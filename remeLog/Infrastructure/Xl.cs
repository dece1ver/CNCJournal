using ClosedXML.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using libeLog;
using libeLog.Extensions;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using remeLog.Models.Reports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static class Xl
    {
        private static readonly XLColor _lightRed = XLColor.FromHtml("#DA9694");
        private static readonly XLColor _lightGreen = XLColor.FromHtml("#96DA94");
        const double SetupLimit = 0.8;

        /// <summary>
        /// Типы экспорта отчетов операторов.
        /// </summary>
        public enum ExportOperatorReportType
        {
            /// <summary>
            /// Отчет по выполнению норм, где операторы выполнили план ниже нормы.
            /// </summary>
            Under,

            /// <summary>
            /// Отчет по выполнению норм, где операторы выполнили план значительно ниже нормы.
            /// (например, на уровне критической точки).
            /// </summary>
            Below
        }

        /// <summary>
        /// Варианты ориентации заголовков.
        /// </summary>
        public enum HeaderRotateOption
        {
            /// <summary>
            /// Горизонтальная ориентация заголовков.
            /// </summary>
            Horizontal,

            /// <summary>
            /// Вертикальная ориентация заголовков.
            /// </summary>
            Vertical
        }


        /// <summary>
        /// Перечисление, которое определяет полужирность для левого и правого заголовков.
        /// </summary>
        public enum BoldOption
        {
            /// <summary>
            /// Левый заголовок выделен полужирным, правый — нет.
            /// </summary>
            Left,

            /// <summary>
            /// Правый заголовок выделен полужирным, левый — нет.
            /// </summary>
            Right,

            /// <summary>
            /// Оба заголовка выделены полужирным.
            /// </summary>
            Both
        }


        /// <summary>
        /// Отчёт за период
        /// </summary>
        /// <param name="parts"></param>
        /// <param name="fromDate"></param>
        /// <param name="toDate"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string ExportReportForPeroid(ICollection<Part> parts, DateTime fromDate, DateTime toDate, Shift shift, string path, int? underOverBorder, string runCountFilter, bool addSheetPerMachine, IProgress<string> progress)
        {
            progress?.Report("Начало экспорта...");
            underOverBorder ??= 10;


            var runCountCondition = Util.TryParseComparison(runCountFilter, out var comparisonOperator, out var comparisonValue)
                ? Util.CreateComparisonFunc(comparisonOperator, comparisonValue)
                : (count => count >= comparisonValue);

            var tempParts = new List<Part>();

            foreach (var p in parts)
            {
                tempParts.Add(p);
            }

            var machines = new List<string>();
            var res = machines.ReadMachines();
            Database.GetShiftsByPeriod(machines, fromDate, toDate, shift, out List<ShiftInfo> shifts);
            var totalDays = Util.GetWorkDaysBeetween(fromDate, toDate);
            double totalWorkedMinutes;

            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Отчет за период");
            ws.Style.Font.FontSize = 12;
            var cm = new CM.Builder()
                .Add(CM.Machine)
                .Add(CM.WorkedShifts)
                .Add(CM.NoOperatorShifts)
                .Add(CM.HardwareRepairShifts)
                .Add(CM.NoPowerShifts)
                .Add(CM.ProcessRelatedLossShifts)
                .Add(CM.UnspecifiedOtherShifts)
                .Add(CM.SetupRatio)
                .Add(CM.SetupRatioIncludeDowntimes)
                .Add(CM.ProductionRatio)
                .Add(CM.ProductionRatioIncludeDowntimes)
                .Add(CM.SetupRatioUnder)
                .Add(CM.ProductionRatioUnder)
                .Add(CM.SetupRatioOver)
                .Add(CM.ProductionRatioOver)
                .Add(CM.SetupUnderOverRatio)
                .Add(CM.ProductionUnderOverRatio)
                .Add(CM.SetupToTotalRatio)
                .Add(CM.ProductionToTotalRatio)
                .Add(CM.ProductionEfficiencyToTotalRatio)
                .Add(CM.AverageSetupTime)
                .Add(CM.TotalSetupTime)
                .Add(CM.TotalProductionTime)
                .Add(CM.TotalDowntimesTime)
                .Add(CM.TotalTime)
                .Add(CM.AverageFinishedCount)
                .Add(CM.AveragePartsCount)
                .Add(CM.SmallProductionsRatio)
                .Add(CM.SmallSeriesRatio)
                .Add(CM.AverageReplacementTime)
                .Add(CM.SpecifiedDowntimes)
                .Add(CM.CreateNcProgramTime)
                .Add(CM.MaintenanceTime)
                .Add(CM.ToolSearchingTime)
                .Add(CM.ToolChangingTime)
                .Add(CM.MentoringTime)
                .Add(CM.ContactingDepartmentsTime)
                .Add(CM.FixtureMakingTime)
                .Add(CM.HardwareFailureTime)
                .Add(CM.UnspecifiedDowntimes)
                .Add(CM.CountPerMachine)
                .Build();
            var headerRow = 2;
            var ci = cm.GetIndexes();
            ConfigureWorksheetHeader(ws, cm, HeaderRotateOption.Vertical, 65, 8);

            var headerRange = ws.Range(2, 1, 2, cm.Count);
            var row = 3;
            var firstDataRow = row;
            progress?.Report("Подготовка данных...");
            var totalUnique = parts
                .Select(p => new
                {
                    Part = p,
                    NormalizedName = p.PartName.NormalizedPartName()
                })
                .GroupBy(x => x.NormalizedName)
                .Where(group => runCountFilter == null ||
                       runCountCondition(group.Select(x => x.Part.Order).Distinct().Count()))
                .Select(group => group.First().Part)
                .ToList();

            var filteredParts = parts
                .Where(p => !p.ExcludeFromReports)
                .GroupBy(p => p.Machine)
                .SelectMany(machineGroup =>
                    machineGroup
                        .GroupBy(p => p.PartName)
                        .Where(partGroup => runCountFilter == null ||
                            runCountCondition(partGroup.GroupBy(p => p.Order).Count()))
                        .SelectMany(partGroup => partGroup))
                .OrderBy(p => p.Machine);
            progress?.Report("Формирование общего листа...");
            var totalFinished = filteredParts.DistinctBy(p => p.Order).Sum(p => p.TotalCount);
            var uniqueParts = filteredParts.DistinctBy(p => p.PartName).ToList();

            foreach (var partGroup in filteredParts.GroupBy(p => p.Machine).OrderBy(pg => pg.Key))
            {
                parts = partGroup.OrderBy(p => p.StartSetupTime).ToList();
                totalWorkedMinutes = parts.FullWorkedTime().TotalMinutes;
                ws.Cell(row, ci[CM.Machine]).Value = partGroup.Key;
                ws.Cell(row, ci[CM.WorkedShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s is not ({ Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 }));
                ws.Cell(row, ci[CM.NoOperatorShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Отсутствие оператора" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.HardwareRepairShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Ремонт оборудования" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.NoPowerShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Отсутствие электричества" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.ProcessRelatedLossShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Организационные потери" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.UnspecifiedOtherShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Другое" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is ({ Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 }));
                ws.Cell(row, ci[CM.SetupRatio]).Value = parts.AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioIncludeDowntimes]).Value = parts.AverageSetupRatioIncludeDowntimes();
                ws.Cell(row, ci[CM.ProductionRatio]).Value = parts.ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioIncludeDowntimes]).Value = parts.ProductionRatioIncludeDowntimes();
                var setupUnderRatio = parts.Where(p => p.FinishedCountFact < underOverBorder).AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioUnder]).Value = setupUnderRatio;
                var productionUnderRatio = parts.Where(p => p.FinishedCountFact < underOverBorder).ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioUnder]).Value = productionUnderRatio;
                var setupOverRatio = parts.Where(p => p.FinishedCountFact >= underOverBorder).AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioOver]).Value = setupOverRatio;
                var productionOverRatio = parts.Where(p => p.FinishedCountFact >= underOverBorder).ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioOver]).Value = productionOverRatio;
                ws.Cell(row, ci[CM.SetupUnderOverRatio]).Value = setupUnderRatio == 0 ? 0 : setupOverRatio / setupUnderRatio;
                ws.Cell(row, ci[CM.ProductionUnderOverRatio]).Value = productionUnderRatio == 0 ? 0 : productionOverRatio / productionUnderRatio;
                var setupTimeFactSum = parts.Sum(p => p.SetupTimeFact);
                var prodTimeFactSum = parts.Sum(p => p.ProductionTimeFact);
                ws.Cell(row, ci[CM.SetupToTotalRatio]).Value = 1 - prodTimeFactSum / totalWorkedMinutes - parts.SpecifiedDowntimesRatio(ShiftType.All);
                ws.Cell(row, ci[CM.ProductionToTotalRatio]).Value = prodTimeFactSum / totalWorkedMinutes;
                var prodTimePlanSum = parts.Sum(p => p.PlanForBatch);
                ws.Cell(row, ci[CM.ProductionEfficiencyToTotalRatio]).Value = prodTimePlanSum / totalWorkedMinutes;

                ws.Cell(row, ci[CM.AverageSetupTime]).SetValue(parts.AverageSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalSetupTime]).SetValue(parts.TotalSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalProductionTime]).SetValue(parts.TotalProductionTime().TotalHours);
                ws.Cell(row, ci[CM.TotalDowntimesTime]).SetValue(parts.TotalDowntimesTime().TotalHours);
                ws.Cell(row, ci[CM.TotalTime]).SetValue(totalWorkedMinutes / 60);

                var uniquePartsPerMachine = parts.DistinctBy(p => p.PartName).ToList();

                var averageFinishedCount = parts.Where(p => p.FinishedCount > 0).Average(p => p.FinishedCountFact);
                var averagePartsCount = uniquePartsPerMachine.Average(p => p.TotalCount);
                var smallProductionsRatio = (double)parts.Count(p => p.FinishedCount <= underOverBorder && p.FinishedCount > 0) / parts.Count;
                var smallSeriesRatio = (double)uniquePartsPerMachine.Count(p => p.TotalCount <= underOverBorder) / uniquePartsPerMachine.Count;

                ws.Cell(row, ci[CM.AverageFinishedCount])
                    .SetValue(averageFinishedCount)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Integer;

                ws.Cell(row, ci[CM.AveragePartsCount])
                    .SetValue(averagePartsCount)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Integer;

                ws.Cell(row, ci[CM.SmallProductionsRatio])
                    .SetValue(smallProductionsRatio)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                ws.Cell(row, ci[CM.SmallSeriesRatio])
                    .SetValue(smallSeriesRatio)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                ws.Range(row, ci[CM.SetupRatio], row, ci[CM.ProductionEfficiencyToTotalRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                ws.Cell(row, ci[CM.AverageReplacementTime])
                    .SetValue(parts.Where(p => p.FinishedCountFact >= underOverBorder).AverageReplacementTimeRatio())
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

                ws.Cell(row, ci[CM.SpecifiedDowntimes]).Value = parts.SpecifiedDowntimesRatio(ShiftType.All);
                ws.Cell(row, ci[CM.CreateNcProgramTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.CreateNcProgram);
                ws.Cell(row, ci[CM.MaintenanceTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.Maintenance);
                ws.Cell(row, ci[CM.ToolSearchingTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.ToolSearching);
                ws.Cell(row, ci[CM.ToolChangingTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.ToolChanging);
                ws.Cell(row, ci[CM.MentoringTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.Mentoring);
                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.ContactingDepartments);
                ws.Cell(row, ci[CM.FixtureMakingTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.FixtureMaking);
                ws.Cell(row, ci[CM.HardwareFailureTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.HardwareFailure);
                ws.Cell(row, ci[CM.UnspecifiedDowntimes]).Value = parts.UnspecifiedDowntimesRatio(fromDate, toDate, ShiftType.All);
                ws.Cell(row, ci[CM.CountPerMachine]).Value = parts.Count;
                ws.Range(row, ci[CM.SpecifiedDowntimes], row, ci[CM.SpecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                row++;
            }
            var lastDataRow = row - 1;
            //ws.Cell(row, workedShiftsColId).Value = totalDays;
            var dataRange = ws.RangeUsed();

            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            ws.Range(headerRow, ci[CM.Machine], lastDataRow, ci[CM.Machine]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.ProductionRatioIncludeDowntimes]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.ProductionRatioIncludeDowntimes]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.8);
            ws.Range(headerRow, ci[CM.SetupRatioUnder], lastDataRow, ci[CM.ProductionRatioUnder]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent2, 0.8);
            ws.Range(headerRow, ci[CM.SetupRatioOver], lastDataRow, ci[CM.ProductionRatioOver]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupRatioOver], lastDataRow, ci[CM.ProductionRatioOver]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent3, 0.8);
            ws.Range(headerRow, ci[CM.SetupUnderOverRatio], lastDataRow, ci[CM.ProductionUnderOverRatio]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent4, 0.8);
            ws.Range(headerRow, ci[CM.SetupToTotalRatio], lastDataRow, ci[CM.ProductionEfficiencyToTotalRatio]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupToTotalRatio], lastDataRow, ci[CM.ProductionEfficiencyToTotalRatio]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent5, 0.8);
            ws.Range(headerRow, ci[CM.SpecifiedDowntimes], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SpecifiedDowntimes], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent6, 0.8);
            ws.Range(headerRow, ci[CM.SpecifiedDowntimes], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Range(firstDataRow, ci[CM.WorkedShifts], lastDataRow, ci[CM.WorkedShifts]).Style.Font.FontColor = XLColor.Red;
            ws.Range(firstDataRow, ci[CM.WorkedShifts], lastDataRow, ci[CM.WorkedShifts]).AddConditionalFormat().WhenEquals($"=$B${lastDataRow + 2}").Font.FontColor = XLColor.Green;
            dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            dataRange.SetAutoFilter(true);
            ws.Column(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Range(1, 1, 2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Columns().AdjustToContents();
            ws.RowsUsed().Height = 20;
            ws.Row(2).Height = 130;
            var title = $"Отчёт за период с {fromDate.ToString(Constants.ShortDateFormat)} по {toDate.ToString(Constants.ShortDateFormat)}";
            switch (shift.Type)
            {
                case ShiftType.Day:
                    title += " за дневные смены";
                    break;
                case ShiftType.Night:
                    title += " за ночные смены";
                    break;
            }
            if (comparisonValue > 1) title += $" ( {Util.GetOperatorSymbol(comparisonOperator)}{comparisonValue.FormattedLaunches(true)} )";
            ws.Cell(1, 1).Value = title;
            ws.Range(1, 1, 1, cm.Count).Merge();
            ws.Range(1, 1, 1, 1).Style.Font.FontSize = 16;
            ws.Columns(2, cm.Count).Width = 8;

            ws.Cell(row, ci[CM.Machine]).Value = "Итог:";
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(row, ci[CM.Machine], row, ci[CM.UnspecifiedOtherShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Range(row, ci[CM.NoOperatorShifts], row, ci[CM.UnspecifiedOtherShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            for (int col = ci[CM.WorkedShifts]; col <= ci[CM.UnspecifiedOtherShifts]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"AVERAGE({colLetter}{firstDataRow}:{colLetter}{lastDataRow})/$B${lastDataRow + 2}";
            }
            for (int col = ci[CM.SetupRatio]; col <= ci[CM.AverageSetupTime]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"AVERAGE({colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }
            for (int col = ci[CM.TotalSetupTime]; col <= ci[CM.TotalTime]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"SUM({colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }
            for (int col = ci[CM.SpecifiedDowntimes]; col <= ci[CM.UnspecifiedDowntimes]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"AVERAGE({colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }
            ws.Range(row, ci[CM.WorkedShifts], row, ci[CM.UnspecifiedOtherShifts]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentPrecision2;
            ws.Range(row, ci[CM.SetupRatio], row, ci[CM.UnspecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Range(firstDataRow, ci[CM.AverageSetupTime], row, ci[CM.TotalTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            row++;
            ws.Cell(row, ci[CM.Machine]).Value = "Рабочих смен:";
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Value = shift.Type == ShiftType.All ? totalDays * 2 : totalDays;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            ws.Columns(ci[CM.SetupUnderOverRatio], ci[CM.ProductionUnderOverRatio]).Hide();

            var partsByMachine = filteredParts.GroupBy(p => p.Machine).ToDictionary(g => g.Key, g => g.ToList());

            var mcm = new CM.Builder()
                        .Add(CM.Date)
                        .Add(CM.Shift)
                        .Add(CM.Operator)
                        .Add(CM.Part)
                        .Add(CM.Order)
                        .Add(CM.TotalByOrder)
                        .Add(CM.Finished)
                        .Add(CM.Setup)
                        .Add(CM.StartSetupTime)
                        .Add(CM.StartMachiningTime)
                        .Add(CM.EndMachiningTime)
                        .Add(CM.SetupTimePlan)
                        .Add(CM.SetupTimeFact)
                        .Add(CM.SingleProductionTimePlan)
                        .Add(CM.MachiningTime)
                        .Add(CM.SingleProductionTime)
                        .Add(CM.PartReplacementTime)
                        .Add(CM.ProductionTimeFact)
                        .Add(CM.PlanForBatch)
                        .Add(CM.OperatorComment)
                        .Add(CM.SetupDowntimes)
                        .Add(CM.MachiningDowntimes)
                        .Add(CM.PartialSetupTime)
                        .Add(CM.CreateNcProgramTime)
                        .Add(CM.MaintenanceTime)
                        .Add(CM.ToolSearchingTime)
                        .Add(CM.MentoringTime)
                        .Add(CM.ContactingDepartmentsTime)
                        .Add(CM.FixtureMakingTime)
                        .Add(CM.HardwareFailureTime)
                        .Add(CM.SpecifiedDowntimesRatio)
                        .Add(CM.SpecifiedDowntimesComment)
                        .Add(CM.SetupRatioTitle)
                        .Add(CM.MasterSetupComment)
                        .Add(CM.ProductionRatioTitle)
                        .Add(CM.MasterProductionComment)
                        .Add(CM.MasterComment)
                        .Add(CM.FixedSetupTimePlan)
                        .Add(CM.FixedProductionTimePlan)
                        .Add(CM.EngineerComment)
                        .Build();
            if (addSheetPerMachine)
            {
                foreach (var machine in partsByMachine.Keys)
                {
                    progress?.Report($"Формирование листа по станку {machine}...");
                    ConfigureMachineSheetForPeriod(wb, partsByMachine[machine], machine, mcm);
                }

                var tcm = new CM
                    .Builder()
                    .Add(CM.Part)
                    .Add(CM.Order)
                    .Build();

                var wst = wb.AddWorksheet("Общий список");
                ConfigureWorksheetHeader(wst, tcm);
                wst.Style.Font.FontSize = 10;
                wst.Style.Alignment.WrapText = true;
                wst.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                wst.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ci = tcm.GetIndexes();
                int rowt = 3;
                progress?.Report($"Формирование общего списка номенклатуры...");
                foreach (var part in totalUnique)
                {
                    wst.Cell(rowt, ci[CM.Part]).SetValue(part.PartName);
                    wst.Cell(rowt, ci[CM.Order]).SetValue(part.Order);
                    rowt++;
                }
                wst.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                wst.Columns().AdjustToContents();


                wst.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                wst.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                wst.RangeUsed().Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                wst.RangeUsed().SetAutoFilter(true);
                wst.Columns().AdjustToContents();
                wst.Row(1).Delete();
                wst.SheetView.FreezeRows(1);

            }
            progress?.Report("Формирование завершено, сохранение файла...");
            wb.SaveAs(path);
            progress?.Report("Формирование завершено, выберите ответ в диалоговом окне");
            //AddDiagramToReportForPeriod(path);
            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo() { UseShellExecute = true, FileName = path });
            return $"Файл сохранен в \"{path}\"";
        }

        /// <summary>
        /// Отчёт за период
        /// </summary>
        /// <param name="parts"></param>
        /// <param name="fromDate"></param>
        /// <param name="toDate"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        /// TODO: Столбцы с отношениями
        public static async Task<string> ExportNewReportForPeroidAsync(ICollection<Part> parts, DateTime fromDate, DateTime toDate, Shift shift, string path, IProgress<string> progress)
        {
            progress?.Report("Начало экспорта...");
            var totalParts = parts;
            // ==================== ПОДГОТОВКА ДАННЫХ ====================
            var tempParts = new List<Part>();
            foreach (var p in parts)
            {
                tempParts.Add(p);
            }

            var machines = new List<string>();
            var res = machines.ReadMachines();
            Database.GetShiftsByPeriod(machines, fromDate, toDate, shift, out List<ShiftInfo> shifts);
            var totalDays = Util.GetWorkDaysBeetween(fromDate, toDate);

            // ==================== СОЗДАНИЕ EXCEL ДОКУМЕНТА ====================
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Отчет за период");
            ws.Style.Font.FontSize = 11;

            // ==================== НАСТРОЙКА КОЛОНОК ====================
            var cm = new CM.Builder()
                .Add(CM.Machine)
                .Add(CM.WorkedShifts)
                .Add(CM.NoOperatorShifts)
                .Add(CM.HardwareRepairShifts)
                .Add(CM.NoPowerShifts)
                .Add(CM.ProcessRelatedLossShifts)
                .Add(CM.UnspecifiedOtherShifts)
                .Add(CM.SetupRatio)
                .Add(CM.SetupRatioIncludePartialSetups)
                .Add(CM.SetupRatioIncludeDowntimes)
                .Add(CM.ProductionRatio)
                .Add(CM.ProductionRatioIncludeDowntimes)
                .Add(CM.SetupRatioUnder, $"Эффективность наладки{Environment.NewLine}на не серийке")
                .Add(CM.ProductionRatioUnder, $"Эффективность изготовления{Environment.NewLine}на не серийке")
                .Add(CM.SetupRatioOver, $"Эффективность наладки{Environment.NewLine}на серийке")
                .Add(CM.ProductionRatioOver, $"Эффективность изготовления{Environment.NewLine}на серийке")
                .Add(CM.ProductionEfficiencyToTotalRatio)
                .Add(CM.MachineTimeToTotalRatio)
                .Add(CM.SetupToTotalRatio)
                .Add(CM.ProductionToTotalRatio)
                .Add(CM.SpecifiedDowntimes)
                .Add(CM.CreateNcProgramTime)
                .Add(CM.MaintenanceTime)
                .Add(CM.ToolSearchingTime)
                .Add(CM.ToolChangingTime)
                .Add(CM.MentoringTime)
                .Add(CM.ContactingDepartmentsTime)
                .Add(CM.FixtureMakingTime)
                .Add(CM.HardwareFailureTime)
                .Add(CM.SpecialDowntimeTime)
                .Add(CM.UnspecifiedDowntimes)
                .Add(CM.TotalMachinigTime)
                .Add(CM.AverageReplacementTime, "Среднее время замены детали, мин")
                .Add(CM.AverageSetupTime, "Среднее время наладки, час")
                .Add(CM.TotalSetupTime, "Общее время наладок, час")
                .Add(CM.TotalSetupTimeSerial, "Общее время наладок серийки, час")
                .Add(CM.TotalProductionTime, "Общее время изготовления, час")
                .Add(CM.TotalProductionTimeSerial, "Общее время изготовления серийки, час")
                .Add(CM.TotalDowntimesTime, "Общее время простоев, час")
                .Add(CM.TotalDowntimesTimeSerial, "Общее время простоев серийки, час")
                .Add(CM.NonSerialPartsTime)
                .Add(CM.SerialPartsTime)
                .Add(CM.TotalTime)
                .Add(CM.SerialPartsTimeRatio)
                .Add(CM.Finished, "Общее выполненное количество деталей, шт")
                .Add(CM.AverageFinishedCount, "Среднее выполненное количество деталей, шт")
                .Add(CM.AveragePartsCountNonSerial, "Среднее количество деталей в не серийной партии, шт")
                .Add(CM.AveragePartsCountSerial, "Среднее количество деталей в серийной партии, шт")
                .Add(CM.AveragePartsCount, "Среднее количество деталей в партии, шт")
                .Add(CM.Orders)
                .Add(CM.SerialOrders)
                .Add(CM.SerialOrdersRatio)
                .Add(CM.CountPerMachine, "Количество записей")
                .Add(CM.SerialCount)
                .Add(CM.SerialCountRatio)
                .Build();

            var headerRow = 2;
            var ci = cm.GetIndexes();
            ConfigureWorksheetHeader(ws, cm, HeaderRotateOption.Vertical, 105, 8);
            var headerRange = ws.Range(2, 1, 2, cm.Count);

            // ==================== ПОДГОТОВКА ДАННЫХ ДЛЯ ОБРАБОТКИ ====================
            var row = 3;
            var firstDataRow = row;

            progress?.Report("Получение списка серийных деталей...");
            var serialParts = await libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!);

            progress?.Report("Подготовка данных...");
            var serialPartNames = serialParts.Select(p => p.PartName.NormalizedPartNameWithoutComments()).ToImmutableHashSet();
            var filteredParts = parts
                .Where(p => !p.ExcludeFromReports)
                .GroupBy(p => p.Machine)
                .SelectMany(machineGroup =>
                    machineGroup
                        .GroupBy(p => p.PartName)
                        .SelectMany(partGroup => partGroup))
                .OrderBy(p => p.Machine);

            progress?.Report("Формирование общего листа...");
            var totalFinished = filteredParts.DistinctBy(p => p.Order).Sum(p => p.TotalCount);

            // ==================== ЗАПОЛНЕНИЕ ДАННЫХ ПО СТАНКАМ ====================
            foreach (var partGroup in filteredParts.GroupBy(p => p.Machine).OrderBy(pg => pg.Key))
            {
                ws.Row(row).Height = 20;
                parts = partGroup.OrderBy(p => p.StartSetupTime).ToList();
                double totalWorkedMinutes = parts.FullWorkedTime().TotalMinutes;

                // ---------- ОСНОВНАЯ ИНФОРМАЦИЯ ----------
                ws.Cell(row, ci[CM.Machine]).Value = partGroup.Key;

                // ---------- ИНФОРМАЦИЯ О СМЕНАХ ----------
                ws.Cell(row, ci[CM.WorkedShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s is not ({ Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 }));
                ws.Cell(row, ci[CM.NoOperatorShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Отсутствие оператора" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.HardwareRepairShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Ремонт оборудования" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.NoPowerShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Отсутствие электричества" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.ProcessRelatedLossShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Организационные потери" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.UnspecifiedOtherShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Другое" && !Constants.Dates.Holidays.Contains(s.ShiftDate) && s is ({ Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 }));

                // ---------- КОЭФФИЦИЕНТЫ ЭФФЕКТИВНОСТИ ----------
                ws.Cell(row, ci[CM.SetupRatio]).Value = parts.AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioIncludePartialSetups]).Value = parts.AverageSetupRatioInclurePartialSetups();
                ws.Cell(row, ci[CM.SetupRatioIncludeDowntimes]).Value = parts.AverageSetupRatioIncludeDowntimes();
                ws.Cell(row, ci[CM.ProductionRatio]).Value = parts.ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioIncludeDowntimes]).Value = parts.ProductionRatioIncludeDowntimes();

                // ---------- ЭФФЕКТИВНОСТЬ ПО ТИПУ ПРОДУКЦИИ ----------
                var setupUnderRatio = parts.Where(p => !serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioUnder]).Value = setupUnderRatio;

                var productionUnderRatio = parts.Where(p => !serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioUnder]).Value = productionUnderRatio;

                var setupOverRatio = parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioOver]).Value = setupOverRatio;

                var productionOverRatio = parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioOver]).Value = productionOverRatio;

                // ---------- ДЕТАЛИЗАЦИЯ ПРОСТОЕВ ----------
                ws.Cell(row, ci[CM.SpecifiedDowntimes]).Value = parts.SpecifiedDowntimesRatio(ShiftType.All);
                ws.Cell(row, ci[CM.CreateNcProgramTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.CreateNcProgram);
                ws.Cell(row, ci[CM.MaintenanceTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.Maintenance);
                ws.Cell(row, ci[CM.ToolSearchingTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.ToolSearching);
                ws.Cell(row, ci[CM.ToolChangingTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.ToolChanging);
                ws.Cell(row, ci[CM.MentoringTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.Mentoring);
                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.ContactingDepartments);
                ws.Cell(row, ci[CM.FixtureMakingTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.FixtureMaking);
                ws.Cell(row, ci[CM.HardwareFailureTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.HardwareFailure);
                ws.Cell(row, ci[CM.SpecialDowntimeTime]).Value = parts.SpecifiedDowntimeRatio(Downtime.Special);
                ws.Cell(row, ci[CM.UnspecifiedDowntimes]).Value = parts.UnspecifiedDowntimesRatio(fromDate, toDate, ShiftType.All);

                // ---------- ВРЕМЕННЫЕ ПОКАЗАТЕЛИ ----------
                var setupTimeFactSum = parts.Sum(p => p.SetupTimeFact);
                var prodTimeFactSum = parts.Sum(p => p.ProductionTimeFact);
                var prodTimePlanSum = parts.Sum(p => p.PlanForBatch);

                List<double> ratios = new();
                foreach (var part in parts.Where(p => p.SetupTimePlanForReport > 0 && p.PartialSetupTime > 0))
                {
                    ratios.Add(part.PartialSetupTime / part.SetupTimePlanForReport);
                }

                ws.Cell(row, ci[CM.SetupToTotalRatio]).Value = 1 - prodTimeFactSum / totalWorkedMinutes - parts.SpecifiedDowntimesRatio(ShiftType.All);
                ws.Cell(row, ci[CM.ProductionToTotalRatio]).Value = prodTimeFactSum / totalWorkedMinutes;
                //ws.Cell(row, ci[CM.PartialSetupTime]).SetValue(ratios.Any() ? ratios.Average() : 0); // для тестов
                ws.Cell(row, ci[CM.ProductionEfficiencyToTotalRatio]).Value = prodTimePlanSum / totalWorkedMinutes;

                ws.Cell(row, ci[CM.MachineTimeToTotalRatio]).SetFormulaA1($"{ws.Cell(row, ci[CM.TotalMachinigTime]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.TotalTime]).Address.ToStringRelative()}");

                ws.Cell(row, ci[CM.AverageSetupTime]).SetValue(parts.AverageSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalSetupTime]).SetValue(parts.TotalSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalSetupTimeSerial]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).TotalSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalProductionTime]).SetValue(parts.TotalProductionTime().TotalHours);
                ws.Cell(row, ci[CM.TotalProductionTimeSerial]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).TotalProductionTime().TotalHours);
                ws.Cell(row, ci[CM.TotalDowntimesTime]).SetValue(parts.TotalDowntimesTime().TotalHours);
                ws.Cell(row, ci[CM.TotalDowntimesTimeSerial]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).TotalDowntimesTime().TotalHours);
                ws.Cell(row, ci[CM.TotalTime]).SetValue(totalWorkedMinutes / 60);

                // ---------- ВРЕМЯ ПО ТИПУ ПРОДУКЦИИ ----------
                ws.Cell(row, ci[CM.SerialPartsTime]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).FullWorkedTime().TotalHours);
                ws.Cell(row, ci[CM.NonSerialPartsTime]).SetValue(parts.Where(p => !serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).FullWorkedTime().TotalHours);
                ws.Cell(row, ci[CM.SerialPartsTimeRatio]).FormulaA1 = $"{ws.Cell(row, ci[CM.SerialPartsTime]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.TotalTime]).Address.ToStringRelative()}";

                // ---------- СТАТИСТИКА ПО ДЕТАЛЯМ ----------
                var uniquePartsPerMachine = parts.DistinctBy(p => p.PartName.NormalizedPartNameWithoutComments()).ToList();
                var uniqueSerialPartsPerMachine = parts
                    .Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments()))
                    .DistinctBy(p => p.PartName.NormalizedPartNameWithoutComments()).ToList();
                var uniqueNonSerialPartsPerMachine = parts
                    .Where(p => !serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments()))
                    .DistinctBy(p => p.PartName.NormalizedPartNameWithoutComments()).ToList();
                var averageFinishedCount = parts.Where(p => p.FinishedCount > 0).Average(p => p.FinishedCountFact);

                ws.Cell(row, ci[CM.Finished])
                    .SetValue(parts.Sum(p => p.FinishedCount))
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;

                ws.Cell(row, ci[CM.AverageFinishedCount])
                    .SetValue(averageFinishedCount)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;

                ws.Cell(row, ci[CM.AveragePartsCountSerial])
                    .SetValue(uniqueSerialPartsPerMachine.Any() ? uniqueSerialPartsPerMachine.Average(p => p.TotalCount) : 0)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;

                ws.Cell(row, ci[CM.AveragePartsCountNonSerial])
                    .SetValue(uniqueNonSerialPartsPerMachine.Any() ? uniqueNonSerialPartsPerMachine.Average(p => p.TotalCount) : 0)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;

                ws.Cell(row, ci[CM.AveragePartsCount])
                    .SetValue(uniquePartsPerMachine.Average(p => p.TotalCount))
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;

                // ---------- КОЛИЧЕСТВО ЗАПИСЕЙ ----------
                ws.Cell(row, ci[CM.Orders]).Value = parts.Where(p => !p.Order.EqualsOrdinalIgnoreCase("Без М/Л")).Select(p => p.Order).Distinct().Count();
                ws.Cell(row, ci[CM.SerialOrders]).Value = parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).DistinctBy(p => p.Order).Count();
                ws.Cell(row, ci[CM.SerialOrdersRatio]).FormulaA1 = $"={ws.Cell(row, ci[CM.SerialOrders]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.Orders]).Address.ToStringRelative()}";
                ws.Cell(row, ci[CM.SerialCount]).Value = parts.Count(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments()));
                ws.Cell(row, ci[CM.CountPerMachine]).Value = parts.Count;
                ws.Cell(row, ci[CM.SerialCountRatio]).FormulaA1 = $"={ws.Cell(row, ci[CM.SerialCount]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.CountPerMachine]).Address.ToStringRelative()}";

                // ---------- РАЗНОЕ ----------
                ws.Range(row, ci[CM.SetupRatio], row, ci[CM.ProductionEfficiencyToTotalRatio])
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
                ws.Cell(row, ci[CM.TotalMachinigTime])
                    .SetValue(parts.Sum(p => p.MachiningTime.TotalHours * p.FinishedCount))
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Integer;ws.Cell(row, ci[CM.AverageReplacementTime]);
                ws.Cell(row, ci[CM.AverageReplacementTime]).SetValue(parts.AverageReplacementTimeRatio())
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

                double totalReplacementTime = parts.Aggregate(0.0, (acc, p) => double.IsFinite(p.PartReplacementTime) ? acc + p.PartReplacementTime * p.FinishedCount : acc);

                //ws.Cell(row, ci[CM.TotalReplacementTime])
                //    .SetValue(totalReplacementTime / 60)
                //    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                ws.Range(row, ci[CM.SpecifiedDowntimes], row, ci[CM.SpecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                row++;
            }

            var lastDataRow = row - 1;

            // ==================== СОЗДАНИЕ ТАБЛИЦЫ ====================
            var dataRange = ws.RangeUsed();
            var table = dataRange.CreateTable();
            table.Name = "Отчёт за период";
            table.Theme = XLTableTheme.None;

            // ==================== ФОРМАТИРОВАНИЕ ТАБЛИЦЫ ====================
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            // ---------- ГРАНИЦЫ СЕКЦИЙ ----------
            ws.Range(headerRow, ci[CM.Machine], lastDataRow, ci[CM.Machine]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.ProductionRatioIncludeDowntimes]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupRatioOver], lastDataRow, ci[CM.ProductionRatioOver]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.AverageSetupTime], lastDataRow, ci[CM.SerialPartsTimeRatio]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.ProductionEfficiencyToTotalRatio], lastDataRow, ci[CM.SetupToTotalRatio]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.TotalSetupTime], lastDataRow, ci[CM.TotalTime]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.NonSerialPartsTime], lastDataRow, ci[CM.TotalTime]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SpecifiedDowntimes], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.Orders], lastDataRow, ci[CM.SerialOrdersRatio]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            // ---------- ЦВЕТОВОЕ ВЫДЕЛЕНИЕ СЕКЦИЙ ----------
            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.ProductionRatioIncludeDowntimes]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.8);
            ws.Range(headerRow, ci[CM.SetupRatioUnder], lastDataRow, ci[CM.ProductionRatioUnder]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent2, 0.8);
            ws.Range(headerRow, ci[CM.SetupRatioOver], lastDataRow, ci[CM.ProductionRatioOver]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent3, 0.8);
            ws.Range(headerRow, ci[CM.AverageSetupTime], lastDataRow, ci[CM.SerialPartsTimeRatio]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent4, 0.8);
            ws.Range(headerRow, ci[CM.ProductionEfficiencyToTotalRatio], lastDataRow, ci[CM.ProductionToTotalRatio]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent5, 0.8);
            ws.Range(headerRow, ci[CM.SpecifiedDowntimes], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent6, 0.8);

            // ---------- ФОРМАТИРОВАНИЕ ЧИСЕЛ ----------
            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Range(firstDataRow, ci[CM.TotalSetupTime], lastDataRow, ci[CM.TotalTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Range(firstDataRow, ci[CM.TotalMachinigTime], lastDataRow, ci[CM.TotalTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Columns(ci[CM.AverageReplacementTime], ci[CM.AverageSetupTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
            ws.Column(ci[CM.SerialPartsTimeRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Column(ci[CM.SerialOrdersRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Column(ci[CM.SerialCountRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

            // ---------- УСЛОВНОЕ ФОРМАТИРОВАНИЕ ----------
            ws.Range(firstDataRow, ci[CM.WorkedShifts], lastDataRow, ci[CM.WorkedShifts]).Style.Font.FontColor = XLColor.Red;
            ws.Range(firstDataRow, ci[CM.WorkedShifts], lastDataRow, ci[CM.WorkedShifts]).AddConditionalFormat().WhenEquals($"=$B${lastDataRow + 2}").Font.FontColor = XLColor.Green;

            // ---------- ВЫРАВНИВАНИЕ ----------
            dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Column(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Range(1, 1, 2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // ---------- ШИРИНА КОЛОНОК ----------
            ws.Columns().AdjustToContents();
            ws.Column(ci[CM.Machine]).Width = 23;
            ws.Columns(2, cm.Count).Width = 7;

            // ==================== ЗАГОЛОВОК ОТЧЁТА ====================
            var title = $"Отчёт за период с {fromDate.ToString(Constants.ShortDateFormat)} по {toDate.ToString(Constants.ShortDateFormat)}";
            switch (shift.Type)
            {
                case ShiftType.Day:
                    title += " за дневные смены";
                    break;
                case ShiftType.Night:
                    title += " за ночные смены";
                    break;
            }

            ws.Cell(1, 1).Value = title;
            ws.Range(1, 1, 1, cm.Count).Merge();
            ws.Range(1, 1, 1, 1).Style.Font.SetFontSize(14).Font.SetBold(true);

            // ==================== ИТОГИ ====================
            ws.Cell(row, ci[CM.Machine]).Value = "Итог:";
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(row, ci[CM.Machine], row, ci[CM.UnspecifiedOtherShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Range(row, ci[CM.NoOperatorShifts], row, ci[CM.UnspecifiedOtherShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row + 1, ci[CM.AveragePartsCount])
                .SetValue("Видов деталей:")
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row + 1, ci[CM.Orders])
                .SetValue(totalParts.DistinctBy(p => p.PartName).Select(p => p.PartName).Count());
            ws.Cell(row + 1, ci[CM.SerialOrders])
                .SetValue(totalParts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).DistinctBy(p => p.PartName).Select(p => p.PartName).Count());


            // ---------- ФОРМУЛЫ ДЛЯ ИТОГОВ ----------
            for (int col = ci[CM.WorkedShifts]; col <= ci[CM.UnspecifiedOtherShifts]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"SUBTOTAL(101, {colLetter}{firstDataRow}:{colLetter}{lastDataRow})/$B${lastDataRow + 2}";
            }

            for (int col = ci[CM.SetupRatio]; col <= ci[CM.AverageSetupTime]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"SUBTOTAL(101, {colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }

            for (int col = ci[CM.TotalSetupTime]; col <= ci[CM.TotalTime]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"SUBTOTAL(109, {colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }

            for (int col = ci[CM.SpecifiedDowntimes]; col <= ci[CM.UnspecifiedDowntimes]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"SUBTOTAL(101, {colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }

            ws.Cell(row, ci[CM.Finished]).FormulaA1 = $"SUBTOTAL(109, {ws.Range(firstDataRow, ci[CM.Finished], lastDataRow, ci[CM.Finished]).RangeAddress})";
            ws.Cell(row, ci[CM.TotalMachinigTime]).FormulaA1 = $"SUBTOTAL(109, {ws.Range(firstDataRow, ci[CM.TotalMachinigTime], lastDataRow, ci[CM.TotalMachinigTime]).RangeAddress})";
            ws.Cell(row, ci[CM.AverageFinishedCount]).FormulaA1 = $"SUBTOTAL(101, {ws.Range(firstDataRow, ci[CM.AverageFinishedCount], lastDataRow, ci[CM.AverageFinishedCount]).RangeAddress})";
            ws.Cell(row, ci[CM.AveragePartsCount]).FormulaA1 = $"SUBTOTAL(101, {ws.Range(firstDataRow, ci[CM.AveragePartsCount], lastDataRow, ci[CM.AveragePartsCount]).RangeAddress})";
            ws.Cell(row, ci[CM.AveragePartsCountSerial]).FormulaA1 = $"SUBTOTAL(101, {ws.Range(firstDataRow, ci[CM.AveragePartsCountSerial], lastDataRow, ci[CM.AveragePartsCountSerial]).RangeAddress})";
            ws.Cell(row, ci[CM.AveragePartsCountNonSerial]).FormulaA1 = $"SUBTOTAL(101, {ws.Range(firstDataRow, ci[CM.AveragePartsCountNonSerial], lastDataRow, ci[CM.AveragePartsCountNonSerial]).RangeAddress})";

            ws.Cell(row, ci[CM.SerialPartsTimeRatio]).FormulaA1 = $"{ws.Cell(row, ci[CM.SerialPartsTime]).Address}" +
                $"/{ws.Cell(row, ci[CM.TotalTime]).Address}";

            ws.Cell(row, ci[CM.SerialOrdersRatio]).FormulaA1 = $"{ws.Cell(row, ci[CM.SerialOrders]).Address}" +
                $"/{ws.Cell(row, ci[CM.Orders]).Address}";

            ws.Cell(row, ci[CM.SerialCountRatio]).FormulaA1 = $"{ws.Cell(row, ci[CM.SerialCount]).Address}" +
                $"/{ws.Cell(row, ci[CM.CountPerMachine]).Address}";

            for (int col = ci[CM.Orders]; col <= ci[CM.SerialOrders]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"SUBTOTAL(109, {colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }

            for (int col = ci[CM.CountPerMachine]; col <= ci[CM.SerialCount]; col++)
            {
                string colLetter = ws.Column(col).ColumnLetter();
                ws.Cell(row, col).FormulaA1 = $"SUBTOTAL(109, {colLetter}{firstDataRow}:{colLetter}{lastDataRow})";
            }

            // ---------- ФОРМАТИРОВАНИЕ ИТОГОВ ----------
            ws.Range(row, ci[CM.WorkedShifts], row, ci[CM.UnspecifiedOtherShifts]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentPrecision2;
            ws.Range(row, ci[CM.SetupRatio], row, ci[CM.UnspecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Range(row, ci[CM.AverageReplacementTime], row, ci[CM.AverageSetupTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
            ws.Range(row, ci[CM.TotalSetupTime], row, ci[CM.TotalTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Range(row, ci[CM.Finished], row, ci[CM.AveragePartsCount]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Cell(row, ci[CM.TotalMachinigTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // ==================== СТРОКА С ОБЩИМ КОЛИЧЕСТВОМ СМЕН ====================
            row++;
            ws.Cell(row, ci[CM.Machine]).Value = "Рабочих смен:";
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Value = shift.Type == ShiftType.All ? totalDays * 2 : totalDays;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // ==================== ГРУППИРОВКА КОЛОНОК ====================
            ws.Columns(ci[CM.NoOperatorShifts], ci[CM.UnspecifiedOtherShifts]).Collapse();
            ws.Columns(ci[CM.CreateNcProgramTime], ci[CM.HardwareFailureTime]).Collapse();
            ws.Columns(ci[CM.NoOperatorShifts], ci[CM.UnspecifiedOtherShifts]).Group();
            ws.Columns(ci[CM.CreateNcProgramTime], ci[CM.HardwareFailureTime]).Group();



            // ==================== ОБЩИЙ СПИСОК ====================
            var tcm = new CM
                    .Builder()
                    .Add(CM.Part)
                    .Add(CM.SerialPerList)
                    .Build();

            var wst = wb.AddWorksheet("Общий список");
            wst.Style.Font.FontSize = 10;
            wst.Style.Alignment.WrapText = true;
            wst.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            wst.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ConfigureWorksheetHeader(wst, tcm, HeaderRotateOption.Horizontal, 30);
            ci = tcm.GetIndexes();
            row = 3;
            foreach (var part in totalParts.DistinctBy(p => p.PartName))
            {
                wst.Cell(row, ci[CM.Part]).SetValue(part.PartName);
                wst.Cell(row, ci[CM.SerialPerList]).SetValue(serialPartNames.Contains(part.PartName.NormalizedPartNameWithoutComments()));
                row++;
            }
            wst.Row(1).Delete();
            var totalTable = wst.RangeUsed().CreateTable();
            totalTable.Theme = XLTableTheme.TableStyleLight15;
            wst.Column(ci[CM.Part]).Width = 70;
            wst.Column(ci[CM.SerialPerList]).Width = 14;


            var partsByMachine = filteredParts.GroupBy(p => p.Machine).ToDictionary(g => g.Key, g => g.ToList());

            var mcm = new CM.Builder()
                        .Add(CM.Date)
                        .Add(CM.Shift)
                        .Add(CM.Operator)
                        .Add(CM.Part)
                        .Add(CM.SerialPerList)
                        .Add(CM.Order)
                        .Add(CM.TotalByOrder)
                        .Add(CM.Finished)
                        .Add(CM.Setup)
                        .Add(CM.StartSetupTime)
                        .Add(CM.StartMachiningTime)
                        .Add(CM.EndMachiningTime)
                        .Add(CM.SetupTimePlan)
                        .Add(CM.SetupTimeFact)
                        .Add(CM.SingleProductionTimePlan)
                        .Add(CM.MachiningTime)
                        .Add(CM.SingleProductionTime)
                        .Add(CM.PartReplacementTime)
                        .Add(CM.ProductionTimeFact)
                        .Add(CM.PlanForBatch)
                        .Add(CM.OperatorComment)
                        .Add(CM.SetupDowntimes)
                        .Add(CM.MachiningDowntimes)
                        .Add(CM.PartialSetupTime)
                        .Add(CM.CreateNcProgramTime)
                        .Add(CM.MaintenanceTime)
                        .Add(CM.ToolSearchingTime)
                        .Add(CM.MentoringTime)
                        .Add(CM.ContactingDepartmentsTime)
                        .Add(CM.FixtureMakingTime)
                        .Add(CM.HardwareFailureTime)
                        .Add(CM.SpecifiedDowntimesRatio)
                        .Add(CM.SpecifiedDowntimesComment)
                        .Add(CM.SetupRatioTitle)
                        .Add(CM.MasterSetupComment)
                        .Add(CM.ProductionRatioTitle)
                        .Add(CM.MasterProductionComment)
                        .Add(CM.MasterComment)
                        .Add(CM.FixedSetupTimePlan)
                        .Add(CM.FixedProductionTimePlan)
                        .Add(CM.EngineerComment)
                        .Build();
            foreach (var machine in partsByMachine.Keys)
            {
                progress?.Report($"Формирование листа по станку {machine}...");
                ConfigureMachineSheetForPeriod(wb, partsByMachine[machine], machine, mcm, serialPartNames);
            }
            progress?.Report("Формирование завершено, сохранение файла...");
            wb.SaveAs(path);
            progress?.Report("Формирование завершено, выберите ответ в диалоговом окне");
            //AddDiagramToReportForPeriod(path);
            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo() { UseShellExecute = true, FileName = path });
            return $"Файл сохранен в \"{path}\"";
        }

        private static void ConfigureMachineSheetForPeriod(XLWorkbook wb, IEnumerable<Part> parts, string machine, CM cm, ImmutableHashSet<string>? serialParts = null )
        {
            var ws = wb.AddWorksheet(machine);
            ConfigureWorksheetHeader(ws, cm);
            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            var ci = cm.GetIndexes();
            int row = 3;

            foreach (var part in parts.Where(p => p.Machine == machine))
            {
                ws.Cell(row, ci[CM.Date])
                    .SetValue(part.ShiftDate)
                    .Style.DateFormat.Format = "dd.MM.yy";
                ws.Cell(row, ci[CM.Shift]).SetValue(part.Shift);
                ws.Cell(row, ci[CM.Operator]).SetValue(part.Operator);
                ws.Cell(row, ci[CM.Part]).SetValue(part.PartName);
                if (serialParts != null) 
                    ws.Cell(row, ci[CM.SerialPerList])
                        .SetValue(serialParts.Contains(part.PartName.NormalizedPartNameWithoutComments()));
                ws.Cell(row, ci[CM.Order]).SetValue(part.Order);
                ws.Cell(row, ci[CM.TotalByOrder]).SetValue(part.TotalCount);
                ws.Cell(row, ci[CM.Finished]).SetValue(part.FinishedCount);
                ws.Cell(row, ci[CM.Setup]).SetValue(part.Setup);
                ws.Cell(row, ci[CM.StartSetupTime])
                    .SetValue(part.StartSetupTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.StartMachiningTime])
                    .SetValue(part.StartMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.EndMachiningTime])
                    .SetValue(part.EndMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.SetupTimePlan]).SetValue(part.SetupTimePlan);
                ws.Cell(row, ci[CM.SetupTimeFact]).SetValue(part.SetupTimeFact);
                ws.Cell(row, ci[CM.SingleProductionTimePlan]).SetValue(part.SingleProductionTimePlan);
                ws.Cell(row, ci[CM.MachiningTime]).SetValue(part.MachiningTime);
                if (part.SingleProductionTime is double spt && spt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SingleProductionTime])
                        .SetValue(spt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                if (part.PartReplacementTime is double prt && prt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.PartReplacementTime])
                        .SetValue(prt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                ws.Cell(row, ci[CM.ProductionTimeFact]).SetValue(part.ProductionTimeFact);
                ws.Cell(row, ci[CM.PlanForBatch]).SetValue(part.PlanForBatch);
                ws.Cell(row, ci[CM.OperatorComment])
                    .SetValue(part.OperatorComment)
                    .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, ci[CM.SetupDowntimes]).SetValue(part.SetupDowntimes);
                ws.Cell(row, ci[CM.MachiningDowntimes]).SetValue(part.MachiningDowntimes);
                ws.Cell(row, ci[CM.PartialSetupTime]).SetValue(part.PartialSetupTime);
                ws.Cell(row, ci[CM.CreateNcProgramTime]).SetValue(part.CreateNcProgramTime);
                ws.Cell(row, ci[CM.MaintenanceTime]).SetValue(part.MaintenanceTime);
                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(part.ToolSearchingTime);
                ws.Cell(row, ci[CM.MentoringTime]).SetValue(part.MentoringTime);
                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).SetValue(part.ContactingDepartmentsTime);
                ws.Cell(row, ci[CM.FixtureMakingTime]).SetValue(part.FixtureMakingTime);
                ws.Cell(row, ci[CM.HardwareFailureTime]).SetValue(part.HardwareFailureTime);
                if (part.SpecifiedDowntimesRatio is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SpecifiedDowntimesRatio])
                        .SetValue(part.SpecifiedDowntimesRatio)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
                ws.Cell(row, ci[CM.SpecifiedDowntimesComment]).SetValue(part.SpecifiedDowntimesComment);
                ws.Cell(row, ci[CM.SetupRatioTitle]).SetValue(part.SetupRatioTitle);
                ws.Cell(row, ci[CM.MasterSetupComment]).SetValue(part.MasterSetupComment);
                ws.Cell(row, ci[CM.ProductionRatioTitle]).SetValue(part.ProductionRatioTitle);
                ws.Cell(row, ci[CM.MasterProductionComment]).SetValue(part.MasterMachiningComment);
                ws.Cell(row, ci[CM.MasterComment]).SetValue(part.MasterComment);
                ws.Cell(row, ci[CM.FixedSetupTimePlan]).SetValue(part.FixedSetupTimePlan);
                ws.Cell(row, ci[CM.FixedProductionTimePlan]).SetValue(part.FixedProductionTimePlan);
                ws.Cell(row, ci[CM.EngineerComment]).SetValue(part.EngineerComment);
                row++;
            }
            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Columns().AdjustToContents();


            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            ws.Columns().AdjustToContents();

            ws.Column(ci[CM.Operator]).Width = 15;

            ws.Column(ci[CM.Part]).Width = 25;

            ws.Range(3, ci[CM.OperatorComment], row, ci[CM.OperatorComment])
               .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Column(ci[CM.OperatorComment]).Width = 35;

            ws.Column(ci[CM.MasterSetupComment]).Width = 20;
            ws.Column(ci[CM.MasterProductionComment]).Width = 20;
            ws.Column(ci[CM.MasterComment]).Width = 20;
            ws.Row(1).Delete();
            ws.SheetView.FreezeRows(1);
        }

        /// <summary>
        /// Отчёт за период с использованием EPPlus
        /// </summary>
        /// <param name="parts"></param>
        /// <param name="fromDate"></param>
        /// <param name="toDate"></param>
        /// <param name="path"></param>
        /// <param name="underOverBorder"></param>
        /// <returns></returns>
        //public static void AddDiagramToReportForPeriod(string path)
        //{
        //    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        //    FileInfo fileInfo = new FileInfo(path);
        //    using (var package = new ExcelPackage(fileInfo))
        //    {
        //        var ws = package.Workbook.Worksheets[0];
        //        var range = ws.Cells["B18:G18"];
        //        var headers = ws.Cells["B2:G2"];

        //        var chart = ws.Drawings.AddChart("Эффективность работы", eChartType.PieExploded) as ExcelPieChart;
        //        var series = chart?.Series.Add(range, headers);
        //        chart!.Legend.Position = eLegendPosition.Left;
        //        chart!.DataLabel.Format = "0.00%";
        //        chart!.DataLabel.ShowValue = true;
        //        chart!.DataLabel.ShowCategory = true;
        //        chart!.DataLabel.ShowLegendKey = true;
        //        chart!.DataLabel.Position = eLabelPosition.BestFit;
        //        chart!.DataLabel.ShowLeaderLines = true;
        //        chart!.DataLabel.SourceLinked = false;

        //        chart.SetPosition(20, 0, 0, 0);
        //        chart.SetSize(1000, 800);

        //        // Заголовок диаграммы
        //        chart.Title.Text = "Работа оборудования";

        //        // Сохраняем изменения
        //        package.Save();
        //    }

        //}


        /// Экспорт отчета по операторам
        /// </summary>
        /// <param name="parts">Отметки по которым будут производиться расчеты</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="path">Путь к формируемому файлу</param>
        /// <param name="minPartsCount">Минимальное количество деталей</param>
        /// <param name="maxPartsCount">Максимальное количество деталей</param>
        /// <param name="serialParts">Серийные детали (опционально)</param>
        /// <returns>При удачном выполнении возвращает путь к записанному файлу</returns>
        public static async Task<string> ExportOperatorReportAsync(IEnumerable<Part> parts, DateTime fromDate, DateTime toDate,
            string path, int minPartsCount, int maxPartsCount, HashSet<string>? serialParts = null, IProgress<string>? progress = null)
        {
            // ============================================================================
            // ВАЛИДАЦИЯ ВХОДНЫХ ПАРАМЕТРОВ
            // ============================================================================
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Путь не может быть пустым", nameof(path));
            if (fromDate > toDate) throw new ArgumentException("Начальная дата не может быть позже конечной");
            if (minPartsCount < 0) throw new ArgumentException("Минимальное количество деталей не может быть отрицательным");
            if (maxPartsCount < minPartsCount) throw new ArgumentException("Максимальное количество не может быть меньше минимального");

            // ============================================================================
            // ЗАГРУЗКА СПРАВОЧНЫХ ДАННЫХ
            // ============================================================================
            var operatorsTask = Database.GetOperatorsAsync();
            var qualificationsTask = Database.GetQualificationsAsync();
            await Task.WhenAll(operatorsTask, qualificationsTask);
            var operators = await operatorsTask;
            var qualifications = await qualificationsTask;
            
            var workDays = Util.GetWorkDaysBeetween(fromDate, toDate);

            // ============================================================================
            // ФИЛЬТРАЦИЯ ДАННЫХ
            // ============================================================================
            var filteredParts = parts.Where(p => !p.ExcludeFromReports).ToList();
            var onlySerial = serialParts?.Any() == true;

            if (onlySerial)
            {
                filteredParts = filteredParts
                    .Where(p => serialParts!.Contains(p.PartName.NormalizedPartNameWithoutComments()))
                    .ToList();
            }

            // ============================================================================
            // СОЗДАНИЕ И НАСТРОЙКА КНИГИ
            // ============================================================================
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Отчет по операторам");

            var cm = new CM.Builder()
                .Add(CM.Operator)
                .Add(CM.Qualification)
                .Add(CM.Machine)
                .Add(CM.SerialOrders, "Не штучный")
                .Add(CM.SetupRatio, "Наладка средняя")
                .Add(CM.ProductionRatio, "Изготовление общее")
                .Add(CM.AverageReplacementTime)
                .Add(CM.CreateNcProgramTime)
                .Add(CM.MaintenanceTime)
                .Add(CM.ToolSearchingTime)
                .Add(CM.ToolChangingTime)
                .Add(CM.MentoringTime)
                .Add(CM.ContactingDepartmentsTime)
                .Add(CM.FixtureMakingTime)
                .Add(CM.HardwareFailureTime)
                .Add(CM.SpecifiedDowntimes)
                .Add(CM.SpecialDowntimeTime)
                .Add(CM.SpecifiedDowntimesEx, $"Простои{Environment.NewLine}(без отказа оборудования и обучения)")
                .Add(CM.GeneralRatio)
                .Add(CM.SetupsCount)
                .Add(CM.ProductionsCount)
                .Add(CM.WorkedShifts)
                .Add(CM.IncludedOperationsTime)
                .Add(CM.TotalTime)
                .Add(CM.EfficiencyCoefficient)
                .Add(CM.DowntimesCoefficient)
                .Add(CM.Coefficient)
                .Build();

            var ci = cm.GetIndexes();
            ConfigureWorksheetHeader(ws, cm);
            var coeffWs = wb.AddWorksheet("Коэффициенты");
            CreateCoefficientsWorksheet(coeffWs, qualifications);

            // ============================================================================
            // ЗАПОЛНЕНИЕ ДАННЫХ ПО ОПЕРАТОРАМ
            // ============================================================================
            var row = 3;

            foreach (var partGroup in filteredParts
                .GroupBy(p => new { p.Operator, p.Machine })
                .OrderBy(g => g.Key.Machine)
                .ThenBy(g => g.Key.Operator))
            {
                if (partGroup.Key.Operator.ToLower() == "ученик") continue;

                var isSerialMachine = await Database.GetMachineSerialStatus(partGroup.Key.Machine);
                var groupParts = partGroup.ToList();
                

                // ------------------------------------------------------------------------
                // Основные данные оператора
                // ------------------------------------------------------------------------
                ws.Cell(row, ci[CM.Operator]).SetValue(partGroup.Key.Operator);

                var qualification = partGroup.Key.Operator.GetOperatorQualification(operators);
                var validQualification = int.TryParse(qualification, out int qualificationNumber);
                var qual = qualifications.First(q => q.Value == qualificationNumber);
                ws.Cell(row, ci[CM.Qualification]).SetValue(validQualification ? qualificationNumber : qualification);
                ws.Cell(row, ci[CM.Machine]).SetValue(groupParts.First().Machine);
                ws.Cell(row, ci[CM.SerialOrders]).SetValue(isSerialMachine ? 1 : 0)
                    .Style.NumberFormat.SetFormat("\"✓\";;\"✗\"")
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                // ------------------------------------------------------------------------
                // Коэффициенты наладки и изготовления
                // ------------------------------------------------------------------------
                var averageSetupRatio = groupParts.AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatio])
                    .SetValue(averageSetupRatio)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                var productionRatio = groupParts
                    .Where(p => p.FinishedCountFact >= minPartsCount && p.FinishedCountFact < maxPartsCount)
                    .ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatio])
                    .SetValue(productionRatio)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                // ------------------------------------------------------------------------
                // Счетчики наладок и изготовлений
                // ------------------------------------------------------------------------
                var setupsCount = groupParts.Count(p =>
                    p.SetupRatio is not (0 or double.NaN or double.NegativeInfinity or double.PositiveInfinity));
                var productionsCount = groupParts.Count(p =>
                    p.ProductionRatio != 0 && p.FinishedCountFact >= minPartsCount && p.FinishedCountFact < maxPartsCount);

                ws.Cell(row, ci[CM.SetupsCount]).SetValue(setupsCount);
                ws.Cell(row, ci[CM.ProductionsCount]).SetValue(productionsCount);

                // ------------------------------------------------------------------------
                // Общий коэффициент (формула в зависимости от типа станка)
                // ------------------------------------------------------------------------
                string setupRatioAddr = ws.Cell(row, ci[CM.SetupRatio]).Address.ToStringRelative();
                string productionRatioAddr = ws.Cell(row, ci[CM.ProductionRatio]).Address.ToStringRelative();
                string setupsCountAddr = ws.Cell(row, ci[CM.SetupsCount]).Address.ToStringRelative();
                string productionsCountAddr = ws.Cell(row, ci[CM.ProductionsCount]).Address.ToStringRelative();
                string qualificationAddr = ws.Cell(row, ci[CM.Qualification]).Address.ToStringRelative();
                string efficiencyCoeffAddr = ws.Cell(row, ci[CM.EfficiencyCoefficient]).Address.ToStringRelative();
                string downtimeCoeffAddr = ws.Cell(row, ci[CM.DowntimesCoefficient]).Address.ToStringRelative();

                if (isSerialMachine)
                {
                    // Для серийных станков: взвешенное среднее или только изготовление для 1 разряда
                    string generalRatioFormula =
                        $"=IF({qualificationAddr}=1,{productionRatioAddr}," +
                        $"IFERROR(({setupsCountAddr}*{setupRatioAddr}+{productionsCountAddr}*{productionRatioAddr})/({setupsCountAddr}+{productionsCountAddr}),0))";

                    ws.Cell(row, ci[CM.GeneralRatio]).FormulaA1 = generalRatioFormula;
                }
                else
                {
                    // Для несерийных станков: только наладка
                    ws.Cell(row, ci[CM.GeneralRatio]).FormulaA1 = $"={setupRatioAddr}";
                }
                ws.Cell(row, ci[CM.GeneralRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                // ------------------------------------------------------------------------
                // Время замены и простои
                // ------------------------------------------------------------------------
                ws.Cell(row, ci[CM.AverageReplacementTime])
                    .SetValue(groupParts.AverageReplacementTimeRatio())
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

                ws.Cell(row, ci[CM.CreateNcProgramTime]).SetValue(groupParts.Sum(p => p.CreateNcProgramTime));
                ws.Cell(row, ci[CM.MaintenanceTime]).SetValue(groupParts.Sum(p => p.MaintenanceTime));
                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(groupParts.Sum(p => p.ToolSearchingTime));
                ws.Cell(row, ci[CM.ToolChangingTime]).SetValue(groupParts.Sum(p => p.ToolChangingTime));
                ws.Cell(row, ci[CM.MentoringTime]).SetValue(groupParts.Sum(p => p.MentoringTime));
                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).SetValue(groupParts.Sum(p => p.ContactingDepartmentsTime));
                ws.Cell(row, ci[CM.FixtureMakingTime]).SetValue(groupParts.Sum(p => p.FixtureMakingTime));
                ws.Cell(row, ci[CM.HardwareFailureTime]).SetValue(groupParts.Sum(p => p.HardwareFailureTime));
                ws.Cell(row, ci[CM.SpecialDowntimeTime]).SetValue(groupParts.Sum(p => p.SpecialDowntimeTime));

                ws.Cell(row, ci[CM.SpecifiedDowntimes])
                    .SetValue(groupParts.SpecifiedDowntimesRatio(ShiftType.All))
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                var specDowntimesEx = groupParts.SpecifiedDowntimesRatioExcluding(new[] { Downtime.HardwareFailure, Downtime.Mentoring, Downtime.Special });
                ws.Cell(row, ci[CM.SpecifiedDowntimesEx])
                    .SetValue(specDowntimesEx)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                // ------------------------------------------------------------------------
                // Отработанные смены
                // ------------------------------------------------------------------------
                var workedShifts = parts
                    .Where(p => p.Operator == partGroup.Key.Operator && p.Machine == partGroup.Key.Machine)
                    .Select(p => p.ShiftDate)
                    .Distinct()
                    .Count();
                ws.Cell(row, ci[CM.WorkedShifts]).SetValue(workedShifts);

                (TimeSpan totalTime, TimeSpan includedOperationsTime) = parts
                .Where(p => p.Operator == partGroup.Key.Operator && p.Machine == partGroup.Key.Machine)
                .Aggregate(
                    (Total: TimeSpan.Zero, Excluded: TimeSpan.Zero),
                    (acc, p) => (
                        Total: acc.Total + p.FullWorkedTime(),
                        Excluded: acc.Excluded + NotExcludedTime(p, minPartsCount, maxPartsCount)
                    )
                );

                ws.Cell(row, ci[CM.IncludedOperationsTime]).SetValue(includedOperationsTime.TotalHours)
                    .Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.Integer);
                ws.Cell(row, ci[CM.TotalTime]).SetValue(totalTime.TotalHours)
                    .Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.Integer);

                // ------------------------------------------------------------------------
                // Формула итогового коэффициента
                // ------------------------------------------------------------------------
                if (validQualification)
                {
                    string generalRatioAddr = ws.Cell(row, ci[CM.GeneralRatio]).Address.ToStringRelative();
                    string specDowntimesExAddr = ws.Cell(row, ci[CM.SpecifiedDowntimesEx]).Address.ToStringRelative();
                    string workedShiftsAddr = ws.Cell(row, ci[CM.WorkedShifts]).Address.ToStringRelative();
                    string includedOperationsTimeAddr = ws.Cell(row, ci[CM.IncludedOperationsTime]).Address.ToStringRelative();
                    string totalTimeAddr = ws.Cell(row, ci[CM.TotalTime]).Address.ToStringRelative();

                    // Находим правильную строку в листе коэффициентов для текущей квалификации
                    var qualRow = qualifications.OrderBy(q => q.Value).ToList().IndexOf(qual) + 2; // +2 потому что первая строка - заголовки, и индексация с 1

                    // Создаем ссылки на ячейки с коэффициентами для текущей квалификации
                    string effHH = isSerialMachine ? $"Коэффициенты!B{qualRow}" : $"Коэффициенты!Z{qualRow}";
                    string effH = isSerialMachine ? $"Коэффициенты!C{qualRow}" : $"Коэффициенты!AA{qualRow}";
                    string effN = isSerialMachine ? $"Коэффициенты!D{qualRow}" : $"Коэффициенты!AB{qualRow}";
                    string effL = isSerialMachine ? $"Коэффициенты!E{qualRow}" : $"Коэффициенты!AC{qualRow}";
                    string effLL = isSerialMachine ? $"Коэффициенты!F{qualRow}" : $"Коэффициенты!AD{qualRow}";
                    string effLLL = isSerialMachine ? $"Коэффициенты!G{qualRow}" : $"Коэффициенты!AE{qualRow}";
                    string effCoeffHH = isSerialMachine ? $"Коэффициенты!H{qualRow}" : $"Коэффициенты!AF{qualRow}";
                    string effCoeffH = isSerialMachine ? $"Коэффициенты!I{qualRow}" : $"Коэффициенты!AG{qualRow}";
                    string effCoeffN = isSerialMachine ? $"Коэффициенты!J{qualRow}" : $"Коэффициенты!AH{qualRow}";
                    string effCoeffL = isSerialMachine ? $"Коэффициенты!K{qualRow}" : $"Коэффициенты!AI{qualRow}";
                    string effCoeffLL = isSerialMachine ? $"Коэффициенты!L{qualRow}" : $"Коэффициенты!AJ{qualRow}";
                    string effCoeffLLL = isSerialMachine ? $"Коэффициенты!M{qualRow}" : $"Коэффициенты!AK{qualRow}";

                    string downHH = $"Коэффициенты!N{qualRow}";
                    string downH = $"Коэффициенты!O{qualRow}";
                    string downN = $"Коэффициенты!P{qualRow}";
                    string downL = $"Коэффициенты!Q{qualRow}";
                    string downLL = $"Коэффициенты!R{qualRow}";
                    string downLLL = $"Коэффициенты!S{qualRow}";
                    string downCoeffHH = $"Коэффициенты!T{qualRow}";
                    string downCoeffH = $"Коэффициенты!U{qualRow}";
                    string downCoeffN = $"Коэффициенты!V{qualRow}";
                    string downCoeffL = $"Коэффициенты!W{qualRow}";
                    string downCoeffLL = $"Коэффициенты!X{qualRow}";
                    string downCoeffLLL = $"Коэффициенты!Y{qualRow}";

                    if (partGroup.Key.Machine.Contains("SKT21"))
                    {
                        downN = 0.15.ToString("#.##");
                    }

                    // Коэффициент эффективности (на основе общего коэффициента)
                    string efficiencyCoeff =
                        $"IF({generalRatioAddr}>{effHH},{effCoeffHH}," +
                        $"IF({generalRatioAddr}>{effH},{effCoeffH}," +
                        $"IF({generalRatioAddr}>{effN},{effCoeffN}," +
                        $"IF({generalRatioAddr}>{effL},{effCoeffL}," +
                        $"IF({generalRatioAddr}>{effLL},{effCoeffLL}," +
                        $"IF({generalRatioAddr}>{effLLL},{effCoeffLLL}," +
                        $"{effCoeffLLL}))))))";
                    efficiencyCoeff = $"=IF(AND({setupRatioAddr}>=0.8,{workedShiftsAddr}>={workDays / 6},{includedOperationsTimeAddr}>={totalTimeAddr}*0.5),{efficiencyCoeff},\"\")";

                    ws.Cell(row, ci[CM.EfficiencyCoefficient]).FormulaA1 = efficiencyCoeff;

                    // Коэффициент простоев
                    // Для простоев условие обратное: чем МЕНЬШЕ простой, тем ВЫШЕ коэффициент
                    string downtimeCoeff =
                        $"IF({specDowntimesExAddr}<{downHH},{downCoeffHH}," +
                        $"IF({specDowntimesExAddr}<{downH},{downCoeffH}," +
                        $"IF({specDowntimesExAddr}<{downN},{downCoeffN}," +
                        $"IF({specDowntimesExAddr}<{downL},{downCoeffL}," +
                        $"IF({specDowntimesExAddr}<{downLL},{downCoeffLL}," +
                        $"IF({specDowntimesExAddr}<{downLLL},{downCoeffLLL}," +
                        $"{downCoeffLLL}))))))";

                    downtimeCoeff = $"=IF(AND({setupRatioAddr}>=0.8,{workedShiftsAddr}>={workDays / 6},{includedOperationsTimeAddr}>={totalTimeAddr}*0.5),{downtimeCoeff},\"\")";


                    ws.Cell(row, ci[CM.DowntimesCoefficient]).FormulaA1 = downtimeCoeff;

                    var reasons = new List<string>();
                    if (averageSetupRatio < 0.8) reasons.Add("Наладка менее 80%");
                    if (workedShifts < workDays / 6) reasons.Add($"Недостаточно смен (минимум {workDays / 6});");
                    if (includedOperationsTime * 2 < totalTime) reasons.Add($"Больше половины отработанного времени не учитывается.");

                    // Итоговая формула: коэффициент применяется только при выполнении условий
                    string coefficientFormula = $"=IFERROR({efficiencyCoeffAddr}*{downtimeCoeffAddr},\"\")";
                    ws.Cell(row, ci[CM.Coefficient]).FormulaA1 = coefficientFormula;
                    if (reasons.Count > 0) ws.Cell(row, ci[CM.Coefficient]).CreateComment().AddText($"Причины:\n{string.Join('\n', reasons)}");
                }

                row++;
            }

            // ============================================================================
            // ФОРМАТИРОВАНИЕ ЛИСТА
            // ============================================================================
            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            ws.Columns().AdjustToContents();
            ws.Column(ci[CM.Qualification]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Column(ci[CM.Qualification]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Columns(ci[CM.CreateNcProgramTime], ci[CM.SpecifiedDowntimes]).Group(true);

            // Заголовок отчета
            ws.Cell(1, 1).Value =
                $"Отчёт по операторам за период с {fromDate:dd.MM.yyyy} по {toDate:dd.MM.yyyy} " +
                $"(изготовление от {minPartsCount}{(maxPartsCount == int.MaxValue ? "" : $" до {maxPartsCount}")} шт." +
                $"{(onlySerial ? " (Только серийка)" : "")})";
            ws.Range(1, ci[CM.Operator], 1, cm.Count).Merge();
            ws.Range(1, ci[CM.Operator], 1, 1).Style.Font.FontSize = 14;
            ws.Columns(ci[CM.SetupRatio], cm.Count).Width = 7;

            // ============================================================================
            // СОХРАНЕНИЕ И ОТКРЫТИЕ ФАЙЛА
            // ============================================================================
            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

            return path;
        }

        /// <summary>
        /// Создает лист с коэффициентами для всех квалификаций
        /// </summary>
        private static void CreateCoefficientsWorksheet(IXLWorksheet ws, IEnumerable<Qualification> qualifications)
        {
            // Заголовки столбцов
            ws.Cell("A1").SetValue("Квалификация");

            // Эффективность - пороги
            ws.Cell("B1").SetValue("EffHH_Value");
            ws.Cell("C1").SetValue("EffH_Value");
            ws.Cell("D1").SetValue("EffN_Value");
            ws.Cell("E1").SetValue("EffL_Value");
            ws.Cell("F1").SetValue("EffLL_Value");
            ws.Cell("G1").SetValue("EffLLL_Value");

            // Эффективность - коэффициенты
            ws.Cell("H1").SetValue("EffHH_Coeff");
            ws.Cell("I1").SetValue("EffH_Coeff");
            ws.Cell("J1").SetValue("EffN_Coeff");
            ws.Cell("K1").SetValue("EffL_Coeff");
            ws.Cell("L1").SetValue("EffLL_Coeff");
            ws.Cell("M1").SetValue("EffLLL_Coeff");

            // Простои - пороги
            ws.Cell("N1").SetValue("DownHH_Value");
            ws.Cell("O1").SetValue("DownH_Value");
            ws.Cell("P1").SetValue("DownN_Value");
            ws.Cell("Q1").SetValue("DownL_Value");
            ws.Cell("R1").SetValue("DownLL_Value");
            ws.Cell("S1").SetValue("DownLLL_Value");

            // Простои - коэффициенты
            ws.Cell("T1").SetValue("DownHH_Coeff");
            ws.Cell("U1").SetValue("DownH_Coeff");
            ws.Cell("V1").SetValue("DownN_Coeff");
            ws.Cell("W1").SetValue("DownL_Coeff");
            ws.Cell("X1").SetValue("DownLL_Coeff");
            ws.Cell("Y1").SetValue("DownLLL_Coeff");

            // Эффективность - пороги (не серийные)
            ws.Cell("Z1").SetValue("NEffHH_Value");
            ws.Cell("AA1").SetValue("NEffH_Value");
            ws.Cell("AB1").SetValue("NEffN_Value");
            ws.Cell("AC1").SetValue("NEffL_Value");
            ws.Cell("AD1").SetValue("NEffLL_Value");
            ws.Cell("AE1").SetValue("NEffLLL_Value");

            // Эффективность - коэффициенты (не серийные)
            ws.Cell("AF1").SetValue("NEffHH_Coeff");
            ws.Cell("AG1").SetValue("NEffH_Coeff");
            ws.Cell("AH1").SetValue("NEffN_Coeff");
            ws.Cell("AI1").SetValue("NEffL_Coeff");
            ws.Cell("AJ1").SetValue("NEffLL_Coeff");
            ws.Cell("AK1").SetValue("NEffLLL_Coeff");

            // Заполняем данные
            int row = 2;
            foreach (var qual in qualifications.OrderBy(q => q.Value))
            {
                ws.Cell(row, 1).SetValue(qual.Value); // Квалификация

                // Пороги эффективности
                ws.Cell(row, 2).SetValue(qual.EfficiencyValueHH);
                ws.Cell(row, 3).SetValue(qual.EfficiencyValueH);
                ws.Cell(row, 4).SetValue(qual.EfficiencyValueN);
                ws.Cell(row, 5).SetValue(qual.EfficiencyValueL);
                ws.Cell(row, 6).SetValue(qual.EfficiencyValueLL);
                ws.Cell(row, 7).SetValue(qual.EfficiencyValueLLL);

                // Коэффициенты эффективности
                ws.Cell(row, 8).SetValue(qual.EfficiencyCoefficientHH);
                ws.Cell(row, 9).SetValue(qual.EfficiencyCoefficientH);
                ws.Cell(row, 10).SetValue(qual.EfficiencyCoefficientN);
                ws.Cell(row, 11).SetValue(qual.EfficiencyCoefficientL);
                ws.Cell(row, 12).SetValue(qual.EfficiencyCoefficientLL);
                ws.Cell(row, 13).SetValue(qual.EfficiencyCoefficientLLL);

                // Пороги простоев
                ws.Cell(row, 14).SetValue(qual.DownTimesValueHH);
                ws.Cell(row, 15).SetValue(qual.DownTimesValueH);
                ws.Cell(row, 16).SetValue(qual.DownTimesValueN);
                ws.Cell(row, 17).SetValue(qual.DownTimesValueL);
                ws.Cell(row, 18).SetValue(qual.DownTimesValueLL);
                ws.Cell(row, 19).SetValue(qual.DownTimesValueLLL);

                // Коэффициенты простоев
                ws.Cell(row, 20).SetValue(qual.DownTimesCoefficientHH);
                ws.Cell(row, 21).SetValue(qual.DownTimesCoefficientH);
                ws.Cell(row, 22).SetValue(qual.DownTimesCoefficientN);
                ws.Cell(row, 23).SetValue(qual.DownTimesCoefficientL);
                ws.Cell(row, 24).SetValue(qual.DownTimesCoefficientLL);
                ws.Cell(row, 25).SetValue(qual.DownTimesCoefficientLLL);

                // Пороги эффективности (не серийные)
                ws.Cell(row, 26).SetValue(qual.NonSerialEfficiencyValueHH);
                ws.Cell(row, 27).SetValue(qual.NonSerialEfficiencyValueH);
                ws.Cell(row, 28).SetValue(qual.NonSerialEfficiencyValueN);
                ws.Cell(row, 29).SetValue(qual.NonSerialEfficiencyValueL);
                ws.Cell(row, 30).SetValue(qual.NonSerialEfficiencyValueLL);
                ws.Cell(row, 31).SetValue(qual.NonSerialEfficiencyValueLLL);

                // Коэффициенты эффективности (не серийные)
                ws.Cell(row, 32).SetValue(qual.NonSerialEfficiencyCoefficientHH);
                ws.Cell(row, 33).SetValue(qual.NonSerialEfficiencyCoefficientH);
                ws.Cell(row, 34).SetValue(qual.NonSerialEfficiencyCoefficientN);
                ws.Cell(row, 35).SetValue(qual.NonSerialEfficiencyCoefficientL);
                ws.Cell(row, 36).SetValue(qual.NonSerialEfficiencyCoefficientLL);
                ws.Cell(row, 37).SetValue(qual.NonSerialEfficiencyCoefficientLLL);
                row++;
            }

            // Форматируем заголовки
            var headerRange = ws.Range("A1:Y1");
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        /// <summary>
        /// Экспортирует отчет по сменам операторов в Excel.
        /// </summary>
        /// <param name="parts">Коллекция деталей, содержащих данные для отчета.</param>
        /// <param name="fromDate">Начальная дата периода для отчета.</param>
        /// <param name="toDate">Конечная дата периода для отчета.</param>
        /// <param name="path">Путь для сохранения отчета в виде файла Excel.</param>
        /// <returns>Строка сообщения, указывающая путь и успешность выполнения.</returns>
        /// <remarks>
        /// В отчете выводится количество смен для каждого оператора за указанный период. 
        /// Отчет формируется в виде Excel-файла с заголовками и форматированием.
        /// Если оператором указан "Ученик", то данные по нему не включаются в отчет.
        /// После успешного сохранения файла пользователю предлагается открыть его.
        /// </remarks>
        public static string ExportOperatorsShiftsReport(ICollection<Part> parts, DateTime fromDate, DateTime toDate, string path)
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Отчет по операторам");
            var columns = new Dictionary<string, (int index, string header)>
            {
                { "operator", (1, "Оператор") },
                { "shifts", (2, "Смены") },
            };
            var lastCol = columns.Count - 1;
            ConfigureWorksheetHeader(ws, columns, HeaderRotateOption.Horizontal);

            var row = 3;

            foreach (var partGroup in parts
                .Where(p => p.Operator != "Ученик")
                .GroupBy(p => p.Operator)
                .OrderBy(pg => pg.Key))
            {
                var distinctShiftsCount = partGroup.Select(pg => pg.ShiftDate).Distinct().Count();

                ws.Cell(row, columns["operator"].index)
                  .SetValue(partGroup.Key);

                ws.Cell(row, columns["shifts"].index)
                  .SetValue(distinctShiftsCount)
                  .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                row++;
            }
            var usedRange = ws.RangeUsed();
            usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            usedRange.SetAutoFilter(true);
            ws.Columns().AdjustToContents();
            ws.Cell(1, 1).Value = $"Отчёт по операторам за период с {fromDate.ToString(Constants.ShortDateFormat)} по {toDate.ToString(Constants.ShortDateFormat)}";
            wb.SaveAs(path);
            if (MessageBox.Show("Открыть сохраненный файл?", "Вопрос", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
            }

            return $"Файл сохранен в \"{path}\"";
        }

        public static string ExportHistory(ICollection<Part> parts, string path, int ordersCount)
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet($"История последних {ordersCount} заказов");

            ws.Style.Font.FontSize = 8;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var cm = new CM.Builder()
                .Add(CM.Machine)
                .Add(CM.Date)
                .Add(CM.Operator)
                .Add(CM.Part)
                .Add(CM.Finished)
                .Add(CM.Setup)
                .Add(CM.MachiningTime)
                .Add(CM.OperatorComment)
                .Add(CM.MasterComment)
                .Add(CM.EngineerComment)
                .Build();

            ConfigureWorksheetHeader(ws, cm, HeaderRotateOption.Vertical, 65, 8);

            ws.Range(2, 1, 2, cm.Count).Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);

            int row = 3;
            var lastOrders = parts
                .OrderBy(p => p.StartSetupTime)
                .Select(p => p.Order)
                .Distinct()
                .TakeLast(ordersCount)
                .ToList();

            var filteredParts = parts
                .Where(p => lastOrders.Contains(p.Order) && p.FinishedCount > 0)
                .OrderBy(p => p.Order)
                .ThenBy(p => p.StartSetupTime)
                .ToList();

            var ci = cm.GetIndexes();

            foreach (var order in lastOrders)
            {
                ws.Cell(row, 1).SetValue($"{order}");
                ws.Range(row, 1, row, cm.Count).Merge().Style.Font
                    .SetBold(true)
                    .Border.SetLeftBorder(XLBorderStyleValues.Thin)
                    .Border.SetRightBorder(XLBorderStyleValues.Thin)
                    .Border.SetTopBorder(XLBorderStyleValues.Medium)
                    .Border.SetBottomBorder(XLBorderStyleValues.Medium);
                row++;

                foreach (var part in filteredParts.Where(p => p.Order == order))
                {
                    ws.Cell(row, ci[CM.Machine]).SetValue(part.Machine);
                    ws.Cell(row, ci[CM.Date]).SetValue(part.ShiftDate);
                    ws.Cell(row, ci[CM.Operator]).SetValue(part.Operator);
                    ws.Cell(row, ci[CM.Part]).SetValue(part.PartName);
                    ws.Cell(row, ci[CM.Finished]).SetValue(part.FinishedCount);
                    ws.Cell(row, ci[CM.Setup]).SetValue(part.Setup);

                    if (part.MachiningTime != TimeSpan.Zero)
                        ws.Cell(row, ci[CM.MachiningTime]).SetValue(part.MachiningTime);

                    var comment = part.OperatorComment;
                    if (comment.Contains("Отмеченные простои:\n"))
                    {
                        comment = comment.Split("Отмеченные простои:\n")[0].Trim();
                    }

                    ws.Cell(row, ci[CM.OperatorComment]).SetValue(comment)
                        .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    ws.Cell(row, ci[CM.MasterComment]).SetValue(part.MasterComment);
                    ws.Cell(row, ci[CM.EngineerComment]).SetValue(part.EngineerComment);

                    var cells = ws.Range(row, 1, row, cm.Count).Style
                        .Border.SetLeftBorder(XLBorderStyleValues.Medium)
                        .Border.SetRightBorder(XLBorderStyleValues.Medium)
                        .Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    row++;
                }
            }

            ws.Columns().AdjustToContents();

            ws.Column(ci[CM.Machine]).Width = 13;
            ws.Column(ci[CM.Date]).Width = 8;
            ws.Column(ci[CM.Operator]).Width = 13;
            ws.Column(ci[CM.Part]).Width = 15;
            ws.Columns(ci[CM.Finished], ci[CM.Setup]).Width = 3;
            ws.Column(ci[CM.MachiningTime]).Width = 7;
            ws.Column(ci[CM.OperatorComment]).Width = 40;
            ws.Column(ci[CM.MasterComment]).Width = 20;
            ws.Column(ci[CM.EngineerComment]).Width = 20;

            ws.PageSetup.PrintAreas.Add(1, 1, row - 1, cm.Count);
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.Margins.SetLeft(0.3);
            ws.PageSetup.Margins.SetRight(0.2);
            ws.PageSetup.Margins.SetTop(0.4);
            ws.PageSetup.Margins.SetBottom(0.2);
            ws.Range(2, 1, 2, ci.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(row - 1, 1, row - 1, cm.Count).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            SetTitle(ws, ci.Count, "История изготовления", parts.Last().PartName, 14, BoldOption.Right);
            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
            }

            return path;
        }

        public static async Task<string> ExportToolSearchCasesAsync(ICollection<Part> parts, string path, IProgress<string> progress)
        {
            progress.Report("Создание книги");
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet($"Поиски истины");
            progress.Report("Получение данных из БД");
            var cases = await Database.GetToolSearchCasesAsync(parts.Select(p => p.Guid).ToList(), progress);


            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            progress.Report("Формирование столбцов");

            var cm = new CM.Builder()
                .Add(CM.Operator)
                .Add(CM.Type)
                .Add(CM.Description)
                .Add(CM.StartMachiningTime, "Начало")
                .Add(CM.EndMachiningTime, "Завершение")
                .Add(CM.ToolSearchingTime, "Затраченное время")
                .Add(CM.Part)
                .Add(CM.Machine)
                .Add(CM.IsSuccess, "Нашёл?")
                .Build();

            ConfigureWorksheetHeader(ws, cm, HeaderRotateOption.Horizontal, 65, 10);

            ws.Range(2, 1, 2, cm.Count).Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);

            int row = 3;

            var ci = cm.GetIndexes();


            foreach (var @case in cases)
            {
                ws.Cell(row, ci[CM.Operator]).SetValue(@case.Operator);
                ws.Cell(row, ci[CM.Part]).SetValue(@case.Part);
                ws.Cell(row, ci[CM.Machine]).SetValue(@case.Machine);
                ws.Cell(row, ci[CM.Type]).SetValue(@case.Type);
                ws.Cell(row, ci[CM.Description]).SetValue(@case.Description);
                ws.Cell(row, ci[CM.StartMachiningTime]).SetValue(@case.StartTime)
                    .Style.DateFormat.Format = "dd.MM.yy HH:mm";
                ws.Cell(row, ci[CM.EndMachiningTime]).SetValue(@case.EndTime)
                    .Style.DateFormat.Format = "dd.MM.yy HH:mm";
                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(@case.Time);
                ws.Cell(row, ci[CM.IsSuccess]).SetValue(@case.IsSuccess.HasValue ? @case.IsSuccess.Value ? "Да" : "Нет" : "Н/Д");

                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(ci[CM.Type]).Width = 25;
            ws.Column(ci[CM.Description]).Width = 60;
            ws.Column(ci[CM.Part]).Width = 60;
            ws.Column(ci[CM.StartMachiningTime]).Width = 12;
            ws.Column(ci[CM.EndMachiningTime]).Width = 12;
            ws.Column(ci[CM.ToolSearchingTime]).Width = 12;
            ws.Column(ci[CM.IsSuccess]).Width = 8;
            ws.Column(ci[CM.IsSuccess]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(2, ci[CM.StartMachiningTime], row, ci[CM.ToolSearchingTime]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(2, 1, row - 1, ci.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(2, 1, row - 1, cm.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            SetTitle(ws, ci.Count, "Поиски инструмента");
            progress.Report("Сохранение файла");
            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
            }

            return path;
        }

        public async static Task<string> ExportNormsAndWorkloadAnalysisAsync(ICollection<Part> parts, string path, IProgress<string> progress)
        {
            var wb = new XLWorkbook();
            var partsDict = parts
                .Select(p => new {
                    Part = p,
                    NormalizedName = p.PartName.NormalizedPartName()
                })
                .GroupBy(x => x.NormalizedName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Part).ToList()
                );

            // Выбор уникальных деталей, которые встречаются в 3+ заказах с количеством >= 50
            var totalUnique = partsDict
                .Where(kvp => kvp.Value
                    .Where(p => p.TotalCount >= 50)
                    .Select(p => p.Order)
                    .Distinct()
                    .Take(3)
                    .Count() == 3)
                .Select(kvp => kvp.Value.First())
                .ToList();

            var totalUniqueNames = totalUnique.Select(p => p.PartName.NormalizedPartName()).ToHashSet();

            Dictionary<string, int> serialPartsDict = (await libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!))
                .ToDictionary(p => p.PartName.NormalizedPartNameWithoutComments(), p => p.YearCount);

            var wsChanges = wb.AddWorksheet("Изменения нормативов");
            wsChanges.Style.Alignment.WrapText = false;
            progress.Report("Формирование листа \"Изменения нормативов\"");

            var changesBuilder = new CM.Builder()
                .Add(CM.Part)
                .Add(CM.YearCount)
                .Add(CM.Machine)
                .Add(CM.Setup)
                .Add(CM.Type)
                .Add(CM.Date)
                .Add(CM.OldValue)
                .Add(CM.ExcludedOperationsTime)
                .Add(CM.NewValue)
                .Add(CM.Change)
                .Add(CM.ChangeRatio)
                .Add(CM.OldYearValue)
                .Add(CM.NewYearValue)
                .Add(CM.YearChange)
                .Add(CM.YearChangeRatio)
                .Add(CM.SerialPerRuns)
                .Add(CM.SerialPerList)
                .Add(CM.IncreaseReason);

            var changesMap = changesBuilder.Build();
            ConfigureWorksheetHeader(wsChanges, changesMap, HeaderRotateOption.Horizontal, 65, 10);
            var changesCI = changesMap.GetIndexes();
            int changesRow = 3;

            var allChanges = new List<NormChange>();

            foreach (var partGroup in partsDict)
            {
                var partsForName = partGroup.Value;
                bool isInTotalUnique = totalUniqueNames.Contains(partGroup.Key);
                var normalizedPartName = partGroup.Key.NormalizedPartNameWithoutComments();
                bool isInSerialList = serialPartsDict.ContainsKey(normalizedPartName);

                var partsWithChanges = partsForName
                    .Where(p => (p.FixedSetupTimePlan != 0 && p.SetupTimePlan != 0) ||
                                (p.FixedProductionTimePlan != 0 && p.SingleProductionTimePlan != 0))
                    .OrderBy(p => p.EndMachiningTime)
                    .ToList();

                if (partsWithChanges.Count == 0)
                    continue;

                foreach (var changedPart in partsWithChanges)
                {
                    // наладка
                    if (changedPart.FixedSetupTimePlan != 0 && changedPart.SetupTimePlan != 0 &&
                        Math.Abs(changedPart.FixedSetupTimePlan - changedPart.SetupTimePlan) > 0.001)
                    {
                        allChanges.Add(new NormChange(
                            changedPart.PartName,
                            changedPart.Machine,
                            changedPart.Setup,
                            "Наладка",
                            changedPart.SetupTimePlan,
                            0.0,
                            changedPart.FixedSetupTimePlan,
                            changedPart.EndMachiningTime,
                            isInTotalUnique,
                            isInSerialList,
                            changedPart.IncreaseReason
                        ));
                    }

                    // изготовление
                    if (changedPart.FixedProductionTimePlan != 0 && changedPart.SingleProductionTimePlan != 0 &&
                        Math.Abs(changedPart.FixedProductionTimePlan - changedPart.SingleProductionTimePlan) > 0.001)
                    {
                        allChanges.Add(new NormChange(
                            changedPart.PartName,
                            changedPart.Machine,
                            changedPart.Setup,
                            "Изготовление",
                            changedPart.SingleProductionTimePlan,
                            changedPart.ExcludedOperationsTime,
                            changedPart.FixedProductionTimePlan,
                            changedPart.EndMachiningTime,
                            isInTotalUnique,
                            isInSerialList,
                            changedPart.IncreaseReason
                        ));
                    }
                }
            }
            var uniqueChanges = allChanges
                .GroupBy(c => new
                {
                    c.PartName,
                    c.Machine,
                    c.Setup,
                    c.ChangeType,
                    c.OldValue,
                    c.ExcludedOperationsTime,
                    c.NewValue,
                    c.IsInTotalUnique,
                    c.IsInSerialList,
                    c.IncreaseReason
                })
                .Select(g => g.OrderByDescending(c => c.Date).First())
                .ToList();

            foreach (var change in uniqueChanges)
            {
                wsChanges.Cell(changesRow, changesCI[CM.Part]).Value = change.PartName;

                var key = change.PartName.NormalizedPartNameWithoutComments();
                if (!serialPartsDict.TryGetValue(key, out var yearCount))
                {
                    wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Value = "Н/Д";
                }
                else
                {
                    wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Value = yearCount == 0 ? 1 : yearCount;
                }

                wsChanges.Cell(changesRow, changesCI[CM.Machine]).Value = change.Machine;
                wsChanges.Cell(changesRow, changesCI[CM.Setup]).Value = change.Setup;
                wsChanges.Cell(changesRow, changesCI[CM.Type]).Value = change.ChangeType;
                wsChanges.Cell(changesRow, changesCI[CM.Date]).Value = change.Date;
                wsChanges.Cell(changesRow, changesCI[CM.OldValue]).Value = change.OldValue;
                wsChanges.Cell(changesRow, changesCI[CM.ExcludedOperationsTime]).Value = change.ExcludedOperationsTime;
                wsChanges.Cell(changesRow, changesCI[CM.NewValue]).Value = change.NewValue;
                
                var oldYearValueFormula = $"({wsChanges.Cell(changesRow, changesCI[CM.OldValue]).Address.ToStringRelative()}" +
                    $"+{wsChanges.Cell(changesRow, changesCI[CM.ExcludedOperationsTime]).Address.ToStringRelative()})" +
                    $"*{(yearCount != 0 ? (change.ChangeType == "Изготовление" ? wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Address.ToStringRelative() : 1) : 1)}";

                wsChanges.Cell(changesRow, changesCI[CM.OldYearValue])
                    .FormulaA1 = $"={oldYearValueFormula}";

                var newYearValueFormula = $"({wsChanges.Cell(changesRow, changesCI[CM.NewValue]).Address.ToStringRelative()}" +
                    $"*{(yearCount != 0 ? (change.ChangeType == "Изготовление" ? wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Address.ToStringRelative() : 1) : 1)})";

                wsChanges.Cell(changesRow, changesCI[CM.NewYearValue])
                    .FormulaA1 = $"={newYearValueFormula}";

                wsChanges.Cell(changesRow, changesCI[CM.SerialPerRuns]).Value = change.IsInTotalUnique;
                wsChanges.Cell(changesRow, changesCI[CM.SerialPerList]).Value = change.IsInSerialList;

                var newValueCell = wsChanges.Cell(changesRow, changesCI[CM.NewValue]).Address;
                var oldValueCell = wsChanges.Cell(changesRow, changesCI[CM.OldValue]).Address;
                var excludedOperationsTimeCell = wsChanges.Cell(changesRow, changesCI[CM.ExcludedOperationsTime]).Address.ToStringRelative();

                var newYearValueCell = wsChanges.Cell(changesRow, changesCI[CM.NewYearValue]).Address.ToStringRelative();
                var oldYearValueCell = wsChanges.Cell(changesRow, changesCI[CM.OldYearValue]).Address.ToStringRelative();

                wsChanges.Cell(changesRow, changesCI[CM.Change]).FormulaA1 = $"={newValueCell}-({oldValueCell}+{excludedOperationsTimeCell})";
                wsChanges.Cell(changesRow, changesCI[CM.ChangeRatio]).FormulaA1 = $"=IF({oldValueCell}+{excludedOperationsTimeCell}=0,0,{newValueCell}/({oldValueCell}+{excludedOperationsTimeCell})-1)";

                wsChanges.Cell(changesRow, changesCI[CM.YearChange]).FormulaA1 = $"={newYearValueCell}-{oldYearValueFormula}";
                wsChanges.Cell(changesRow, changesCI[CM.YearChangeRatio]).FormulaA1 = $"=IF({oldYearValueFormula}=0,0,({newYearValueCell}/({oldYearValueFormula}))-1)";

                wsChanges.Cell(changesRow, changesCI[CM.IncreaseReason]).Value = change.IncreaseReason;

                changesRow++;
            }

            if (changesRow > 3)
            {
                wsChanges.Range(2, 1, changesRow - 1, changesCI.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                wsChanges.Range(2, 1, changesRow - 1, changesCI.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                wsChanges.Range(3, changesCI[CM.ChangeRatio], changesRow - 1, changesCI[CM.ChangeRatio]).Style.NumberFormat.Format = "0.00%";
                wsChanges.Range(3, changesCI[CM.YearChangeRatio], changesRow - 1, changesCI[CM.YearChangeRatio]).Style.NumberFormat.Format = "0.00%";

                var dateRange = wsChanges.Range(3, changesCI[CM.Date], changesRow - 1, changesCI[CM.Date]);
                dateRange.Style.NumberFormat.Format = Constants.ShortDateFormat;

                var valueColumns = new[] { CM.OldValue, CM.ExcludedOperationsTime, CM.NewValue, CM.Change, CM.OldYearValue, CM.NewYearValue, CM.YearChange };
                foreach (var colName in valueColumns)
                {
                    var valueRange = wsChanges.Range(3, changesCI[colName], changesRow - 1, changesCI[colName]);
                    valueRange.Style.NumberFormat.Format = "0.00";
                }

                ApplyConditionalFormatting(wsChanges, changesRow, changesCI);
            }

            wsChanges.Columns().AdjustToContents();
            var table = wsChanges.RangeUsed().CreateTable();
            table.Name = "ИзмененияНормативов";
            table.Theme = XLTableTheme.TableStyleLight1;

            CalculateTotals(wsChanges, changesRow, changesCI);

            SetTitle(wsChanges, changesCI.Count, "История изменений нормативов");
            wsChanges.Column(changesCI[CM.Part]).Width = 70;
            wsChanges.Column(changesCI[CM.YearCount]).Width = 12;
            wsChanges.Columns(changesCI[CM.OldValue], changesCI[CM.SerialPerList]).Width = 14;
            wsChanges.Column(changesCI[CM.IncreaseReason]).Width = 70;

            progress.Report("Сохранение файла");
            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
            }
            return path;
        }

        private static void ApplyConditionalFormatting(IXLWorksheet ws, int lastRow, Dictionary<string, int> columnIndexes)
        {
            var changeColRange = ws.Range(3, columnIndexes[CM.Change], lastRow - 1, columnIndexes[CM.Change]);
            var cfGreenChange = changeColRange.AddConditionalFormat();
            cfGreenChange.WhenLessThan(0).Fill.BackgroundColor = _lightGreen;

            var cfRedChange = changeColRange.AddConditionalFormat();
            cfRedChange.WhenGreaterThan(0).Fill.BackgroundColor = _lightRed;

            var percentColRange = ws.Range(3, columnIndexes[CM.ChangeRatio], lastRow - 1, columnIndexes[CM.ChangeRatio]);
            var cfPercentGreen = percentColRange.AddConditionalFormat();
            cfPercentGreen.WhenLessThan(0).Fill.BackgroundColor = _lightGreen;

            var cfPercentRed = percentColRange.AddConditionalFormat();
            cfPercentRed.WhenGreaterThan(0).Fill.BackgroundColor = _lightRed;

            var yearChangeColRange = ws.Range(3, columnIndexes[CM.YearChange], lastRow - 1, columnIndexes[CM.YearChange]);
            var cfGreenYearChange = yearChangeColRange.AddConditionalFormat();
            cfGreenYearChange.WhenLessThan(0).Fill.BackgroundColor = _lightGreen;

            var cfRedYearChange = yearChangeColRange.AddConditionalFormat();
            cfRedYearChange.WhenGreaterThan(0).Fill.BackgroundColor = _lightRed;

            var percentYearColRange = ws.Range(3, columnIndexes[CM.YearChangeRatio], lastRow - 1, columnIndexes[CM.YearChangeRatio]);
            var cfPercentYearGreen = percentYearColRange.AddConditionalFormat();
            cfPercentYearGreen.WhenLessThan(0).Fill.BackgroundColor = _lightGreen;

            var cfPercentYearRed = percentYearColRange.AddConditionalFormat();
            cfPercentYearRed.WhenGreaterThan(0).Fill.BackgroundColor = _lightRed;
        }

        private static void CalculateTotals(IXLWorksheet ws, int lastRow, Dictionary<string, int> columnIndexes)
        {
            var oldAddress = ws.Range(2, columnIndexes[CM.OldValue], lastRow - 1, columnIndexes[CM.OldValue]).RangeAddress.ToStringRelative();
            var newAddress = ws.Range(2, columnIndexes[CM.NewValue], lastRow - 1, columnIndexes[CM.NewValue]).RangeAddress.ToStringRelative();

            ws.Cell(lastRow, columnIndexes[CM.OldValue]).FormulaA1 = $"=SUBTOTAL(109, {oldAddress})";
            ws.Cell(lastRow, columnIndexes[CM.NewValue]).FormulaA1 = $"=SUBTOTAL(109, {newAddress})";

            var sumOldValueCell = ws.Cell(lastRow, columnIndexes[CM.OldValue]).Address.ToStringRelative();
            var sumNewValueCell = ws.Cell(lastRow, columnIndexes[CM.NewValue]).Address.ToStringRelative();
            ws.Cell(lastRow, columnIndexes[CM.Change]).FormulaA1 = $"={sumNewValueCell}-{sumOldValueCell}";

            var ratioCell = ws.Cell(lastRow, columnIndexes[CM.ChangeRatio]);
            ratioCell.SetFormulaA1($"=IF({sumOldValueCell}=0, 0, {sumNewValueCell}/{sumOldValueCell}-1)")
                .Style.NumberFormat.Format = "0%";

            var oldYearAddress = ws.Range(2, columnIndexes[CM.OldYearValue], lastRow - 1, columnIndexes[CM.OldYearValue]).RangeAddress.ToStringRelative();
            var newYearAddress = ws.Range(2, columnIndexes[CM.NewYearValue], lastRow - 1, columnIndexes[CM.NewYearValue]).RangeAddress.ToStringRelative();

            ws.Cell(lastRow, columnIndexes[CM.OldYearValue]).FormulaA1 = $"=SUBTOTAL(109, {oldYearAddress})";
            ws.Cell(lastRow, columnIndexes[CM.NewYearValue]).FormulaA1 = $"=SUBTOTAL(109, {newYearAddress})";

            var sumOldYearValueCell = ws.Cell(lastRow, columnIndexes[CM.OldYearValue]).Address.ToStringRelative();
            var sumNewYearValueCell = ws.Cell(lastRow, columnIndexes[CM.NewYearValue]).Address.ToStringRelative();
            ws.Cell(lastRow, columnIndexes[CM.YearChange]).FormulaA1 = $"={sumNewYearValueCell}-{sumOldYearValueCell}";

            var yearRatioCell = ws.Cell(lastRow, columnIndexes[CM.YearChangeRatio]);
            yearRatioCell.SetFormulaA1($"=IF({sumOldYearValueCell}=0, 0, {sumNewYearValueCell}/{sumOldYearValueCell}-1)")
                .Style.NumberFormat.Format = "0%";

            // Условное форматирование для итогов
            var cfSumGreen = ratioCell.AddConditionalFormat();
            cfSumGreen.WhenLessThan(0).Fill.BackgroundColor = _lightGreen;

            var cfSumRed = ratioCell.AddConditionalFormat();
            cfSumRed.WhenGreaterThan(0).Fill.BackgroundColor = _lightRed;

            var cfSumYearGreen = yearRatioCell.AddConditionalFormat();
            cfSumYearGreen.WhenLessThan(0).Fill.BackgroundColor = _lightGreen;

            var cfSumYearRed = yearRatioCell.AddConditionalFormat();
            cfSumYearRed.WhenGreaterThan(0).Fill.BackgroundColor = _lightRed;
        }

        public static string ExportAssignmentCheckResult(IEnumerable<Part> factParts, Dictionary<string, string> assignmentParts, string path, IProgress<string> progress)
        {
            var wb = new XLWorkbook();
            var wsAll = wb.AddWorksheet($"Все");
            wsAll.Style.Alignment.WrapText = true;

            var cm = new CM.Builder()
                .Add(CM.Date)
                .Add(CM.Operator)
                .Add(CM.Part)
                .Add(CM.Setup)
                .Add(CM.Machine)
                .Add(CM.MachineAssigned)
                .Add(CM.IsEqual)
                .Build();

            ConfigureWorksheetHeader(wsAll, cm, HeaderRotateOption.Vertical, 65, 8);

            wsAll.Range(2, 1, 2, cm.Count).Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);

            int row = 3;
            var ci = cm.GetIndexes();

            foreach (var part in factParts)
            {
                progress.Report($"Все: Проверка {part.PartName}");
                var isAssigned = assignmentParts.ContainsKey(part.PartName.ToLowerInvariant().Trim());
                wsAll.Cell(row, ci[CM.Date]).Value = part.ShiftDate;
                wsAll.Cell(row, ci[CM.Operator]).Value = part.Operator;
                wsAll.Cell(row, ci[CM.Part]).Value = part.PartName;
                wsAll.Cell(row, ci[CM.Setup]).Value = part.Setup;
                wsAll.Cell(row, ci[CM.Machine]).Value = part.Machine;
                var assignedMachine = isAssigned
                    ? assignmentParts
                        .Where(ap => ap.Key.ToLowerInvariant().Trim() == part.PartName.ToLowerInvariant().Trim())
                        .First()
                        .Value
                    : "-";
                wsAll.Cell(row, ci[CM.MachineAssigned]).Value = assignedMachine;
                wsAll.Cell(row, ci[CM.IsEqual]).Value = !isAssigned ? "-" : part.Machine.Contains(assignedMachine);
                row++;
            }

            progress.Report("Все: Настройка листа");
            wsAll.Range(2, 1, row - 1, ci.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            wsAll.Range(2, 1, row - 1, cm.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            wsAll.RangeUsed().SetAutoFilter(true);
            SetTitle(wsAll, ci.Count, "Проверка на изготовление в соответствии со СЗН");
            wsAll.Columns().AdjustToContents();

            var wsUnique = wb.AddWorksheet("Уникальные");
            wsUnique.Style.Alignment.WrapText = true;

            cm = new CM.Builder()
                .Add(CM.Date)
                .Add(CM.Part)
                .Add(CM.Order)
                .Add(CM.Machine)
                .Add(CM.MachineAssigned)
                .Add(CM.IsEqual)
                .Build();
            ConfigureWorksheetHeader(wsUnique, cm, HeaderRotateOption.Vertical, 65, 8);
            wsUnique.Range(2, 1, 2, cm.Count).Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);

            row = 3;
            ci = cm.GetIndexes();

            foreach (var part in factParts.GroupBy(p => (p.PartName, p.Order, p.Machine)).Select(g => g.First()))
            {
                progress.Report($"Уникальные: Проверка {part.PartName}");
                var isAssigned = assignmentParts.ContainsKey(part.PartName.ToLowerInvariant().Trim());
                wsUnique.Cell(row, ci[CM.Date]).Value = part.ShiftDate;
                wsUnique.Cell(row, ci[CM.Part]).Value = part.PartName;
                wsUnique.Cell(row, ci[CM.Order]).Value = part.Order;
                wsUnique.Cell(row, ci[CM.Machine]).Value = part.Machine;
                var assignedMachine = isAssigned
                    ? assignmentParts
                        .Where(ap => ap.Key.ToLowerInvariant().Trim() == part.PartName.ToLowerInvariant().Trim())
                        .First()
                        .Value
                    : "-";
                wsUnique.Cell(row, ci[CM.MachineAssigned]).Value = assignedMachine;
                wsUnique.Cell(row, ci[CM.IsEqual]).Value = !isAssigned ? "-" : part.Machine.Contains(assignedMachine);
                row++;
            }
            progress.Report("Уникальные: Настройка листа");
            wsUnique.Range(2, 1, row - 1, ci.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            wsUnique.Range(2, 1, row - 1, cm.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            wsUnique.RangeUsed().SetAutoFilter(true);
            SetTitle(wsUnique, ci.Count, "Проверка на изготовление в соответствии со СЗН");
            wsUnique.Columns().AdjustToContents();

            var wsFromList = wb.AddWorksheet("По списку");
            wsFromList.Style.Alignment.WrapText = true;

            cm = new CM.Builder()
                .Add(CM.Part)
                .Add(CM.MachineAssigned)
                .Add(CM.Order, "Заказы")
                .Add(CM.Machine, "Станки")
                .Add(CM.IsEqual, "Делали")
                .Add(CM.CountPerMachine, "Количество запусков")
                .Build();
            ConfigureWorksheetHeader(wsFromList, cm, HeaderRotateOption.Vertical, 65, 8);
            wsFromList.Range(2, 1, 2, cm.Count).Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);

            row = 3;
            ci = cm.GetIndexes();

            foreach (var partName in assignmentParts.Keys.Skip(1))
            {
                progress.Report($"По списку: Проверка {partName}");
                var isProcessed = factParts.Select(p => p.PartName.ToLowerInvariant().Trim()).Contains(partName);
                wsFromList.Cell(row, ci[CM.Part]).Value = partName;
                var orders = factParts.Where(p => p.PartName.ToLowerInvariant().Trim() == partName).Select(pn => pn.Order).Distinct();
                wsFromList.Cell(row, ci[CM.Order]).Value = string.Join(" ", orders);
                var machines = factParts.Where(p => p.PartName.ToLowerInvariant().Trim() == partName).Select(pn => pn.Machine).Distinct();
                wsFromList.Cell(row, ci[CM.Machine]).Value = string.Join(" ", machines);
                wsFromList.Cell(row, ci[CM.MachineAssigned]).Value = assignmentParts[partName];
                var status = "";
                if (isProcessed && machines.Count() == 1 && string.Join(" ", machines).Contains(assignmentParts[partName]))
                {
                    status = "Делали как надо";
                }
                else if (isProcessed && machines.Count() > 1 && string.Join(" ", machines).Contains(assignmentParts[partName]))
                {
                    status = "Делали и где надо и где не надо";
                }
                else if (isProcessed)
                {
                    status = "Делали где попало, но не где надо";
                }
                else
                {
                    status = "Не делали";
                }
                wsFromList.Cell(row, ci[CM.IsEqual]).Value = status;
                var count = factParts.Where(p => p.PartName.ToLowerInvariant().Trim() == partName).Select(p => (p.Order, p.Machine)).Distinct().Count();
                wsFromList.Cell(row, ci[CM.CountPerMachine]).Value = count;
                row++;
            }
            progress.Report("Уникальные: Настройка листа");
            wsFromList.Range(2, 1, row - 1, ci.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            wsFromList.Range(2, 1, row - 1, cm.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            wsFromList.RangeUsed().SetAutoFilter(true);
            SetTitle(wsFromList, ci.Count, "Проверка на изготовление в соответствии со СЗН");
            wsFromList.Columns().AdjustToContents();



            progress.Report("Сохранение файла");
            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
            }

            return path;
        }

        /// <summary>
        /// Экспортирует историю последних заказов в Excel-файл.
        ///
        /// Метод создает Excel-файл с историей заказов, включая информацию о станках, операторах,
        /// времени работы, установках, комментариях и других данных. Данные фильтруются по количеству последних заказов.
        /// Форматирует таблицу, добавляет стили и сохраняет файл по указанному пути.
        ///
        /// </summary>
        /// <param name="parts">
        /// Коллекция деталей (Part), содержащая данные для экспорта.
        /// </param>
        /// <param name="path">
        /// Путь, по которому будет сохранен созданный Excel-файл.
        /// </param>
        /// <param name="ordersCount">
        /// Количество последних заказов, которые необходимо включить в отчет.
        /// </param>
        /// <returns>
        /// Полный путь к сохраненному файлу.
        /// </returns>
        /// <remarks>
        /// Формат выходного файла:
        /// - Включает следующие столбцы: "Станок", "Дата", "Оператор", "М/Л", "Выполнено", "Установка",
        ///   "Машинное время", "Комментарий оператора", "Типовые проблемы", "Комментарий мастера".
        /// - Данные упорядочены сначала по станку, затем по времени установки.
        /// - Добавляются стили ячеек, границы, автофильтры, а также настройки страницы (ориентация, размеры полей и т. д.).
        ///
        /// Если файл успешно сохранен, пользователю предлагается открыть его.
        /// </remarks>
        /// <example>
        /// Пример вызова метода:
        /// <code>
        /// var parts = GetParts();
        /// string filePath = "C:\\Reports\\History.xlsx";
        /// ExportHistory(parts, filePath, 10);
        /// </code>
        /// </example>
        public static string ExportPartsInfo(ICollection<Part> parts, string path, DateTime fromDate, DateTime toDate)
        {
            var wb = new XLWorkbook();
            var wsTotal = wb.AddWorksheet("Общий");
            ConfigureWorksheetStyles(wsTotal);

            var machines = parts.Where(p => !p.ExcludeFromReports).Select(p => p.Machine).Distinct().ToArray();

            foreach (var machine in machines)
            {
                wsTotal.CopyTo(machine);
            }

            var columns = new Dictionary<string, (int index, string header)>
            {
                {"machine", (1, "Станок") },
                {"part", (2, "Деталь") },
                {"ordersCnt", (3, $"Количество М/Л{Environment.NewLine}(по станку)") },
                {"partsCnt", (4, $"Количество деталей{Environment.NewLine}(по станку)") },
                {"planSum", (5, $"Сумма нормативов{Environment.NewLine}(по станку)") },
                {"factSum", (6, $"Время фактическое{Environment.NewLine}(по станку)") },
                {"ordersCntTotal", (7, $"Количество М/Л{Environment.NewLine}(общее)") },
                {"partsCntTotal", (8, $"Количество деталей{Environment.NewLine}(общее)") },
                {"planSumTotal", (9, $"Сумма нормативов{Environment.NewLine}(общее)") },
                {"factSumTotal", (10, $"Время фактическое{Environment.NewLine}(общее)") },
            };

            ConfigureWorksheetHeader(wsTotal, columns);

            var tempParts = parts.Where(p => !p.ExcludeFromReports).Select(part => new Part(part) { PartName = part.PartName.ToUpper().Trim('"') }).ToList();

            var machinesGroup = tempParts.GroupBy(p => p.Machine);

            FillTotalMachinesWorksheetData(wsTotal, machinesGroup, columns, tempParts);

            foreach (var ws in wb.Worksheets.Skip(1))
            {
                ConfigureWorksheetHeader(ws, columns);
                FillMachineWorksheetData(ws, tempParts.Where(p => p.Machine == ws.Name), columns);
            }

            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo() { UseShellExecute = true, FileName = path });
            return path;
        }

        public static string ExportShiftsInfo(ICollection<Part> parts, string path, DateTime fromDate, DateTime toDate)
        {
            var wb = new XLWorkbook();
            var wsTotal = wb.AddWorksheet("Общий");
            ConfigureWorksheetStyles(wsTotal);

            var machines = parts.Where(p => !p.ExcludeFromReports).Select(p => p.Machine).Distinct().ToArray();

            //foreach (var machine in machines)
            //{
            //    wsTotal.CopyTo(machine);
            //}

            var columns = new Dictionary<string, (int index, string header)>
            {
                {"date", (1, "Дата") },
                {"type", (2, "Смена") },
                {"machine", (3, "Станок") },
                {"master", (4, "Мастер") },
                {"ordersCnt", (5, $"Количество М/Л") },
                {"partsCnt", (6, $"Количество деталей") },
                {"planSum", (7, $"Сумма нормативов") },
                {"factSum", (8, $"Время фактическое") },
                {"specDowntimes", (9, $"Отмеченные простои") },
                {"unspecDowntimes", (10, $"Неотмеченные простои") },
                {"comment", (11, $"Комментарий") },
                {"isChecked", (12, $"Проверено СГТ") },
            };

            ConfigureWorksheetHeader(wsTotal, columns);

            switch (Database.GetShiftsByPeriod(machines, fromDate, toDate, new Shift(ShiftType.All), out var shifts))
            {
                case libeLog.Models.DbResult.AuthError:
                    MessageBox.Show("AuthError");
                    return "";
                case libeLog.Models.DbResult.Error:
                    MessageBox.Show("Error");
                    return "";
                case libeLog.Models.DbResult.NoConnection:
                    MessageBox.Show("NoConnection");
                    return "";
            }
            shifts = shifts.OrderBy(s => s.Machine).ToList();
            int row = 3;
            for (DateTime dt = fromDate; dt <= toDate; dt += TimeSpan.FromDays(1))
            {
                foreach (var machine in machines)
                {
                    var dayShiftInfo = shifts.Find(s => s.ShiftDate == dt && s.Machine == machine && s.Shift == "День");
                    var dayParts = parts.Where(p => p.ShiftDate == dt && p.Machine == machine && p.Shift == "День");
                    var nightShiftInfo = shifts.Find(s => s.ShiftDate == dt && s.Machine == machine && s.Shift == "Ночь");
                    var nightParts = parts.Where(p => p.ShiftDate == dt && p.Machine == machine && p.Shift == "Ночь");
                    wsTotal.Cell(row, columns["date"].index).Value = dt;
                    wsTotal.Range(row, columns["date"].index, row + 1, columns["date"].index).Merge();

                    wsTotal.Cell(row, columns["type"].index).Value = "День";
                    wsTotal.Cell(row + 1, columns["type"].index).Value = "Ночь";

                    wsTotal.Cell(row, columns["machine"].index).Value = machine;
                    wsTotal.Range(row, columns["machine"].index, row + 1, columns["machine"].index).Merge();

                    wsTotal.Cell(row, columns["master"].index).Value = dayShiftInfo != null ? dayShiftInfo.Master : "Н/Д";
                    wsTotal.Range(row, columns["master"].index, row + 1, columns["master"].index).Merge();

                    wsTotal.Cell(row, columns["ordersCnt"].index).Value = dayParts
                        .Select(p => p.Order)
                        .Distinct()
                        .Count();
                    wsTotal.Cell(row + 1, columns["ordersCnt"].index).Value = nightParts
                        .Select(p => p.Order)
                        .Distinct()
                        .Count();

                    row += 2;
                }
            }

            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo() { UseShellExecute = true, FileName = path });
            return path;
        }

        public static string ExportVerevkinReport(IEnumerable<VerevkinPart> parts, string path, IProgress<string> progress)
        {
            var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();

            var columns = new Dictionary<string, int>
            {
                {"no", 1 },
                {"part", 2 },
                {"fact1", 4 },
                {"fact2", 5 },
                {"fact3", 6 },
                {"fact4", 7 },
                {"fact5", 8 },
                {"factSum", 9 },
                {"factTurn1prev", 10 },
                {"factTurn2prev", 11 },
                {"factTurn3prev", 12 },
                {"factSumPrev", 13 },
                {"factMill1prev", 14 },
                {"factMill2prev", 15 },
                {"factMill3prev", 16 },
                {"factMill4prev", 17 },
                {"factSumMillPrev", 18 },
                {"factSum2", 19 },
                {"factSumPrev2", 20 },
                {"effectTime", 21 },
                {"effectPercent", 22 },
                {"order", 23 },
                {"count", 24 },
            };
            progress.Report("Чтение содержимого");

            int rowIndex = 0;

            foreach (var row in ws.Rows().Skip(2))
            {
                rowIndex++;
                if (!row.Cell(2).IsEmpty() || !row.Cell(2).Value.IsBlank) continue;
                break;
            }
            rowIndex += 2;
            progress.Report("Добавление деталей");
            foreach (var part in parts)
            {
                ws.Row(rowIndex).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(rowIndex).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIndex, columns["no"]).SetValue(rowIndex - 2);
                ws.Cell(rowIndex, columns["part"]).SetValue(part.PartName).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Range(rowIndex, columns["part"], rowIndex, columns["part"] + 1).Merge();
                if (part.MachineTime1 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact1"]).SetValue(part.MachineTime1);
                if (part.MachineTime2 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact2"]).SetValue(part.MachineTime2);
                if (part.MachineTime3 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact3"]).SetValue(part.MachineTime3);
                if (part.MachineTime4 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact4"]).SetValue(part.MachineTime4);
                if (part.MachineTime5 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact5"]).SetValue(part.MachineTime5);
                ws.Cell(rowIndex, columns["factSum"]).SetFormulaA1($"SUM(D{rowIndex}:H{rowIndex})");
                ws.Cell(rowIndex, columns["factSumPrev"]).SetFormulaA1($"SUM(J{rowIndex}:L{rowIndex})");
                ws.Cell(rowIndex, columns["factSumMillPrev"]).SetFormulaA1($"SUM(N{rowIndex}:Q{rowIndex})");
                ws.Cell(rowIndex, columns["factSum2"]).SetFormulaA1($"I{rowIndex}");
                ws.Cell(rowIndex, columns["factSumPrev2"]).SetFormulaA1($"M{rowIndex}+R{rowIndex}");
                ws.Cell(rowIndex, columns["effectTime"]).SetFormulaA1(
                    $"IF(ISNUMBER(T{rowIndex})*AND(T{rowIndex}>0), IF(T{rowIndex} - S{rowIndex} < 0, \"-\" " +
                    $"& TEXT(ABS(T{rowIndex} - S{rowIndex}), \"Ч:ММ:СС\"), TEXT(T{rowIndex} - S{rowIndex}, \"Ч:ММ:СС\")), T{rowIndex})");

                ws.Cell(rowIndex, columns["effectPercent"]).SetFormulaA1($"IFERROR(T{rowIndex}/S{rowIndex},T{rowIndex})")
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
                ws.Range(rowIndex, columns["fact1"], rowIndex, columns["effectTime"]).Style.NumberFormat.SetFormat("h:mm:ss");
                ws.Cell(rowIndex, columns["order"]).SetValue(part.Order);
                ws.Cell(rowIndex, columns["count"]).SetValue(part.FinishedCount);
                rowIndex++;
            }

            progress.Report("Сохранение");
            wb.SaveAs(path);

            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo() { UseShellExecute = true, FileName = path });
            return path;
        }

        public static string ExportLongSetups(ICollection<Part> parts, string path)
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Длительные наладки");
            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var limits = parts
                .Select(p => p.Machine)
                .Distinct()
                .ToDictionary(
                machine => machine,
                machine =>
                {
                    var (_, SetupLimit, _) = machine.GetMachineSetupLimit();
                    var (_, SetupCoefficient, _) = machine.GetMachineSetupCoefficient();
                    return (SetupCoefficient, SetupLimit);
                });

            var totalSetups = parts
                .Where(p => p.SetupTimePlanForCalc > 0 || p.PartialSetupTime > 0)
                .GroupBy(p => (p.PartName, p.Order, p.Setup))
                .SelectMany(g => g.Distinct())
                .Count();

            var cm = new CM();
            cm.Add(CM.Machine);
            cm.Add(CM.Date);
            cm.Add(CM.Shift);
            cm.Add(CM.Operator);
            cm.Add(CM.Part);
            cm.Add(CM.Order);
            cm.Add(CM.Finished);
            cm.Add(CM.Setup);
            cm.Add(CM.StartSetupTime);
            cm.Add(CM.StartMachiningTime);
            cm.Add(CM.EndMachiningTime);
            cm.Add(CM.SetupLimit);
            cm.Add(CM.SetupTimePlan);
            cm.Add(CM.SetupTimeFact);
            cm.Add(CM.PartialSetupTime);
            cm.Add(CM.SingleProductionTimePlan);
            cm.Add(CM.MachiningTime);
            cm.Add(CM.SingleProductionTime);
            cm.Add(CM.PartReplacementTime);
            cm.Add(CM.OperatorComment);
            cm.Add(CM.SetupDowntimes);
            cm.Add(CM.MachiningDowntimes);
            cm.Add(CM.MaintenanceTime);
            cm.Add(CM.ToolSearchingTime);
            cm.Add(CM.ToolChangingTime);
            cm.Add(CM.MentoringTime);
            cm.Add(CM.ContactingDepartmentsTime);
            cm.Add(CM.FixtureMakingTime);
            cm.Add(CM.HardwareFailureTime);
            cm.Add(CM.SpecifiedDowntimesRatio);
            cm.Add(CM.SpecifiedDowntimesComment);
            cm.Add(CM.SetupRatioTitle);
            cm.Add(CM.MasterSetupComment);
            cm.Add(CM.MasterComment);
            cm.Add(CM.FixedSetupTimePlan);
            cm.Add(CM.FixedProductionTimePlan);
            cm.Add(CM.EngineerComment);

            ConfigureWorksheetHeader(ws, cm);

            var ci = cm.GetIndexes();
            var row = 3;
            var cnt = 0;
            foreach (var part in parts)
            {
                var (limitValue, limitInfo) = part.SetupLimit(limits[part.Machine].SetupCoefficient, limits[part.Machine].SetupLimit);
                if (limitValue >= part.SetupTimeFact + part.PartialSetupTime) continue;

                ws.Cell(row, ci[CM.Machine]).SetValue(part.Machine);
                ws.Cell(row, ci[CM.Date])
                    .SetValue(part.ShiftDate)
                    .Style.DateFormat.Format = "dd.MM.yy";
                ws.Cell(row, ci[CM.Shift]).SetValue(part.Shift);
                ws.Cell(row, ci[CM.Operator]).SetValue(part.Operator);
                ws.Cell(row, ci[CM.Part]).SetValue(part.PartName);
                ws.Cell(row, ci[CM.Order]).SetValue(part.Order);
                ws.Cell(row, ci[CM.Finished]).SetValue(part.FinishedCount);
                ws.Cell(row, ci[CM.Setup]).SetValue(part.Setup);
                ws.Cell(row, ci[CM.StartSetupTime])
                    .SetValue(part.StartSetupTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.StartMachiningTime])
                    .SetValue(part.StartMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.EndMachiningTime])
                    .SetValue(part.EndMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.SetupTimePlan]).SetValue(part.SetupTimePlan);
                ws.Cell(row, ci[CM.SetupTimeFact]).SetValue(part.SetupTimeFact);
                if (part.SetupTimeFact > limitValue) ws.Cell(row, ci[CM.SetupTimeFact]).Style.Fill.BackgroundColor = XLColor.LightYellow;
                ws.Cell(row, ci[CM.SetupLimit]).SetValue(limitValue).CreateComment().SetAuthor("Отчёт").AddText(limitInfo).AddNewLine();
                ws.Cell(row, ci[CM.SingleProductionTimePlan]).SetValue(part.SingleProductionTimePlan);
                ws.Cell(row, ci[CM.MachiningTime]).SetValue(part.MachiningTime);
                if (part.SingleProductionTime is double spt && spt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SingleProductionTime])
                        .SetValue(spt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                if (part.PartReplacementTime is double prt && prt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.PartReplacementTime])
                        .SetValue(prt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                ws.Cell(row, ci[CM.OperatorComment])
                    .SetValue(part.OperatorComment)
                    .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, ci[CM.SetupDowntimes]).SetValue(part.SetupDowntimes);
                ws.Cell(row, ci[CM.MachiningDowntimes]).SetValue(part.MachiningDowntimes);
                ws.Cell(row, ci[CM.PartialSetupTime]).SetValue(part.PartialSetupTime);
                if (part.PartialSetupTime > limitValue) ws.Cell(row, ci[CM.PartialSetupTime]).Style.Fill.BackgroundColor = XLColor.LightYellow;
                ws.Cell(row, ci[CM.MaintenanceTime]).SetValue(part.MaintenanceTime);
                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(part.ToolSearchingTime);
                ws.Cell(row, ci[CM.ToolChangingTime]).SetValue(part.ToolChangingTime);
                ws.Cell(row, ci[CM.MentoringTime]).SetValue(part.MentoringTime);
                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).SetValue(part.ContactingDepartmentsTime);
                ws.Cell(row, ci[CM.FixtureMakingTime]).SetValue(part.FixtureMakingTime);
                ws.Cell(row, ci[CM.HardwareFailureTime]).SetValue(part.HardwareFailureTime);
                if (part.SpecifiedDowntimesRatio is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SpecifiedDowntimesRatio])
                        .SetValue(part.SpecifiedDowntimesRatio)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
                ws.Cell(row, ci[CM.SpecifiedDowntimesComment]).SetValue(part.SpecifiedDowntimesComment);
                ws.Cell(row, ci[CM.SetupRatioTitle]).SetValue(part.SetupRatioTitle);
                ws.Cell(row, ci[CM.MasterSetupComment]).SetValue(part.MasterSetupComment);
                ws.Cell(row, ci[CM.MasterComment]).SetValue(part.MasterComment);
                ws.Cell(row, ci[CM.FixedSetupTimePlan]).SetValue(part.FixedSetupTimePlan);
                ws.Cell(row, ci[CM.FixedProductionTimePlan]).SetValue(part.FixedProductionTimePlan);
                ws.Cell(row, ci[CM.EngineerComment]).SetValue(part.EngineerComment);
                cnt++;
                row++;
            }

            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            ws.Columns().AdjustToContents();

            ws.Range(3, ci[CM.Machine], row, ci[CM.Machine])
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Column(ci[CM.Operator]).Width = 15;
            ws.Column(ci[CM.Part]).Width = 25;
            ws.Range(3, ci[CM.OperatorComment], row, ci[CM.OperatorComment])
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Column(ci[CM.OperatorComment]).Width = 35;
            ws.Column(ci[CM.MasterSetupComment]).Width = 20;
            ws.Column(ci[CM.MasterComment]).Width = 20;


            var hiddenColumns = new List<string>
            {
                CM.SingleProductionTimePlan,
                CM.MachiningTime,
                CM.SingleProductionTime,
                CM.PartReplacementTime,
                CM.MachiningDowntimes,
                CM.MaintenanceTime,
                CM.ToolSearchingTime,
                CM.ToolChangingTime,
                CM.MentoringTime,
                CM.ContactingDepartmentsTime,
                CM.FixtureMakingTime,
                CM.HardwareFailureTime,
                CM.SpecifiedDowntimesRatio,
                CM.SpecifiedDowntimesComment,
                CM.FixedProductionTimePlan
            };
            foreach (var col in hiddenColumns)
            {
                ws.Column(ci[col]).Hide();
            }
            SetTitle(ws, cm.Count, $"Отчёт по длительным наладкам: {cnt} из {totalSetups} ({((double)cnt / (double)totalSetups) * 100:N2}%) за период от {parts.Min(p => p.ShiftDate).ToString(Constants.ShortDateFormat)} до {parts.Max(p => p.ShiftDate).ToString(Constants.ShortDateFormat)}");
            ws.SheetView.FreezeRows(2);

            wb.SaveAs(path);
            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo() { UseShellExecute = true, FileName = path });
            return path;
        }

        public static string ExportDataset(ICollection<Part> parts, string path)
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Экспорт");
            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var cm = new CM.Builder()
                .Add(CM.Machine)
                .Add(CM.Date)
                .Add(CM.Shift)
                .Add(CM.Operator)
                .Add(CM.Part)
                .Add(CM.Order)
                .Add(CM.TotalByOrder)
                .Add(CM.Finished)
                .Add(CM.Setup)
                .Add(CM.StartSetupTime)
                .Add(CM.StartMachiningTime)
                .Add(CM.EndMachiningTime)
                .Add(CM.SetupTimePlan)
                .Add(CM.SetupTimeFact)
                .Add(CM.SingleProductionTimePlan)
                .Add(CM.MachiningTime)
                .Add(CM.SingleProductionTime)
                .Add(CM.PartReplacementTime)
                .Add(CM.ProductionTimeFact)
                .Add(CM.PlanForBatch)
                .Add(CM.OperatorComment)
                .Add(CM.SetupDowntimes)
                .Add(CM.MachiningDowntimes)
                .Add(CM.PartialSetupTime)
                .Add(CM.CreateNcProgramTime)
                .Add(CM.MaintenanceTime)
                .Add(CM.ToolSearchingTime)
                .Add(CM.ToolChangingTime)
                .Add(CM.MentoringTime)
                .Add(CM.ContactingDepartmentsTime)
                .Add(CM.FixtureMakingTime)
                .Add(CM.HardwareFailureTime)
                .Add(CM.SpecifiedDowntimesRatio)
                .Add(CM.SpecifiedDowntimesComment)
                .Add(CM.SetupRatioTitle)
                .Add(CM.MasterSetupComment)
                .Add(CM.ProductionRatioTitle)
                .Add(CM.MasterProductionComment)
                .Add(CM.MasterComment)
                .Add(CM.FixedSetupTimePlan)
                .Add(CM.FixedProductionTimePlan)
                .Add(CM.EngineerComment)
                .Add(CM.SerialPerList)
                .Build();

            ConfigureWorksheetHeader(ws, cm);
            var serialParts = !string.IsNullOrEmpty(AppSettings.Instance.ConnectionString) ? libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString).GetAwaiter().GetResult() : new();

            var ci = cm.GetIndexes();

            var row = 3;
            foreach (var part in parts)
            {
                var isSerial = serialParts.Select(sp => sp.PartName.NormalizedPartNameWithoutComments()).Contains(part.PartName.NormalizedPartNameWithoutComments());
                ws.Cell(row, ci[CM.Machine]).SetValue(part.Machine);

                ws.Cell(row, ci[CM.Date])
                    .SetValue(part.ShiftDate)
                    .Style.DateFormat.Format = "dd.MM.yy";

                ws.Cell(row, ci[CM.Shift]).SetValue(part.Shift);

                ws.Cell(row, ci[CM.Operator]).SetValue(part.Operator);

                ws.Cell(row, ci[CM.Part]).SetValue(part.PartName).Style.Font.SetUnderline(isSerial ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None);

                ws.Cell(row, ci[CM.Order]).SetValue(part.Order);

                ws.Cell(row, ci[CM.TotalByOrder]).SetValue(part.TotalCount);

                ws.Cell(row, ci[CM.Finished]).SetValue(part.FinishedCount);

                ws.Cell(row, ci[CM.Setup]).SetValue(part.Setup);

                ws.Cell(row, ci[CM.StartSetupTime])
                    .SetValue(part.StartSetupTime)
                    .Style.DateFormat.Format = "HH:mm";

                ws.Cell(row, ci[CM.StartMachiningTime])
                    .SetValue(part.StartMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";

                ws.Cell(row, ci[CM.EndMachiningTime])
                    .SetValue(part.EndMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";

                ws.Cell(row, ci[CM.SetupTimePlan]).SetValue(part.SetupTimePlan);

                ws.Cell(row, ci[CM.SetupTimeFact]).SetValue(part.SetupTimeFact);

                ws.Cell(row, ci[CM.SingleProductionTimePlan]).SetValue(part.SingleProductionTimePlan);

                ws.Cell(row, ci[CM.MachiningTime]).SetValue(part.MachiningTime);

                if (part.SingleProductionTime is double spt && spt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SingleProductionTime])
                        .SetValue(spt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

                if (part.PartReplacementTime is double prt && prt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.PartReplacementTime])
                        .SetValue(prt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

                ws.Cell(row, ci[CM.ProductionTimeFact]).SetValue(part.ProductionTimeFact);

                ws.Cell(row, ci[CM.PlanForBatch]).SetValue(part.PlanForBatch);

                ws.Cell(row, ci[CM.OperatorComment])
                    .SetValue(part.OperatorComment)
                    .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                ws.Cell(row, ci[CM.SetupDowntimes]).SetValue(part.SetupDowntimes);

                ws.Cell(row, ci[CM.MachiningDowntimes]).SetValue(part.MachiningDowntimes);

                ws.Cell(row, ci[CM.PartialSetupTime]).SetValue(part.PartialSetupTime);

                ws.Cell(row, ci[CM.CreateNcProgramTime]).SetValue(part.CreateNcProgramTime);

                ws.Cell(row, ci[CM.MaintenanceTime]).SetValue(part.MaintenanceTime);

                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(part.ToolSearchingTime);

                ws.Cell(row, ci[CM.ToolChangingTime]).SetValue(part.ToolChangingTime);

                ws.Cell(row, ci[CM.MentoringTime]).SetValue(part.MentoringTime);

                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).SetValue(part.ContactingDepartmentsTime);

                ws.Cell(row, ci[CM.FixtureMakingTime]).SetValue(part.FixtureMakingTime);

                ws.Cell(row, ci[CM.HardwareFailureTime]).SetValue(part.HardwareFailureTime);

                if (part.SpecifiedDowntimesRatio is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SpecifiedDowntimesRatio])
                        .SetValue(part.SpecifiedDowntimesRatio)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                ws.Cell(row, ci[CM.SpecifiedDowntimesComment]).SetValue(part.SpecifiedDowntimesComment);

                ws.Cell(row, ci[CM.SetupRatioTitle]).SetValue(part.SetupRatioTitle);

                ws.Cell(row, ci[CM.MasterSetupComment]).SetValue(part.MasterSetupComment);

                ws.Cell(row, ci[CM.ProductionRatioTitle]).SetValue(part.ProductionRatioTitle);

                ws.Cell(row, ci[CM.MasterProductionComment]).SetValue(part.MasterMachiningComment);

                ws.Cell(row, ci[CM.MasterComment]).SetValue(part.MasterComment);

                ws.Cell(row, ci[CM.FixedSetupTimePlan]).SetValue(part.FixedSetupTimePlan);

                ws.Cell(row, ci[CM.FixedProductionTimePlan]).SetValue(part.FixedProductionTimePlan);

                ws.Cell(row, ci[CM.EngineerComment]).SetValue(part.EngineerComment);

                ws.Cell(row, ci[CM.SerialPerList]).SetValue(isSerial);

                row++;
            }


            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            ws.Columns().AdjustToContents();

            ws.Range(3, ci[CM.Machine], row, ci[CM.Machine])
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Column(ci[CM.Operator]).Width = 15;

            ws.Column(ci[CM.Part]).Width = 25;

            ws.Range(3, ci[CM.OperatorComment], row, ci[CM.OperatorComment])
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Column(ci[CM.OperatorComment]).Width = 35;

            ws.Column(ci[CM.MasterSetupComment]).Width = 20;
            ws.Column(ci[CM.MasterProductionComment]).Width = 20;
            ws.Column(ci[CM.MasterComment]).Width = 20;
            ws.Column(ci[CM.SerialPerList]).Width = 8;

            ws.Columns(ci[CM.PartialSetupTime], ci[CM.HardwareFailureTime]).Group(false);
            
            ws.Row(1).Delete();
            ws.SheetView.FreezeRows(1);

            wb.SaveAs(path);
            if (MessageBox.Show("Открыть сохраненный файл?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo() { UseShellExecute = true, FileName = path });
            return path;
        }

        //public static string GetOperatorQualification(this string operatorName)
        //{
        //    if (!File.Exists(AppSettings.Instance.QualificationSourcePath)) return "Н/Д";
        //    try
        //    {
        //        using (var fs = new FileStream(AppSettings.Instance.QualificationSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        //        {
        //            var wb = new XLWorkbook(fs);
        //            foreach (var xlRow in wb.Worksheet(1).Rows().Skip(2))
        //            {
        //                if (xlRow.Cell(2).Value.IsText && xlRow.Cell(2).Value.GetText() == operatorName)
        //                {
        //                    if (xlRow.Cell(4).Value.IsText) return xlRow.Cell(4).Value.GetText();
        //                    else if (xlRow.Cell(4).Value.IsNumber) return xlRow.Cell(4).Value.GetNumber().ToString();
        //                }
        //            }
        //            return "Н/Д";
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Util.WriteLog(ex);
        //        return "Н/Д";
        //    }
        //}

        public static string GetOperatorQualification(this string operatorFullName, IEnumerable<OperatorInfo> operators)
        {
            foreach (var @operator in operators)
            {
                if (@operator.FullName == operatorFullName)
                {
                    return @operator.Qualification.ToString();
                }
            }
            return "Н/Д";
        }

        private static void ConfigureWorksheetStyles(IXLWorksheet ws)
        {
            ws.Style.Font.FontSize = 12;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        /// <summary>
        /// Настраивает заголовки на рабочем листе Excel на основе словаря колонок.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="columns">Словарь колонок, где ключ — идентификатор, значение — пара (индекс, заголовок).</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeader(IXLWorksheet ws, Dictionary<string, (int index, string header)> columns, HeaderRotateOption headerRotateOption = HeaderRotateOption.Vertical, int height = 90, int fontSize = 10)
        {
            ConfigureWorksheetHeaderInternal(ws, columns.Select(c => (c.Value.index, c.Value.header)).ToList(), headerRotateOption, height, fontSize);
        }

        /// <summary>
        /// Настраивает заголовки на рабочем листе Excel EPPlus на основе словаря колонок.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="columns">Словарь колонок, где ключ — идентификатор, значение — пара (индекс, заголовок).</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeader(ExcelWorksheet ws, Dictionary<string, (int index, string header)> columns, HeaderRotateOption headerRotateOption = HeaderRotateOption.Vertical, int height = 90, int fontSize = 10)
        {
            ConfigureWorksheetHeaderInternal(ws, columns.Select(c => (c.Value.index, c.Value.header)).ToList(), headerRotateOption, height, fontSize);
        }

        /// <summary>
        /// Настраивает заголовки на рабочем листе Excel на основе объекта CM.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="cm">Объект CM, содержащий информацию о колонках.</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeader(IXLWorksheet ws, CM cm, HeaderRotateOption headerRotateOption = HeaderRotateOption.Vertical, int height = 90, int fontSize = 10)
        {
            ConfigureWorksheetHeaderInternal(ws, cm.GetIndexedHeaders().ToList(), headerRotateOption, height, fontSize);
        }

        /// <summary>
        /// Настраивает заголовки на рабочем листе Excel на основе объекта CM.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="cm">Объект CM, содержащий информацию о колонках.</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeader(ExcelWorksheet ws, CM cm, HeaderRotateOption headerRotateOption = HeaderRotateOption.Vertical, int height = 90, int fontSize = 10)
        {
            ConfigureWorksheetHeaderInternal(ws, cm.GetIndexedHeaders().ToList(), headerRotateOption, height, fontSize);
        }

        /// <summary>
        /// Настраивает заголовки на листе Excel.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="columns">Список колонок, где каждая пара состоит из индекса и заголовка.</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeaderInternal(IXLWorksheet ws, List<(int index, string header)> columns, HeaderRotateOption headerRotateOption, int height, int fontSize)
        {
            foreach (var (index, header) in columns)
            {
                ws.Cell(2, index).Value = header;
            }

            var headerRange = ws.Range(2, 1, 2, columns.Count);
            if (headerRotateOption == HeaderRotateOption.Vertical)
                headerRange.Style.Alignment.TextRotation = 90;

            headerRange.Style.Font.FontName = "Segoe UI Semibold";
            headerRange.Style.Font.FontSize = fontSize;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Alignment.WrapText = true;
            ws.Row(2).Height = height;
        }

        /// <summary>
        /// Конфигурирует заголовок таблицы Excel с использованием EPPlus.
        /// </summary>
        /// <param name="ws">Лист Excel.</param>
        /// <param name="columns">Список заголовков и их индексов.</param>
        /// <param name="headerRotateOption">Опция поворота заголовка.</param>
        /// <param name="height">Высота строки заголовка.</param>
        /// <param name="fontSize">Размер шрифта заголовка.</param>
        private static void ConfigureWorksheetHeaderInternal(ExcelWorksheet ws, List<(int index, string header)> columns, HeaderRotateOption headerRotateOption, int height, int fontSize)
        {
            // Установка заголовков
            foreach (var (index, header) in columns)
            {
                ws.Cells[2, index].Value = header; // Установка значения для ячейки
            }

            // Определение диапазона заголовков
            var headerRange = ws.Cells[2, 1, 2, columns.Count];

            // Поворот текста заголовка
            if (headerRotateOption == HeaderRotateOption.Vertical)
            {
                headerRange.Style.TextRotation = 90; // Вертикальный текст
            }

            // Настройка шрифта и выравнивания
            headerRange.Style.Font.Name = "Segoe UI Semibold";
            headerRange.Style.Font.Size = fontSize;
            headerRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            headerRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // Установка высоты строки заголовка
            ws.Row(2).Height = height;
        }

        /// <summary>
        /// Устанавливает заголовок в первую строку рабочего листа, растягивая его на указанные столбцы.
        /// </summary>
        /// <param name="ws">Рабочий лист, на котором устанавливается заголовок.</param>
        /// <param name="columnsCount">Количество столбцов, на которые будет растянут заголовок (для объединения ячеек).</param>
        /// <param name="title">Текст заголовка, который будет выведен в первой ячейке.</param>
        /// <param name="fontSize">Размер шрифта для заголовка. По умолчанию 14.</param>
        /// <param name="bold">Указывает, будет ли заголовок полужирным. По умолчанию true.</param>
        /// <param name="merge">Указывает, нужно ли объединять ячейки заголовка на весь диапазон столбцов. По умолчанию true.</param>
        /// <param name="alignment">Выравнивание текста в ячейке. По умолчанию выравнивание по левому краю.</param>
        private static void SetTitle(IXLWorksheet ws, int columnsCount, string title, int fontSize = 14, bool bold = true, bool merge = true, XLAlignmentHorizontalValues alignment = XLAlignmentHorizontalValues.Left)
        {
            var cell = ws.Cell(1, 1).SetValue(title)
                .Style.Font.SetFontSize(fontSize)
                .Font.SetBold(bold)
                .Alignment.SetHorizontal(alignment)
                .Alignment.SetWrapText(false);

            if (merge)
            {
                ws.Range(1, 1, 1, columnsCount).Merge();
            }
        }

        /// <summary>
        /// Устанавливает два заголовка: один слева, другой справа. Каждый заголовок может быть выделен полужирным.
        /// </summary>
        /// <param name="ws">Рабочий лист, на котором устанавливаются заголовки.</param>
        /// <param name="columnsCount">Количество столбцов, на которые будет растянут второй заголовок (справа).</param>
        /// <param name="left">Текст заголовка, который будет выведен слева в первой ячейке.</param>
        /// <param name="right">Текст заголовка, который будет выведен справа в последней ячейке первой строки.</param>
        /// <param name="fontSize">Размер шрифта для обоих заголовков. По умолчанию 14.</param>
        /// <param name="bold">Указывает, будет ли левый и правый заголовок полужирным. 
        /// Определяется с помощью перечисления <see cref="BoldOption">BoldOption</see>. По умолчанию оба заголовка будут полужирными.</param>
        private static void SetTitle(IXLWorksheet ws, int columnsCount, string left, string right, int fontSize = 14, BoldOption bold = BoldOption.Both)
        {
            var leftCell = ws.Cell(1, 1).SetValue(left)
                .Style.Font.SetFontSize(fontSize)
                .Font.SetBold(bold == BoldOption.Left || bold == BoldOption.Both)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                .Alignment.SetWrapText(false);

            var rightCell = ws.Cell(1, columnsCount).SetValue(right)
                .Style.Font.SetFontSize(fontSize)
                .Font.SetBold(bold == BoldOption.Right || bold == BoldOption.Both)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .Alignment.SetWrapText(false);
        }

        private static void FillTotalMachinesWorksheetData(IXLWorksheet ws, IEnumerable<IGrouping<string, Part>> machinesGroup, Dictionary<string, (int index, string header)> columns, List<Part> tempParts)
        {
            int row = 3;

            foreach (var mg in machinesGroup)
            {
                foreach (var pg in mg.OrderBy(p => p.PartName).GroupBy(p => p.PartName))
                {
                    var p = pg.ToList();
                    var tp = tempParts.Where(tp => tp.PartName == pg.Key).ToList();
                    ws.Cell(row, columns["machine"].index).Value = mg.Key;
                    ws.Cell(row, columns["part"].index).Value = pg.Key;
                    ws.Cell(row, columns["ordersCnt"].index).SetValue(pg.Select(p => p.Order).Distinct().Count());
                    ws.Cell(row, columns["partsCnt"].index).SetValue(pg.Where(p => p.Setup == 1).Sum(p => p.FinishedCount));
                    ws.Cell(row, columns["planSum"].index).SetValue(pg.Sum(p => p.PlanForBatch) + pg.SetupTimePlanForReport());
                    ws.Cell(row, columns["factSum"].index).SetValue(pg.FullWorkedTime().TotalMinutes);
                    ws.Cell(row, columns["ordersCntTotal"].index).SetValue(tp.Select(p => p.Order).Distinct().Count());
                    ws.Cell(row, columns["partsCntTotal"].index).SetValue(tp.Where(p => p.Setup == 1).Sum(p => p.FinishedCount));
                    ws.Cell(row, columns["planSumTotal"].index).SetValue(tp.Sum(p => p.PlanForBatch) + tp.SetupTimePlanForReport());
                    ws.Cell(row, columns["factSumTotal"].index).SetValue(tp.FullWorkedTime().TotalMinutes);
                    row++;
                }
            }

            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            ws.Columns().AdjustToContents();

            ws.Range(3, columns["machine"].index, row, columns["part"].index)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Columns(columns["ordersCnt"].index, columns["factSumTotal"].index).Width = 8;
            ws.Cell(1, 1).Value = $"Изготовленное с {tempParts.Min(p => p.StartSetupTime).ToString(Constants.ShortDateFormat)} по {tempParts.Max(p => p.EndMachiningTime).ToString(Constants.ShortDateFormat)} ";
            ws.Range(1, columns["machine"].index, 1, columns["factSumTotal"].index).Merge();
            ws.SheetView.FreezeRows(2);
        }

        private static void FillMachineWorksheetData(IXLWorksheet ws, IEnumerable<Part> wsParts, Dictionary<string, (int index, string header)> columns)
        {
            int row = 3;
            int ordersCnt = 0;

            foreach (var pg in wsParts.OrderBy(p => p.PartName).GroupBy(p => p.PartName))
            {
                var p = pg.ToList();
                ws.Cell(row, columns["part"].index).Value = pg.Key;
                var orders = pg.Select(p => p.Order).Distinct().Count();
                ordersCnt += orders;
                ws.Cell(row, columns["ordersCnt"].index).SetValue(orders);
                ws.Cell(row, columns["partsCnt"].index).SetValue(pg.Where(p => p.Setup == 1).Sum(p => p.FinishedCount));
                ws.Cell(row, columns["planSum"].index).SetValue(pg.Sum(p => p.PlanForBatch) + pg.SetupTimePlanForReport());
                ws.Cell(row, columns["factSum"].index).SetValue(pg.FullWorkedTime().TotalMinutes);
                row++;
            }

            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            ws.Columns().AdjustToContents();

            ws.Range(3, columns["machine"].index, row, columns["part"].index)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Columns(columns["ordersCnt"].index, columns["factSum"].index).Width = 8;
            ws.Column(1).Delete();
            ws.Columns(6, 9).Delete();
            ws.Cell(1, 1).Value = $"Изготовленное с {wsParts.Min(p => p.StartSetupTime).ToString(Constants.ShortDateFormat)} по {wsParts.Max(p => p.EndMachiningTime).ToString(Constants.ShortDateFormat)} " +
                $"на станке {ws.Name} (всего м/л: {ordersCnt}, " +
                $"деталей: {wsParts.Where(p => p.Setup == 1).Sum(p => p.FinishedCount)})";
            ws.Range(1, columns["part"].index, 1, columns["factSum"].index).Merge();
            ws.SheetView.FreezeRows(2);
        }

        static bool IsInvalidRatio(double ratio) =>
            ratio == 0 || double.IsInfinity(ratio) || double.IsNaN(ratio);

        static TimeSpan NotExcludedTime(Part part, double minPartsCount, double maxPartsCount)
        {
            var span = TimeSpan.Zero;
            if (!part.ExcludeFromReports)
            {
                if (part.SetupRatio is not (0 or double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                {
                    span = span.Add(TimeSpan.FromMinutes(part.SetupTimeFact)).Add(TimeSpan.FromMinutes(part.SetupDowntimes));
                }
                if (part.ProductionRatio != 0 && part.FinishedCountFact >= minPartsCount && part.FinishedCountFact < maxPartsCount)
                {
                    span = span.Add(TimeSpan.FromMinutes(part.ProductionTimeFact)).Add(TimeSpan.FromMinutes(part.MachiningDowntimes));
                }
            }
            return span;
        }
    }
}
