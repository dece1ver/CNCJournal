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
using libeLog.Views;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        public static string ExportShiftsInfo(ICollection<Part> parts, string path, DateTime fromDate, DateTime toDate)
        {
            var wb = new XLWorkbook();
            var wsTotal = wb.AddWorksheet("Общий");
            ConfigureWorksheetStyles(wsTotal);

            var machines = parts.Where(p => !p.ExcludeFromReports).Select(p => p.Machine).Distinct().OrderBy(m => m).ToArray();

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

            var shiftsResult = Database.GetShiftsByPeriod(machines, fromDate, toDate, new Shift(ShiftType.All));
            switch (shiftsResult.Status)
            {
                case remeLog.Core.Db.DbResult.AuthError:
                    MessageBoxWindow.Show("Ошибка авторизации при получении данных о сменах.");
                    return "";
                case remeLog.Core.Db.DbResult.Error:
                    MessageBoxWindow.Show("Ошибка при получении данных о сменах.");
                    return "";
                case remeLog.Core.Db.DbResult.NoConnection:
                    MessageBoxWindow.Show("Нет соединения с базой данных.");
                    return "";
            }
            var shifts = shiftsResult.Value;
            int row = 3;
            for (DateTime dt = fromDate.Date; dt <= toDate.Date; dt = dt.AddDays(1))
            {
                foreach (var machine in machines)
                {
                    var dayShiftInfo = shifts.Find(s => s.ShiftDate == dt && s.Machine == machine && s.Shift == Shifts.Day);
                    var dayParts = parts.Where(p => !p.ExcludeFromReports && p.ShiftDate == dt && p.Machine == machine && p.Shift == Shifts.Day).ToList();
                    var nightShiftInfo = shifts.Find(s => s.ShiftDate == dt && s.Machine == machine && s.Shift == Shifts.Night);
                    var nightParts = parts.Where(p => !p.ExcludeFromReports && p.ShiftDate == dt && p.Machine == machine && p.Shift == Shifts.Night).ToList();

                    FillShiftRow(wsTotal, columns, row, dt, ShiftType.Day, machine, dayShiftInfo, dayParts);
                    FillShiftRow(wsTotal, columns, row + 1, dt, ShiftType.Night, machine, nightShiftInfo, nightParts);

                    wsTotal.Range(row, columns["date"].index, row + 1, columns["date"].index).Merge();
                    wsTotal.Range(row, columns["machine"].index, row + 1, columns["machine"].index).Merge();

                    row += 2;
                }
            }

            if (row > 3)
            {
                var usedRange = wsTotal.RangeUsed();
                usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                usedRange.SetAutoFilter(true);
            }
            wsTotal.Columns().AdjustToContents();
            wsTotal.Column(columns["comment"].index).Width = 35;
            wsTotal.SheetView.FreezeRows(2);

            wb.SaveAndOfferOpen(path);
            return $"Файл сохранен в \"{path}\"";
        }

        /// <summary>
        /// Заполняет строку одной смены (день/ночь) на листе отчёта. Мастер, комментарий и
        /// отметка проверки берутся из cnc_shifts (<paramref name="shiftInfo"/> — null, если
        /// смена туда ещё не записана); остальное считается по деталям этой смены.
        /// </summary>
        private static void FillShiftRow(IXLWorksheet ws, Dictionary<string, (int index, string header)> columns,
            int row, DateTime date, ShiftType shiftType, string machine, ShiftInfo? shiftInfo, List<Part> shiftParts)
        {
            var shift = new Shift(shiftType);

            ws.Cell(row, columns["date"].index).SetValue(date).Style.DateFormat.Format = "dd.MM.yy";
            ws.Cell(row, columns["type"].index).SetValue(shift.Name);
            ws.Cell(row, columns["machine"].index).SetValue(machine);
            ws.Cell(row, columns["master"].index).SetValue(shiftInfo != null ? shiftInfo.Master : "Н/Д");

            ws.Cell(row, columns["ordersCnt"].index).SetValue(shiftParts.Select(p => p.Order).Distinct().Count());
            ws.Cell(row, columns["partsCnt"].index).SetValue(shiftParts.Sum(p => p.FinishedCount));

            var planSum = shiftParts.SetupTimePlanForReport()
                + shiftParts.Where(p => p.ProductionTimePlanForCalc > 0).Sum(p => p.FinishedCountFact * p.ProductionTimePlanForCalc);
            ws.Cell(row, columns["planSum"].index).SetValue(Math.Round(planSum, 1));

            var factSum = shiftParts.TotalSetupTime().TotalMinutes + shiftParts.TotalProductionTime().TotalMinutes;
            ws.Cell(row, columns["factSum"].index).SetValue(Math.Round(factSum, 1));

            var specDowntimes = shiftParts.TotalDowntimesTime().TotalMinutes;
            ws.Cell(row, columns["specDowntimes"].index).SetValue(Math.Round(specDowntimes, 1));

            var unspecDowntimes = shiftParts.UnspecifiedDowntimes(date, date, shiftType);
            ws.Cell(row, columns["unspecDowntimes"].index).SetValue(Math.Round(unspecDowntimes, 1));

            ws.Cell(row, columns["comment"].index)
                .SetValue(shiftInfo?.CommonComment ?? "")
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Cell(row, columns["isChecked"].index).SetValue(shiftInfo?.IsChecked ?? false);
        }
    }
}
