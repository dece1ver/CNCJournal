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
using System.Threading.Tasks;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        public static async Task<string> ExportToolSearchCasesAsync(ICollection<Part> parts, string path, IProgress<string> progress)
        {
            progress.Report("Создание книги");
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet($"Поиски истины");
            progress.Report("Получение данных из БД");
            var cases = await Database.GetToolSearchCasesAsync(parts.Select(p => p.Guid).ToList(), progress);


            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            progress.Report("Формирование столбцов");

            var cm = new CM.Builder()
                .Add(CM.Operator)
                .Add(CM.Type)
                .Add(CM.Description)
                .Add(CM.StartMachiningTime, "Начало")
                .Add(CM.EndMachiningTime, "Завершение")
                .Add(CM.ToolSearchingTime, "Затраченное время")
                .Add(CM.Part)
                .Add(CM.Machine)
                .Add(CM.IsSuccess, "Нашёл?")
                .Build();

            ConfigureWorksheetHeader(ws, cm, HeaderRotateOption.Horizontal, 65, 10);

            ws.Range(2, 1, 2, cm.Count).Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);

            int row = 3;

            var ci = cm.GetIndexes();


            foreach (var @case in cases)
            {
                ws.Cell(row, ci[CM.Operator]).SetValue(@case.Operator);
                ws.Cell(row, ci[CM.Part]).SetValue(@case.Part);
                ws.Cell(row, ci[CM.Machine]).SetValue(@case.Machine);
                ws.Cell(row, ci[CM.Type]).SetValue(@case.Type);
                ws.Cell(row, ci[CM.Description]).SetValue(@case.Description);
                ws.Cell(row, ci[CM.StartMachiningTime]).SetValue(@case.StartTime)
                    .Style.DateFormat.Format = "dd.MM.yy HH:mm";
                ws.Cell(row, ci[CM.EndMachiningTime]).SetValue(@case.EndTime)
                    .Style.DateFormat.Format = "dd.MM.yy HH:mm";
                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(@case.Time);
                ws.Cell(row, ci[CM.IsSuccess]).SetValue(@case.IsSuccess.HasValue ? @case.IsSuccess.Value ? "Да" : "Нет" : "Н/Д");

                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(ci[CM.Type]).Width = 25;
            ws.Column(ci[CM.Description]).Width = 60;
            ws.Column(ci[CM.Part]).Width = 60;
            ws.Column(ci[CM.StartMachiningTime]).Width = 12;
            ws.Column(ci[CM.EndMachiningTime]).Width = 12;
            ws.Column(ci[CM.ToolSearchingTime]).Width = 12;
            ws.Column(ci[CM.IsSuccess]).Width = 8;
            ws.Column(ci[CM.IsSuccess]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(2, ci[CM.StartMachiningTime], row, ci[CM.ToolSearchingTime]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(2, 1, row - 1, ci.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(2, 1, row - 1, cm.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            SetTitle(ws, ci.Count, "Поиски инструмента");
            progress.Report("Сохранение файла");
            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
