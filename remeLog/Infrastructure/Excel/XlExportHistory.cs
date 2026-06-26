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
            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
