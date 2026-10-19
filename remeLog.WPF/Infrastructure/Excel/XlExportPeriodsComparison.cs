using ClosedXML.Excel;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure.Excel;
using remeLog.Infrastructure.Extensions;
using remeLog.Models;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        /// <summary>
        /// Отчёт "Сравнение периодов": лист-таблица по станкам за первый период, такой же лист
        /// за второй период, и лист сравнительной сводки (см. <see cref="WriteSummaryBlock"/>)
        /// с условным форматированием, показывающим соотношение показателей между периодами.
        /// Данные за оба периода загружаются из БД самостоятельно — не зависят от текущей
        /// выборки в окне.
        /// </summary>
        public static async Task<string> ExportPeriodsComparisonAsync(
            DateTime fromDate1, DateTime toDate1, string label1,
            DateTime fromDate2, DateTime toDate2, string label2,
            Shift shift, string path, IProgress<string> progress)
        {
            progress?.Report("Начало экспорта...");

            progress?.Report("Получение списка серийных деталей...");
            var serialParts = await libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!);
            var serialPartNames = serialParts.Select(p => p.PartName.NormalizedPartNameWithoutComments()).ToImmutableHashSet();

            progress?.Report($"Загрузка данных за период «{label1}»...");
            var parts1 = await Database.ReadPartsByShiftDate(fromDate1, toDate1, CancellationToken.None);

            progress?.Report($"Загрузка данных за период «{label2}»...");
            var parts2 = await Database.ReadPartsByShiftDate(fromDate2, toDate2, CancellationToken.None);

            var wb = new XLWorkbook();

            var period1 = BuildPeriodDataSheet(wb, label1, parts1, fromDate1, toDate1, shift, serialPartNames, progress);
            var period2 = BuildPeriodDataSheet(wb, label2, parts2, fromDate2, toDate2, shift, serialPartNames, progress);

            progress?.Report("Формирование сравнительной сводки...");
            var summaryWs = CreateSummaryWorksheet(wb, "Сравнение");
            var block1Last = WriteSummaryBlock(summaryWs, 1, period1.Title, period1.Ws, period1.Ci, period1.TotalRow);
            var block2Last = WriteSummaryBlock(summaryWs, block1Last + 2, period2.Title, period2.Ws, period2.Ci, period2.TotalRow);

            // Каждый блок — 3 строки данных (Общее/Серийная/Не серийная), заканчивается на *Last.
            ApplyComparisonFormatting(summaryWs, block1Last - 2, block2Last - 2);

            progress?.Report("Формирование завершено, сохранение файла...");
            wb.SaveAndOfferOpen(path);
            return $"Файл сохранен в \"{path}\"";
        }

        /// <summary>
        /// Условное форматирование только на процентных колонках (Наладка/Изготовление/Простои,
        /// B:D) — часы (E:H) не трогаем. На каждую ячейку блока периода 2 — свой набор иконок,
        /// сравнивающий её значение с той же метрикой периода 1 (той же строкой того же блока):
        /// "Общая наладка" периода 2 против "Общей наладки" периода 1 и т.д. — не с процентилем
        /// диапазона, а именно ячейка к ячейке через формулу-порог, как в исходном файле.
        /// Для "меньше — лучше" (Наладка, Простои) порядок иконок реверсирован, чтобы более
        /// низкое (по сравнению с периодом 1) значение получало "хорошую" стрелку; для
        /// "больше — лучше" (Изготовление) — обычный порядок.
        /// </summary>
        private static void ApplyComparisonFormatting(IXLWorksheet ws, int block1First, int block2First)
        {
            for (int offset = 0; offset <= 2; offset++)
            {
                int row1 = block1First + offset;
                int row2 = block2First + offset;
                ApplyCellIconSet(ws, 2, row2, row1, reverse: true);  // Наладка: меньше — лучше
                ApplyCellIconSet(ws, 3, row2, row1, reverse: false); // Изготовление: больше — лучше
                ApplyCellIconSet(ws, 4, row2, row1, reverse: true);  // Отмеченные простои: меньше — лучше
            }
        }

        /// <summary>
        /// Иконка в ячейке (<paramref name="targetRow"/>, <paramref name="col"/>) сравнивает её
        /// значение с ячейкой-эталоном (<paramref name="referenceRow"/>, тот же столбец):
        /// ниже эталона — одна стрелка, выше — другая (порядок задаёт <paramref name="reverse"/>).
        /// .IconSet(...) сам по себе не пишет пороги (&lt;cfvo&gt;) — без явных AddValue получается
        /// &lt;iconSet&gt; без единого &lt;cfvo&gt;, это невалидно по схеме OOXML, и Excel требует
        /// восстановления файла.
        /// </summary>
        private static void ApplyCellIconSet(IXLWorksheet ws, int col, int targetRow, int referenceRow, bool reverse)
        {
            // Обязательно абсолютный адрес ($B$3, не B3) — как в исходном файле. Без "$" порог
            // сравнения у некоторых ячеек в наблюдаемом файле вёл себя так, будто вообще не
            // реагирует на реальное значение (иконка одна и та же для всей колонки).
            var referenceAddress = ws.Cell(referenceRow, col).Address.ToStringFixed();
            ws.Range(targetRow, col, targetRow, col).AddConditionalFormat()
                .IconSet(XLIconSetStyle.ThreeArrows, reverseIconOrder: reverse)
                .AddValue(XLCFIconSetOperator.EqualOrGreaterThan, 0, XLCFContentType.Percent)
                .AddValue(XLCFIconSetOperator.EqualOrGreaterThan, referenceAddress, XLCFContentType.Formula)
                .AddValue(XLCFIconSetOperator.GreaterThan, referenceAddress, XLCFContentType.Formula);
        }
    }
}
