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
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        /// <summary>
        /// Результат построения листа-таблицы по станкам за один период —
        /// используется как для одиночного отчёта, так и для отчёта сравнения периодов.
        /// </summary>
        private readonly struct PeriodDataSheet
        {
            public PeriodDataSheet(IXLWorksheet ws, Dictionary<string, int> ci, int totalRow, string title, IOrderedEnumerable<Part> filteredParts)
            {
                Ws = ws;
                Ci = ci;
                TotalRow = totalRow;
                Title = title;
                FilteredParts = filteredParts;
            }

            public IXLWorksheet Ws { get; }
            public Dictionary<string, int> Ci { get; }
            /// <summary>Строка "Итог:" — здесь лежат агрегаты по всем станкам за период.</summary>
            public int TotalRow { get; }
            public string Title { get; }
            public IOrderedEnumerable<Part> FilteredParts { get; }
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

            progress?.Report("Получение списка серийных деталей...");
            var serialParts = await libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!);
            var serialPartNames = serialParts.Select(p => p.PartName.NormalizedPartNameWithoutComments()).ToImmutableHashSet();

            var wb = new XLWorkbook();

            var period = BuildPeriodDataSheet(wb, "Отчет за период", parts, fromDate, toDate, shift, serialPartNames, progress);

            progress?.Report("Формирование сводки...");
            var summaryWs = CreateSummaryWorksheet(wb, "Сводка");
            WriteSummaryBlock(summaryWs, 1, period.Title, period.Ws, period.Ci, period.TotalRow);

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
            var tci = tcm.GetIndexes();
            var listRow = 3;
            foreach (var part in totalParts.DistinctBy(p => p.PartName))
            {
                wst.Cell(listRow, tci[CM.Part]).SetValue(part.PartName);
                wst.Cell(listRow, tci[CM.SerialPerList]).SetValue(serialPartNames.Contains(part.PartName.NormalizedPartNameWithoutComments()));
                listRow++;
            }
            wst.Row(1).Delete();
            var totalTable = wst.RangeUsed().CreateTable();
            totalTable.Theme = XLTableTheme.TableStyleLight15;
            wst.Column(tci[CM.Part]).Width = 70;
            wst.Column(tci[CM.SerialPerList]).Width = 14;


            var partsByMachine = period.FilteredParts.GroupBy(p => p.Machine).ToDictionary(g => g.Key, g => g.ToList());

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
                        .Add(CM.ToolChangingTime)
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
            foreach (var machine in partsByMachine.Keys)
            {
                progress?.Report($"Формирование листа по станку {machine}...");
                ConfigureMachineSheetForPeriod(wb, partsByMachine[machine], machine, mcm, serialPartNames);
            }
            progress?.Report("Формирование завершено, сохранение файла...");
            wb.SaveAndOfferOpen(path);
            return $"Файл сохранен в \"{path}\"";
        }

        /// <summary>
        /// Строит лист-таблицу по станкам за один период (общий и для одиночного отчёта,
        /// и для каждого из двух периодов в отчёте сравнения периодов).
        /// </summary>
        private static PeriodDataSheet BuildPeriodDataSheet(
            XLWorkbook wb, string sheetName, ICollection<Part> parts, DateTime fromDate, DateTime toDate, Shift shift,
            ImmutableHashSet<string> serialPartNames, IProgress<string> progress)
        {
            var totalParts = parts;

            var machinesResult = Database.ReadMachines(); var machines = machinesResult.Value ?? new List<string>();
            var shiftsResult = Database.GetShiftsByPeriod(machines, fromDate, toDate, shift); var shifts = shiftsResult.Value ?? new List<ShiftInfo>();
            var totalDays = Util.GetWorkDaysBeetween(fromDate, toDate);

            var ws = wb.AddWorksheet(sheetName);
            ws.Style.Font.FontSize = 11;

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
                .Add(CM.AverageDowntimesTime, "Среднее время простоев, час")
                .Add(CM.AverageSetupNormative, $"Среднее время{Environment.NewLine}норматива наладки, час")
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
                .Add(CM.SetupsCount)
                .Build();

            var headerRow = 2;
            var ci = cm.GetIndexes();
            ConfigureWorksheetHeader(ws, cm, HeaderRotateOption.Vertical, 105, 8);
            var headerRange = ws.Range(2, 1, 2, cm.Count);

            var row = 3;
            var firstDataRow = row;

            var filteredParts = parts
                .GroupBy(p => p.Machine)
                .SelectMany(machineGroup =>
                    machineGroup
                        .GroupBy(p => p.PartName)
                        .SelectMany(partGroup => partGroup))
                .OrderBy(p => p.Machine);

            progress?.Report($"Формирование листа «{sheetName}»...");

            foreach (var partGroup in filteredParts.GroupBy(p => p.Machine).OrderBy(pg => pg.Key))
            {
                ws.Row(row).Height = 20;
                var totalMachineParts = partGroup.OrderBy(p => p.StartSetupTime);
                parts = totalMachineParts.ToList();
                double totalWorkedMinutes = parts.FullWorkedTime().TotalMinutes;

                ws.Cell(row, ci[CM.Machine]).Value = partGroup.Key;

                ws.Cell(row, ci[CM.WorkedShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s is not ({ Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 }));
                ws.Cell(row, ci[CM.NoOperatorShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Отсутствие оператора" && !AppSettings.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.HardwareRepairShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Ремонт оборудования" && !AppSettings.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.NoPowerShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Отсутствие электричества" && !AppSettings.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.ProcessRelatedLossShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Организационные потери" && !AppSettings.Holidays.Contains(s.ShiftDate) && s is { Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 });
                ws.Cell(row, ci[CM.UnspecifiedOtherShifts]).Value = shifts.Count(s => s.Machine == partGroup.Key && s.DowntimesComment == "Другое" && !AppSettings.Holidays.Contains(s.ShiftDate) && s is ({ Shift: "День", UnspecifiedDowntimes: 660 } or { Shift: "Ночь", UnspecifiedDowntimes: 630 }));

                ws.Cell(row, ci[CM.SetupRatio]).Value = parts.AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioIncludePartialSetups]).Value = parts.AverageSetupRatioInclurePartialSetups();
                ws.Cell(row, ci[CM.SetupRatioIncludeDowntimes]).Value = parts.AverageSetupRatioIncludeDowntimes();
                ws.Cell(row, ci[CM.ProductionRatio]).Value = parts.ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioIncludeDowntimes]).Value = parts.ProductionRatioIncludeDowntimes();

                var setupUnderRatio = parts.Where(p => !serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioUnder]).Value = setupUnderRatio;

                var productionUnderRatio = parts.Where(p => !serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioUnder]).Value = productionUnderRatio;

                var setupOverRatio = parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).AverageSetupRatio();
                ws.Cell(row, ci[CM.SetupRatioOver]).Value = setupOverRatio;

                var productionOverRatio = parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatioOver]).Value = productionOverRatio;

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
                ws.Cell(row, ci[CM.ProductionEfficiencyToTotalRatio]).Value = prodTimePlanSum / totalWorkedMinutes;

                ws.Cell(row, ci[CM.MachineTimeToTotalRatio]).SetFormulaA1($"{ws.Cell(row, ci[CM.TotalMachinigTime]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.TotalTime]).Address.ToStringRelative()}");
                var sumDowntimes = parts.Sum(p => p.SetupDowntimes + p.MachiningDowntimes);
                ws.Cell(row, ci[CM.AverageDowntimesTime]).SetValue(sumDowntimes / parts.Count / 60);
                ws.Cell(row, ci[CM.AverageSetupNormative]).SetValue(totalMachineParts.AverageSetupNormatives().TotalHours);
                ws.Cell(row, ci[CM.AverageSetupTime]).SetValue(parts.AverageSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalSetupTime]).SetValue(parts.TotalSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalSetupTimeSerial]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).TotalSetupTime().TotalHours);
                ws.Cell(row, ci[CM.TotalProductionTime]).SetValue(parts.TotalProductionTime().TotalHours);
                ws.Cell(row, ci[CM.TotalProductionTimeSerial]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).TotalProductionTime().TotalHours);
                ws.Cell(row, ci[CM.TotalDowntimesTime]).SetValue(parts.TotalDowntimesTime().TotalHours);
                ws.Cell(row, ci[CM.TotalDowntimesTimeSerial]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).TotalDowntimesTime().TotalHours);
                ws.Cell(row, ci[CM.TotalTime]).SetValue(totalWorkedMinutes / 60);

                ws.Cell(row, ci[CM.SerialPartsTime]).SetValue(parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).FullWorkedTime().TotalHours);
                ws.Cell(row, ci[CM.NonSerialPartsTime]).SetValue(parts.Where(p => !serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).FullWorkedTime().TotalHours);
                ws.Cell(row, ci[CM.SerialPartsTimeRatio]).FormulaA1 = $"{ws.Cell(row, ci[CM.SerialPartsTime]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.TotalTime]).Address.ToStringRelative()}";

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

                ws.Cell(row, ci[CM.Orders]).Value = parts.Where(p => !p.Order.EqualsOrdinalIgnoreCase("Без М/Л")).Select(p => p.Order).Distinct().Count();
                ws.Cell(row, ci[CM.SerialOrders]).Value = parts.Where(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments())).DistinctBy(p => p.Order).Count();
                ws.Cell(row, ci[CM.SerialOrdersRatio]).FormulaA1 = $"={ws.Cell(row, ci[CM.SerialOrders]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.Orders]).Address.ToStringRelative()}";
                ws.Cell(row, ci[CM.SerialCount]).Value = parts.Count(p => serialPartNames.Contains(p.PartName.NormalizedPartNameWithoutComments()));
                ws.Cell(row, ci[CM.CountPerMachine]).Value = parts.Count;
                ws.Cell(row, ci[CM.SerialCountRatio]).FormulaA1 = $"={ws.Cell(row, ci[CM.SerialCount]).Address.ToStringRelative()}/{ws.Cell(row, ci[CM.CountPerMachine]).Address.ToStringRelative()}";

                ws.Range(row, ci[CM.SetupRatio], row, ci[CM.ProductionEfficiencyToTotalRatio])
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
                ws.Cell(row, ci[CM.TotalMachinigTime])
                    .SetValue(parts.Sum(p => p.MachiningTime.TotalHours * p.FinishedCount))
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Integer;ws.Cell(row, ci[CM.AverageReplacementTime]);
                ws.Cell(row, ci[CM.AverageReplacementTime]).SetValue(parts.AverageReplacementTimeRatio())
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

                ws.Cell(row, ci[CM.SetupsCount]).Value = totalMachineParts.Count(p => p.SetupTimeFact > 0) + totalMachineParts.Count(p => p.PartialSetupTime > 0) / 2;

                double totalReplacementTime = parts.Aggregate(0.0, (acc, p) => double.IsFinite(p.PartReplacementTime) ? acc + p.PartReplacementTime * p.FinishedCount : acc);

                ws.Range(row, ci[CM.SpecifiedDowntimes], row, ci[CM.SpecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                row++;
            }

            var lastDataRow = row - 1;

            var dataRange = ws.RangeUsed();
            var table = dataRange.CreateTable();
            table.Name = $"Report_{ws.Position}";
            table.Theme = XLTableTheme.None;

            ws.ApplyStandardBorders();

            ws.Range(headerRow, ci[CM.Machine], lastDataRow, ci[CM.Machine]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.ProductionRatioIncludeDowntimes]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SetupRatioOver], lastDataRow, ci[CM.ProductionRatioOver]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.AverageSetupTime], lastDataRow, ci[CM.SerialPartsTimeRatio]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.ProductionEfficiencyToTotalRatio], lastDataRow, ci[CM.SetupToTotalRatio]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.TotalSetupTime], lastDataRow, ci[CM.TotalTime]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.NonSerialPartsTime], lastDataRow, ci[CM.TotalTime]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.SpecifiedDowntimes], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, ci[CM.Orders], lastDataRow, ci[CM.SerialOrdersRatio]).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.ProductionRatioIncludeDowntimes]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.8);
            ws.Range(headerRow, ci[CM.SetupRatioUnder], lastDataRow, ci[CM.ProductionRatioUnder]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent2, 0.8);
            ws.Range(headerRow, ci[CM.SetupRatioOver], lastDataRow, ci[CM.ProductionRatioOver]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent3, 0.8);
            ws.Range(headerRow, ci[CM.AverageSetupTime], lastDataRow, ci[CM.SerialPartsTimeRatio]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent4, 0.8);
            ws.Range(headerRow, ci[CM.ProductionEfficiencyToTotalRatio], lastDataRow, ci[CM.ProductionToTotalRatio]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent5, 0.8);
            ws.Range(headerRow, ci[CM.SpecifiedDowntimes], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent6, 0.8);

            ws.Range(headerRow, ci[CM.SetupRatio], lastDataRow, ci[CM.UnspecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Range(firstDataRow, ci[CM.TotalSetupTime], lastDataRow, ci[CM.TotalTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Range(firstDataRow, ci[CM.TotalMachinigTime], lastDataRow, ci[CM.TotalTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Columns(ci[CM.AverageReplacementTime], ci[CM.AverageSetupTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
            ws.Column(ci[CM.SerialPartsTimeRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Column(ci[CM.SerialOrdersRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Column(ci[CM.SerialCountRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

            ws.Range(firstDataRow, ci[CM.WorkedShifts], lastDataRow, ci[CM.WorkedShifts]).Style.Font.FontColor = XLColor.Red;
            ws.Range(firstDataRow, ci[CM.WorkedShifts], lastDataRow, ci[CM.WorkedShifts]).AddConditionalFormat().WhenEquals($"=$B${lastDataRow + 2}").Font.FontColor = XLColor.Green;

            dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Column(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Range(1, 1, 2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.AdjustColumns();
            ws.Column(ci[CM.Machine]).Width = 23;
            ws.Columns(2, cm.Count).Width = 7;

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
            ws.Cell(row, ci[CM.SetupsCount]).FormulaA1 = $"SUBTOTAL(109, {ws.Range(firstDataRow, ci[CM.SetupsCount], lastDataRow, ci[CM.SetupsCount])})";

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

            ws.Range(row, ci[CM.WorkedShifts], row, ci[CM.UnspecifiedOtherShifts]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentPrecision2;
            ws.Range(row, ci[CM.SetupRatio], row, ci[CM.UnspecifiedDowntimes]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Range(row, ci[CM.AverageReplacementTime], row, ci[CM.AverageSetupTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
            ws.Range(row, ci[CM.TotalSetupTime], row, ci[CM.TotalTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Range(row, ci[CM.Finished], row, ci[CM.AveragePartsCount]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Cell(row, ci[CM.TotalMachinigTime]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            row++;
            ws.Cell(row, ci[CM.Machine]).Value = "Рабочих смен:";
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, ci[CM.Machine]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Value = shift.Type == ShiftType.All ? totalDays * 2 : totalDays;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, ci[CM.WorkedShifts]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            ws.Columns(ci[CM.NoOperatorShifts], ci[CM.UnspecifiedOtherShifts]).Collapse();
            ws.Columns(ci[CM.CreateNcProgramTime], ci[CM.HardwareFailureTime]).Collapse();
            ws.Columns(ci[CM.NoOperatorShifts], ci[CM.UnspecifiedOtherShifts]).Group();
            ws.Columns(ci[CM.CreateNcProgramTime], ci[CM.HardwareFailureTime]).Group();

            return new PeriodDataSheet(ws, ci, row - 1, title, filteredParts);
        }

        /// <summary>Создаёт пустой лист под один или несколько блоков сводки (см. <see cref="WriteSummaryBlock"/>).</summary>
        private static IXLWorksheet CreateSummaryWorksheet(XLWorkbook wb, string sheetName)
        {
            var ws = wb.AddWorksheet(sheetName);
            ws.Style.Font.FontSize = 11;
            return ws;
        }

        /// <summary>
        /// Пишет один блок сводки (Общее/Серийная/Не серийная продукция: доли и часы наладки,
        /// изготовления и простоев), начиная со строки <paramref name="startRow"/>. Все ячейки —
        /// формулы, ссылающиеся на строку "Итог:" листа-источника, значения нигде не копируются.
        /// Возвращает номер последней использованной строки блока.
        /// </summary>
        private static int WriteSummaryBlock(
            IXLWorksheet ws, int startRow, string title,
            IXLWorksheet sourceWs, Dictionary<string, int> sourceCi, int sourceTotalRow)
        {
            string[] headers =
            {
                "Серийность", "Наладка", "Изготовление", "Отмеченные простои",
                "Время наладки, час", "Время изготовления, час", "Время простоев, час", "Время общее, час"
            };

            var titleRow = startRow;
            var headerRow = startRow + 1;
            var totalRow = startRow + 2;
            var serialRow = startRow + 3;
            var nonSerialRow = startRow + 4;

            ws.Cell(titleRow, 1).Value = title;
            ws.Range(titleRow, 1, titleRow, headers.Length).Merge();
            ws.Range(titleRow, 1, titleRow, 1).Style.Font.SetFontSize(14).Font.SetBold(true);

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(headerRow, i + 1).Value = headers[i];
            ws.Range(headerRow, 1, headerRow, headers.Length).Style.Font.Bold = true;
            ws.Range(headerRow, 1, headerRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.8);
            ws.Range(headerRow, 1, headerRow, headers.Length).Style.Alignment.WrapText = true;

            // Ссылка на ячейку строки "Итог:" листа-источника по ключу колонки CM.
            string SourceRef(string columnKey) =>
                $"'{sourceWs.Name}'!{sourceWs.Cell(sourceTotalRow, sourceCi[columnKey]).Address}";

            ws.Cell(totalRow, 1).Value = "Общее";
            ws.Cell(totalRow, 5).FormulaA1 = SourceRef(CM.TotalSetupTime);
            ws.Cell(totalRow, 6).FormulaA1 = SourceRef(CM.TotalProductionTime);
            ws.Cell(totalRow, 7).FormulaA1 = SourceRef(CM.TotalDowntimesTime);
            ws.Cell(totalRow, 8).FormulaA1 = SourceRef(CM.TotalTime);

            ws.Cell(serialRow, 1).Value = "Серийная продукция";
            ws.Cell(serialRow, 5).FormulaA1 = SourceRef(CM.TotalSetupTimeSerial);
            ws.Cell(serialRow, 6).FormulaA1 = SourceRef(CM.TotalProductionTimeSerial);
            ws.Cell(serialRow, 7).FormulaA1 = SourceRef(CM.TotalDowntimesTimeSerial);
            ws.Cell(serialRow, 8).FormulaA1 = SourceRef(CM.SerialPartsTime);

            ws.Cell(nonSerialRow, 1).Value = "Не серийная продукция";
            // Наладка/изготовление/простои не серийной продукции отдельными колонками в
            // источнике не считаются — только их сумма (NonSerialPartsTime), поэтому здесь
            // единственный вариант "не копировать значение" — вычесть Серийную из Общего
            // (обе уже являются ссылками на источник, см. выше).
            for (int col = 5; col <= 7; col++)
                ws.Cell(nonSerialRow, col).FormulaA1 = $"{ws.Cell(totalRow, col).Address}-{ws.Cell(serialRow, col).Address}";
            ws.Cell(nonSerialRow, 8).FormulaA1 = SourceRef(CM.NonSerialPartsTime);

            for (int r = totalRow; r <= nonSerialRow; r++)
            {
                ws.Cell(r, 2).FormulaA1 = $"IFERROR({ws.Cell(r, 5).Address}/{ws.Cell(r, 8).Address},0)";
                ws.Cell(r, 3).FormulaA1 = $"IFERROR({ws.Cell(r, 6).Address}/{ws.Cell(r, 8).Address},0)";
                ws.Cell(r, 4).FormulaA1 = $"IFERROR({ws.Cell(r, 7).Address}/{ws.Cell(r, 8).Address},0)";
            }

            ws.Range(totalRow, 2, nonSerialRow, 4).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
            ws.Range(totalRow, 5, nonSerialRow, 8).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.IntegerWithSeparator;

            var body = ws.Range(headerRow, 1, nonSerialRow, headers.Length);
            body.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Range(totalRow, 1, nonSerialRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Range(headerRow, 1, nonSerialRow, headers.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow, 1, nonSerialRow, headers.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            if (ws.Column(1).Width < 24) ws.Column(1).Width = 24;
            for (int col = 2; col <= headers.Length; col++)
                if (ws.Column(col).Width < 15) ws.Column(col).Width = 15;

            return nonSerialRow;
        }
    }
}
