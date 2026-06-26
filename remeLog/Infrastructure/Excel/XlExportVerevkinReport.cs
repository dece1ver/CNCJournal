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

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        public static string ExportVerevkinReport(IEnumerable<VerevkinPart> parts, string path, IProgress<string> progress)
        {
            var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();

            var columns = new Dictionary<string, int>
            {
                {"no", 1 },
                {"part", 2 },
                {"fact1", 4 },
                {"fact2", 5 },
                {"fact3", 6 },
                {"fact4", 7 },
                {"fact5", 8 },
                {"factSum", 9 },
                {"factTurn1prev", 10 },
                {"factTurn2prev", 11 },
                {"factTurn3prev", 12 },
                {"factSumPrev", 13 },
                {"factMill1prev", 14 },
                {"factMill2prev", 15 },
                {"factMill3prev", 16 },
                {"factMill4prev", 17 },
                {"factSumMillPrev", 18 },
                {"factSum2", 19 },
                {"factSumPrev2", 20 },
                {"effectTime", 21 },
                {"effectPercent", 22 },
                {"order", 23 },
                {"count", 24 },
            };
            progress.Report("Чтение содержимого");

            int rowIndex = 0;

            foreach (var row in ws.Rows().Skip(2))
            {
                rowIndex++;
                if (!row.Cell(2).IsEmpty() || !row.Cell(2).Value.IsBlank) continue;
                break;
            }
            rowIndex += 2;
            progress.Report("Добавление деталей");
            foreach (var part in parts)
            {
                ws.Row(rowIndex).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(rowIndex).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIndex, columns["no"]).SetValue(rowIndex - 2);
                ws.Cell(rowIndex, columns["part"]).SetValue(part.PartName).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Range(rowIndex, columns["part"], rowIndex, columns["part"] + 1).Merge();
                if (part.MachineTime1 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact1"]).SetValue(part.MachineTime1);
                if (part.MachineTime2 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact2"]).SetValue(part.MachineTime2);
                if (part.MachineTime3 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact3"]).SetValue(part.MachineTime3);
                if (part.MachineTime4 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact4"]).SetValue(part.MachineTime4);
                if (part.MachineTime5 != TimeSpan.Zero) ws.Cell(rowIndex, columns[$"fact5"]).SetValue(part.MachineTime5);
                ws.Cell(rowIndex, columns["factSum"]).SetFormulaA1($"SUM(D{rowIndex}:H{rowIndex})");
                ws.Cell(rowIndex, columns["factSumPrev"]).SetFormulaA1($"SUM(J{rowIndex}:L{rowIndex})");
                ws.Cell(rowIndex, columns["factSumMillPrev"]).SetFormulaA1($"SUM(N{rowIndex}:Q{rowIndex})");
                ws.Cell(rowIndex, columns["factSum2"]).SetFormulaA1($"I{rowIndex}");
                ws.Cell(rowIndex, columns["factSumPrev2"]).SetFormulaA1($"M{rowIndex}+R{rowIndex}");
                ws.Cell(rowIndex, columns["effectTime"]).SetFormulaA1(
                    $"IF(ISNUMBER(T{rowIndex})*AND(T{rowIndex}>0), IF(T{rowIndex} - S{rowIndex} < 0, \"-\" " +
                    $"& TEXT(ABS(T{rowIndex} - S{rowIndex}), \"Ч:ММ:СС\"), TEXT(T{rowIndex} - S{rowIndex}, \"Ч:ММ:СС\")), T{rowIndex})");

                ws.Cell(rowIndex, columns["effectPercent"]).SetFormulaA1($"IFERROR(T{rowIndex}/S{rowIndex},T{rowIndex})")
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
                ws.Range(rowIndex, columns["fact1"], rowIndex, columns["effectTime"]).Style.NumberFormat.SetFormat("h:mm:ss");
                ws.Cell(rowIndex, columns["order"]).SetValue(part.Order);
                ws.Cell(rowIndex, columns["count"]).SetValue(part.FinishedCount);
                rowIndex++;
            }

            progress.Report("Сохранение");
            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
