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
        /// Поля, за валидацию которых отвечает Part.this[columnName]
        /// (норматив=0 при заказе, КПД вне диапазона, превышение частичной наладки,
        /// простои &gt;50% без комментария, некорректная привязка причины к порогам и т.п.).
        /// </summary>
        private static readonly string[] ValidatedColumns = new[]
        {
            nameof(Part.MasterSetupComment),
            nameof(Part.MasterMachiningComment),
            nameof(Part.MasterComment),
            nameof(Part.SpecifiedDowntimesComment),
        };

        /// <summary>
        /// Записи, которые сейчас не проходят валидацию мастера (Part.Error непуст) —
        /// то, что реально блокирует закрытие суточного отчёта. Список нужен, чтобы
        /// одним запуском найти все такие строки за период, не выискивая их вручную
        /// по красным полям в гриде.
        /// </summary>
        public static string ExportValidationErrors(ICollection<Part> parts, string path)
        {
            var errorRows = parts
                .Select(p => (Part: p, Errors: CollectValidationErrors(p)))
                .Where(x => x.Errors.Count > 0)
                .ToList();

            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Ошибки валидации");
            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var cm = new CM();
            cm.Add(CM.Machine);
            cm.Add(CM.Date);
            cm.Add(CM.Shift);
            cm.Add(CM.Operator);
            cm.Add(CM.Part);
            cm.Add(CM.Order);
            cm.Add(CM.Setup);
            cm.Add(CM.SetupTimePlan);
            cm.Add(CM.SingleProductionTimePlan);
            cm.Add(CM.MasterSetupComment);
            cm.Add(CM.MasterProductionComment);
            cm.Add(CM.MasterComment);
            cm.Add(CM.ValidationErrors);

            ConfigureWorksheetHeader(ws, cm);
            var ci = cm.GetIndexes();
            var row = 3;

            foreach (var (part, errors) in errorRows)
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
                ws.Cell(row, ci[CM.SetupTimePlan]).SetValue(part.SetupTimePlan);
                ws.Cell(row, ci[CM.SingleProductionTimePlan]).SetValue(part.SingleProductionTimePlan);
                ws.Cell(row, ci[CM.MasterSetupComment]).SetValue(part.MasterSetupComment);
                ws.Cell(row, ci[CM.MasterProductionComment]).SetValue(part.MasterMachiningComment);
                ws.Cell(row, ci[CM.MasterComment])
                    .SetValue(part.MasterComment)
                    .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, ci[CM.ValidationErrors])
                    .SetValue(string.Join("; ", errors))
                    .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                row++;
            }

            if (errorRows.Count > 0)
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
            ws.Column(ci[CM.MasterSetupComment]).Width = 20;
            ws.Column(ci[CM.MasterProductionComment]).Width = 20;
            ws.Column(ci[CM.MasterComment]).Width = 25;
            ws.Column(ci[CM.ValidationErrors]).Width = 35;

            var period = parts.Count > 0
                ? $"{parts.Min(p => p.ShiftDate).ToString(Constants.ShortDateFormat)} до {parts.Max(p => p.ShiftDate).ToString(Constants.ShortDateFormat)}"
                : "—";
            SetTitle(ws, cm.Count, $"Записи без объяснения мастера: {errorRows.Count} из {parts.Count} за период от {period}");
            ws.SheetView.FreezeRows(2);

            wb.SaveAndOfferOpen(path);
            return path;
        }

        /// <summary> Есть ли среди переданных деталей хотя бы одна с ошибкой валидации. </summary>
        public static bool HasValidationErrors(IEnumerable<Part> parts) =>
            parts.Any(p => CollectValidationErrors(p).Count > 0);

        private static List<string> CollectValidationErrors(Part part)
        {
            var errors = new List<string>();
            foreach (var column in ValidatedColumns)
            {
                var message = part[column];
                if (!string.IsNullOrWhiteSpace(message))
                    errors.Add(message);
            }
            return errors;
        }
    }
}
