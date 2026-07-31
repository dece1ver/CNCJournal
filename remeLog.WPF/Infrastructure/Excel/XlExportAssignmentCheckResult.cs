using ClosedXML.Excel;
using libeLog;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Excel;
using libeLog.Models;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
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
            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
