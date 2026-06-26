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
        public static string ExportPartsInfo(ICollection<Part> parts, string path, DateTime fromDate, DateTime toDate)
        {
            var wb = new XLWorkbook();
            var wsTotal = wb.AddWorksheet("Общий");
            ConfigureWorksheetStyles(wsTotal);
            var serialParts = libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!).Result;
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
                {"factSumTotal", (10, $"Время фактическое(общее)") },
                {"isSerial", (11, $"Серийная{Environment.NewLine}по списку") },
            };

            ConfigureWorksheetHeader(wsTotal, columns);

            var tempParts = parts.Where(p => !p.ExcludeFromReports).Select(part => new Part(part) { PartName = part.PartName.ToUpper().Trim('"') }).ToList();

            var machinesGroup = tempParts.GroupBy(p => p.Machine);

            FillTotalMachinesWorksheetData(wsTotal, machinesGroup, columns, tempParts, serialParts);

            foreach (var ws in wb.Worksheets.Skip(1))
            {
                ConfigureWorksheetHeader(ws, columns);
                FillMachineWorksheetData(ws, tempParts.Where(p => p.Machine == ws.Name), columns);
            }

            wb.SaveAndOfferOpen(path);
            return path;
        }

        private static void FillTotalMachinesWorksheetData(IXLWorksheet ws, IEnumerable<IGrouping<string, Part>> machinesGroup, Dictionary<string, (int index, string header)> columns, List<Part> tempParts, List<SerialPart> serialParts)
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
                    ws.Cell(row, columns["isSerial"].index).SetValue(serialParts.Select(sp => sp.PartName.NormalizedPartNameWithoutComments()).Contains(pg.Key.NormalizedPartNameWithoutComments()));
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
    }
}
