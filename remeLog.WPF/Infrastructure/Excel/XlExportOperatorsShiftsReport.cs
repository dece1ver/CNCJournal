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
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        /// <summary>
        /// Экспортирует отчет по сменам операторов в Excel.
        /// </summary>
        /// <param name="parts">Коллекция деталей, содержащих данные для отчета.</param>
        /// <param name="fromDate">Начальная дата периода для отчета.</param>
        /// <param name="toDate">Конечная дата периода для отчета.</param>
        /// <param name="path">Путь для сохранения отчета в виде файла Excel.</param>
        /// <returns>Строка сообщения, указывающая путь и успешность выполнения.</returns>
        /// <remarks>
        /// В отчете выводится количество смен для каждого оператора за указанный период. 
        /// Отчет формируется в виде Excel-файла с заголовками и форматированием.
        /// Если оператором указан "Ученик", то данные по нему не включаются в отчет.
        /// После успешного сохранения файла пользователю предлагается открыть его.
        /// </remarks>
        public static string ExportOperatorsShiftsReport(ICollection<Part> parts, DateTime fromDate, DateTime toDate, string path)
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Отчет по операторам");
            var columns = new Dictionary<string, (int index, string header)>
            {
                { "operator", (1, "Оператор") },
                { "shifts", (2, "Смены") },
            };
            var lastCol = columns.Count - 1;
            ConfigureWorksheetHeader(ws, columns, HeaderRotateOption.Horizontal);

            var row = 3;

            foreach (var partGroup in parts
                .Where(p => p.Operator != "Ученик")
                .GroupBy(p => p.Operator)
                .OrderBy(pg => pg.Key))
            {
                var distinctShiftsCount = partGroup.Select(pg => pg.ShiftDate).Distinct().Count();

                ws.Cell(row, columns["operator"].index)
                  .SetValue(partGroup.Key);

                ws.Cell(row, columns["shifts"].index)
                  .SetValue(distinctShiftsCount)
                  .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                row++;
            }
            var usedRange = ws.RangeUsed();
            usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            usedRange.SetAutoFilter(true);
            ws.Columns().AdjustToContents();
            ws.Cell(1, 1).Value = $"Отчёт по операторам за период с {fromDate.ToString(Constants.ShortDateFormat)} по {toDate.ToString(Constants.ShortDateFormat)}";
            wb.SaveAndOfferOpen(path);
            return $"Файл сохранен в \"{path}\"";
        }
    }
}
