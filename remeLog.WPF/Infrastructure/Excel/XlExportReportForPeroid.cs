using ClosedXML.Excel;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Excel;
using libeLog.Models;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Windows;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
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

            var machinesResult = Database.ReadMachines(); var machines = machinesResult.Value ?? new List<string>();
            var shiftsResult = Database.GetShiftsByPeriod(machines, fromDate, toDate, shift); var shifts = shiftsResult.Value ?? new List<ShiftInfo>();
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
                int IdleShiftsByReason(string reason) => shifts.Count(s =>
                    s.Machine == partGroup.Key
                    && s.DowntimesComment == reason
                    && !AppSettings.Holidays.Contains(s.ShiftDate)
                    && s.IsIdleWholeShift);

                ws.Cell(row, ci[CM.WorkedShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && !s.IsIdleWholeShift);
                ws.Cell(row, ci[CM.NoOperatorShifts]).Value = IdleShiftsByReason(DowntimeReasons.NoOperator);
                ws.Cell(row, ci[CM.HardwareRepairShifts]).Value = IdleShiftsByReason(DowntimeReasons.HardwareRepair);
                ws.Cell(row, ci[CM.NoPowerShifts]).Value = IdleShiftsByReason(DowntimeReasons.NoPower);
                ws.Cell(row, ci[CM.ProcessRelatedLossShifts]).Value = IdleShiftsByReason(DowntimeReasons.ProcessRelatedLoss);
                ws.Cell(row, ci[CM.UnspecifiedOtherShifts]).Value = IdleShiftsByReason(DowntimeReasons.Other);
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
            var dataRange = ws.RangeUsed();

            ws.ApplyStandardBorders();

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
            ws.ApplyAutoFilter();
            ws.Column(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Range(1, 1, 2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.AdjustColumns();
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
                        .Add(CM.MasterSetupDetail)
                        .Add(CM.ProductionRatioTitle)
                        .Add(CM.MasterProductionComment)
                        .Add(CM.MasterMachiningDetail)
                        .Add(CM.MasterComment)
                        .Add(CM.FixedSetupTimePlan)
                        .Add(CM.FixedProductionTimePlan)
                        .Add(CM.EngineerConclusion)
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
                wst.ApplyStandardBorders();
                wst.AdjustColumns();


                wst.RangeUsed().Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                wst.ApplyAutoFilter();
                wst.AdjustColumns();
                wst.Row(1).Delete();
                wst.SheetView.FreezeRows(1);

            }
            progress?.Report("Формирование завершено, сохранение файла...");
            wb.SaveAndOfferOpen(path);
            return $"Файл сохранен в \"{path}\"";
        }
    }
}
