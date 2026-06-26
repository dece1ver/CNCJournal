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
using System.IO;
using System.Linq;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
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

            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
