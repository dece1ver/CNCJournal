using ClosedXML.Excel;
using libeLog;
using libeLog.Infrastructure.Excel;
using System.Collections.Generic;
using System.Linq;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        /// <summary>
        /// Записи, где СГТ переопределил причину мастера (наладки и/или изготовления) —
        /// то, что мастер отметил как «Некорректное заполнение» и подобные причины, требующие
        /// исправления аналитиком. Отчёт показывает контекст записи рядом с указанной мастером
        /// причиной и тем, на что её переопределил СГТ, — без этого разбор пришлось бы вести
        /// по гриду вручную, ячейка за ячейкой. Причина/комментарий сведены в одну ячейку
        /// («было → стало»), чтобы не плодить по 4 колонки на каждую категорию.
        /// </summary>
        public static string ExportIncorrectFills(ICollection<Part> parts, string path)
        {
            var overriddenParts = parts
                .Where(p => p.HasSetupReasonOverride || p.HasMachiningReasonOverride)
                .ToList();

            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Переопределённые причины");
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
                .Add(CM.Setup)
                .Add(CM.SetupReasonSummary)
                .Add(CM.SetupDetailSummary)
                .Add(CM.SetupFaultComment)
                .Add(CM.MachiningReasonSummary)
                .Add(CM.MachiningDetailSummary)
                .Add(CM.MachiningFaultComment)
                .Add(CM.MasterFault)
                .Add(CM.ReasonOverrideBy)
                .Add(CM.ReasonOverrideAt)
                .Build();

            ConfigureWorksheetHeader(ws, cm);
            var ci = cm.GetIndexes();
            var row = 3;

            foreach (var part in overriddenParts)
            {
                ws.Cell(row, ci[CM.Machine]).SetValue(part.Machine);
                ws.Cell(row, ci[CM.Date])
                    .SetValue(part.ShiftDate)
                    .Style.DateFormat.Format = "dd.MM.yy";
                ws.Cell(row, ci[CM.Shift]).SetValue(part.Shift);
                ws.Cell(row, ci[CM.Operator]).SetValue(part.Operator);
                ws.Cell(row, ci[CM.Part]).SetValue(part.PartName);
                ws.Cell(row, ci[CM.Order]).SetValue(part.Order);
                ws.Cell(row, ci[CM.Setup]).SetValue(part.Setup);

                if (part.HasSetupReasonOverride)
                {
                    ws.Cell(row, ci[CM.SetupReasonSummary])
                        .SetValue(BuildSummary(part.MasterSetupComment, part.SetupReasonOverride))
                        .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    ws.Cell(row, ci[CM.SetupDetailSummary])
                        .SetValue(BuildSummary(part.MasterSetupDetail, part.SetupReasonOverrideComment))
                        .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    if (part.SetupReasonOverrideIsMasterFault)
                        ws.Cell(row, ci[CM.SetupFaultComment])
                            .SetValue(part.SetupReasonOverrideMasterFaultComment)
                            .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }

                if (part.HasMachiningReasonOverride)
                {
                    ws.Cell(row, ci[CM.MachiningReasonSummary])
                        .SetValue(BuildSummary(part.MasterMachiningComment, part.MachiningReasonOverride))
                        .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    ws.Cell(row, ci[CM.MachiningDetailSummary])
                        .SetValue(BuildSummary(part.MasterMachiningDetail, part.MachiningReasonOverrideComment))
                        .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    if (part.MachiningReasonOverrideIsMasterFault)
                        ws.Cell(row, ci[CM.MachiningFaultComment])
                            .SetValue(part.MachiningReasonOverrideMasterFaultComment)
                            .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }

                ws.Cell(row, ci[CM.MasterFault]).SetValue(
                    (part.HasSetupReasonOverride && part.SetupReasonOverrideIsMasterFault)
                    || (part.HasMachiningReasonOverride && part.MachiningReasonOverrideIsMasterFault));

                ws.Cell(row, ci[CM.ReasonOverrideBy]).SetValue(part.ReasonOverrideBy);
                if (part.ReasonOverrideAt.HasValue)
                    ws.Cell(row, ci[CM.ReasonOverrideAt])
                        .SetValue(part.ReasonOverrideAt.Value)
                        .Style.DateFormat.Format = "dd.MM.yy";

                row++;
            }

            if (overriddenParts.Count > 0)
            {
                ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                ws.RangeUsed().SetAutoFilter(true);
            }
            ws.Columns().AdjustToContents();

            ws.Range(3, ci[CM.Machine], row, ci[CM.Machine])
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Column(ci[CM.Operator]).Width = 15;
            ws.Column(ci[CM.Part]).Width = 25;
            ws.Column(ci[CM.SetupReasonSummary]).Width = 30;
            ws.Column(ci[CM.SetupDetailSummary]).Width = 30;
            ws.Column(ci[CM.SetupFaultComment]).Width = 25;
            ws.Column(ci[CM.MachiningReasonSummary]).Width = 30;
            ws.Column(ci[CM.MachiningDetailSummary]).Width = 30;
            ws.Column(ci[CM.MachiningFaultComment]).Width = 25;

            var period = parts.Count > 0
                ? $"{parts.Min(p => p.ShiftDate).ToString(Constants.ShortDateFormat)} до {parts.Max(p => p.ShiftDate).ToString(Constants.ShortDateFormat)}"
                : "—";
            SetTitle(ws, cm.Count, $"Переопределённые причины (некорректное заполнение мастера): {overriddenParts.Count} из {parts.Count} за период от {period}");
            ws.SheetView.FreezeRows(2);

            wb.SaveAndOfferOpen(path);
            return path;
        }

        /// <summary>
        /// Сводит отметку мастера и решение СГТ в одну ячейку: «было → стало». Если поля
        /// совпадают или второе пусто, стрелка не нужна — показывает только то, что есть.
        /// </summary>
        private static string BuildSummary(string masterValue, string overrideValue)
        {
            var master = string.IsNullOrWhiteSpace(masterValue) ? "не указано" : masterValue;
            if (string.IsNullOrWhiteSpace(overrideValue) || overrideValue == masterValue)
                return master;
            return $"{master}\n→ {overrideValue}";
        }

        /// <summary> Есть ли среди переданных деталей хотя бы одна с переопределённой причиной. </summary>
        public static bool HasReasonOverrides(IEnumerable<Part> parts) =>
            parts.Any(p => p.HasSetupReasonOverride || p.HasMachiningReasonOverride);
    }
}
