using ClosedXML.Excel;
using libeLog;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Excel;
using libeLog.Models;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
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
            cm.Add(CM.MasterSetupDetail);
            cm.Add(CM.MasterComment);
            cm.Add(CM.FixedSetupTimePlan);
            cm.Add(CM.FixedProductionTimePlan);
            cm.Add(CM.EngineerConclusion);

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
                // Итоговая классификация: переопределение СГТ, если оно есть, иначе отметка мастера.
                ws.Cell(row, ci[CM.MasterSetupComment]).SetValue(part.EffectiveSetupReason);
                ws.Cell(row, ci[CM.MasterSetupDetail]).SetValue(part.MasterSetupDetail);
                ws.Cell(row, ci[CM.MasterComment]).SetValue(part.MasterComment);
                ws.Cell(row, ci[CM.FixedSetupTimePlan]).SetValue(part.FixedSetupTimePlan);
                ws.Cell(row, ci[CM.FixedProductionTimePlan]).SetValue(part.FixedProductionTimePlan);
                ws.Cell(row, ci[CM.EngineerConclusion]).SetValue(part.EngineerConclusion);
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
            ws.Column(ci[CM.MasterSetupDetail]).Width = 20;
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

            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
