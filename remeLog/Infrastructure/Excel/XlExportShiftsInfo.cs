using ClosedXML.Excel;
using libeLog;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Excel;
using libeLog.Models;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
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

            var machines = parts.Where(p => !p.ExcludeFromReports).Select(p => p.Machine).Distinct().ToArray();

            //foreach (var machine in machines)
            //{
            //    wsTotal.CopyTo(machine);
            //}

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
                case libeLog.Models.DbResult.AuthError:
                    MessageBox.Show("AuthError");
                    return "";
                case libeLog.Models.DbResult.Error:
                    MessageBox.Show("Error");
                    return "";
                case libeLog.Models.DbResult.NoConnection:
                    MessageBox.Show("NoConnection");
                    return "";
            }
            var shifts = shiftsResult.Value.OrderBy(s => s.Machine).ToList();
            int row = 3;
            for (DateTime dt = fromDate; dt <= toDate; dt += TimeSpan.FromDays(1))
            {
                foreach (var machine in machines)
                {
                    var dayShiftInfo = shifts.Find(s => s.ShiftDate == dt && s.Machine == machine && s.Shift == "День");
                    var dayParts = parts.Where(p => p.ShiftDate == dt && p.Machine == machine && p.Shift == "День");
                    var nightShiftInfo = shifts.Find(s => s.ShiftDate == dt && s.Machine == machine && s.Shift == "Ночь");
                    var nightParts = parts.Where(p => p.ShiftDate == dt && p.Machine == machine && p.Shift == "Ночь");
                    wsTotal.Cell(row, columns["date"].index).Value = dt;
                    wsTotal.Range(row, columns["date"].index, row + 1, columns["date"].index).Merge();

                    wsTotal.Cell(row, columns["type"].index).Value = "День";
                    wsTotal.Cell(row + 1, columns["type"].index).Value = "Ночь";

                    wsTotal.Cell(row, columns["machine"].index).Value = machine;
                    wsTotal.Range(row, columns["machine"].index, row + 1, columns["machine"].index).Merge();

                    wsTotal.Cell(row, columns["master"].index).Value = dayShiftInfo != null ? dayShiftInfo.Master : "Н/Д";
                    wsTotal.Range(row, columns["master"].index, row + 1, columns["master"].index).Merge();

                    wsTotal.Cell(row, columns["ordersCnt"].index).Value = dayParts
                        .Select(p => p.Order)
                        .Distinct()
                        .Count();
                    wsTotal.Cell(row + 1, columns["ordersCnt"].index).Value = nightParts
                        .Select(p => p.Order)
                        .Distinct()
                        .Count();

                    row += 2;
                }
            }

            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
