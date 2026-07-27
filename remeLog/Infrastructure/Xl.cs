using ClosedXML.Excel;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using remeLog.Models.Reports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        private static readonly XLColor _lightRed = XLColor.FromHtml("#DA9694");
        private static readonly XLColor _lightGreen = XLColor.FromHtml("#96DA94");
        const double MinimumIncludedTimeRatio = 0.3;

        /// <summary>
        /// Типы экспорта отчетов операторов.
        /// </summary>
        public enum ExportOperatorReportType
        {
            /// <summary>
            /// Отчет по выполнению норм, где операторы выполнили план ниже нормы.
            /// </summary>
            Under,

            /// <summary>
            /// Отчет по выполнению норм, где операторы выполнили план значительно ниже нормы.
            /// (например, на уровне критической точки).
            /// </summary>
            Below
        }

        /// <summary>
        /// Варианты ориентации заголовков.
        /// </summary>
        public enum HeaderRotateOption
        {
            /// <summary>
            /// Горизонтальная ориентация заголовков.
            /// </summary>
            Horizontal,

            /// <summary>
            /// Вертикальная ориентация заголовков.
            /// </summary>
            Vertical
        }


        /// <summary>
        /// Перечисление, которое определяет полужирность для левого и правого заголовков.
        /// </summary>
        public enum BoldOption
        {
            /// <summary>
            /// Левый заголовок выделен полужирным, правый — нет.
            /// </summary>
            Left,

            /// <summary>
            /// Правый заголовок выделен полужирным, левый — нет.
            /// </summary>
            Right,

            /// <summary>
            /// Оба заголовка выделены полужирным.
            /// </summary>
            Both
        }



        private static void ConfigureMachineSheetForPeriod(XLWorkbook wb, IEnumerable<Part> parts, string machine, CM cm, ImmutableHashSet<string>? serialParts = null )
        {
            var ws = wb.AddWorksheet(machine);
            ConfigureWorksheetHeader(ws, cm);
            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            var ci = cm.GetIndexes();
            int row = 3;

            foreach (var part in parts.Where(p => p.Machine == machine))
            {
                ws.Cell(row, ci[CM.Date])
                    .SetValue(part.ShiftDate)
                    .Style.DateFormat.Format = "dd.MM.yy";
                ws.Cell(row, ci[CM.Shift]).SetValue(part.Shift);
                ws.Cell(row, ci[CM.Operator]).SetValue(part.Operator);
                ws.Cell(row, ci[CM.Part]).SetValue(part.PartName);
                if (serialParts != null) 
                    ws.Cell(row, ci[CM.SerialPerList])
                        .SetValue(serialParts.Contains(part.PartName.NormalizedPartNameWithoutComments()));
                ws.Cell(row, ci[CM.Order]).SetValue(part.Order);
                ws.Cell(row, ci[CM.TotalByOrder]).SetValue(part.TotalCount);
                ws.Cell(row, ci[CM.Finished]).SetValue(part.FinishedCount);
                ws.Cell(row, ci[CM.Setup]).SetValue(part.Setup);
                ws.Cell(row, ci[CM.StartSetupTime])
                    .SetValue(part.StartSetupTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.StartMachiningTime])
                    .SetValue(part.StartMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.EndMachiningTime])
                    .SetValue(part.EndMachiningTime)
                    .Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, ci[CM.SetupTimePlan]).SetValue(part.SetupTimePlan);
                ws.Cell(row, ci[CM.SetupTimeFact]).SetValue(part.SetupTimeFact);
                ws.Cell(row, ci[CM.SingleProductionTimePlan]).SetValue(part.SingleProductionTimePlan);
                ws.Cell(row, ci[CM.MachiningTime]).SetValue(part.MachiningTime);
                if (part.SingleProductionTime is double spt && spt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SingleProductionTime])
                        .SetValue(spt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                if (part.PartReplacementTime is double prt && prt is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.PartReplacementTime])
                        .SetValue(prt)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                ws.Cell(row, ci[CM.ProductionTimeFact]).SetValue(part.ProductionTimeFact);
                ws.Cell(row, ci[CM.PlanForBatch]).SetValue(part.PlanForBatch);
                ws.Cell(row, ci[CM.OperatorComment])
                    .SetValue(part.OperatorComment)
                    .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, ci[CM.SetupDowntimes]).SetValue(part.SetupDowntimes);
                ws.Cell(row, ci[CM.MachiningDowntimes]).SetValue(part.MachiningDowntimes);
                ws.Cell(row, ci[CM.PartialSetupTime]).SetValue(part.PartialSetupTime);
                ws.Cell(row, ci[CM.CreateNcProgramTime]).SetValue(part.CreateNcProgramTime);
                ws.Cell(row, ci[CM.MaintenanceTime]).SetValue(part.MaintenanceTime);
                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(part.ToolSearchingTime);
                ws.Cell(row, ci[CM.MentoringTime]).SetValue(part.MentoringTime);
                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).SetValue(part.ContactingDepartmentsTime);
                ws.Cell(row, ci[CM.FixtureMakingTime]).SetValue(part.FixtureMakingTime);
                ws.Cell(row, ci[CM.HardwareFailureTime]).SetValue(part.HardwareFailureTime);
                if (part.SpecifiedDowntimesRatio is not (double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                    ws.Cell(row, ci[CM.SpecifiedDowntimesRatio])
                        .SetValue(part.SpecifiedDowntimesRatio)
                        .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;
                ws.Cell(row, ci[CM.SpecifiedDowntimesComment]).SetValue(part.SpecifiedDowntimesComment);
                ws.Cell(row, ci[CM.SetupRatioTitle]).SetValue(part.SetupRatioTitle);
                // Итоговая классификация: переопределение СГТ, если оно есть, иначе отметка мастера.
                ws.Cell(row, ci[CM.MasterSetupComment]).SetValue(part.EffectiveSetupReason);
                ws.Cell(row, ci[CM.MasterSetupDetail]).SetValue(part.MasterSetupDetail);
                ws.Cell(row, ci[CM.ProductionRatioTitle]).SetValue(part.ProductionRatioTitle);
                ws.Cell(row, ci[CM.MasterProductionComment]).SetValue(part.EffectiveMachiningReason);
                ws.Cell(row, ci[CM.MasterMachiningDetail]).SetValue(part.MasterMachiningDetail);
                ws.Cell(row, ci[CM.MasterComment]).SetValue(part.MasterComment);
                ws.Cell(row, ci[CM.FixedSetupTimePlan]).SetValue(part.FixedSetupTimePlan);
                ws.Cell(row, ci[CM.FixedProductionTimePlan]).SetValue(part.FixedProductionTimePlan);
                ws.Cell(row, ci[CM.EngineerConclusion]).SetValue(part.EngineerConclusion);
                row++;
            }
            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Columns().AdjustToContents();


            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            ws.RangeUsed().SetAutoFilter(true);
            ws.Columns().AdjustToContents();

            ws.Column(ci[CM.Operator]).Width = 15;

            ws.Column(ci[CM.Part]).Width = 25;

            ws.Range(3, ci[CM.OperatorComment], row, ci[CM.OperatorComment])
               .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Column(ci[CM.OperatorComment]).Width = 35;

            ws.Column(ci[CM.MasterSetupComment]).Width = 20;
            ws.Column(ci[CM.MasterProductionComment]).Width = 20;
            ws.Column(ci[CM.MasterComment]).Width = 20;
            ws.Row(1).Delete();
            ws.SheetView.FreezeRows(1);
        }

        /// <summary>
        /// Создает лист с коэффициентами для всех квалификаций
        /// </summary>
        private static void CreateCoefficientsWorksheet(IXLWorksheet ws, IEnumerable<Qualification> qualifications)
        {
            // Заголовки столбцов
            ws.Cell("A1").SetValue("Квалификация");

            // Эффективность - пороги
            ws.Cell("B1").SetValue("EffHH_Value");
            ws.Cell("C1").SetValue("EffH_Value");
            ws.Cell("D1").SetValue("EffN_Value");
            ws.Cell("E1").SetValue("EffL_Value");
            ws.Cell("F1").SetValue("EffLL_Value");
            ws.Cell("G1").SetValue("EffLLL_Value");

            // Эффективность - коэффициенты
            ws.Cell("H1").SetValue("EffHH_Coeff");
            ws.Cell("I1").SetValue("EffH_Coeff");
            ws.Cell("J1").SetValue("EffN_Coeff");
            ws.Cell("K1").SetValue("EffL_Coeff");
            ws.Cell("L1").SetValue("EffLL_Coeff");
            ws.Cell("M1").SetValue("EffLLL_Coeff");

            // Простои - пороги
            ws.Cell("N1").SetValue("DownHH_Value");
            ws.Cell("O1").SetValue("DownH_Value");
            ws.Cell("P1").SetValue("DownN_Value");
            ws.Cell("Q1").SetValue("DownL_Value");
            ws.Cell("R1").SetValue("DownLL_Value");
            ws.Cell("S1").SetValue("DownLLL_Value");

            // Простои - коэффициенты
            ws.Cell("T1").SetValue("DownHH_Coeff");
            ws.Cell("U1").SetValue("DownH_Coeff");
            ws.Cell("V1").SetValue("DownN_Coeff");
            ws.Cell("W1").SetValue("DownL_Coeff");
            ws.Cell("X1").SetValue("DownLL_Coeff");
            ws.Cell("Y1").SetValue("DownLLL_Coeff");

            // Эффективность - пороги (не серийные)
            ws.Cell("Z1").SetValue("NEffHH_Value");
            ws.Cell("AA1").SetValue("NEffH_Value");
            ws.Cell("AB1").SetValue("NEffN_Value");
            ws.Cell("AC1").SetValue("NEffL_Value");
            ws.Cell("AD1").SetValue("NEffLL_Value");
            ws.Cell("AE1").SetValue("NEffLLL_Value");

            // Эффективность - коэффициенты (не серийные)
            ws.Cell("AF1").SetValue("NEffHH_Coeff");
            ws.Cell("AG1").SetValue("NEffH_Coeff");
            ws.Cell("AH1").SetValue("NEffN_Coeff");
            ws.Cell("AI1").SetValue("NEffL_Coeff");
            ws.Cell("AJ1").SetValue("NEffLL_Coeff");
            ws.Cell("AK1").SetValue("NEffLLL_Coeff");

            // Заполняем данные
            int row = 2;
            foreach (var qual in qualifications.OrderBy(q => q.Value))
            {
                ws.Cell(row, 1).SetValue(qual.Value); // Квалификация

                // Пороги эффективности
                ws.Cell(row, 2).SetValue(qual.EfficiencyValueHH);
                ws.Cell(row, 3).SetValue(qual.EfficiencyValueH);
                ws.Cell(row, 4).SetValue(qual.EfficiencyValueN);
                ws.Cell(row, 5).SetValue(qual.EfficiencyValueL);
                ws.Cell(row, 6).SetValue(qual.EfficiencyValueLL);
                ws.Cell(row, 7).SetValue(qual.EfficiencyValueLLL);

                // Коэффициенты эффективности
                ws.Cell(row, 8).SetValue(qual.EfficiencyCoefficientHH);
                ws.Cell(row, 9).SetValue(qual.EfficiencyCoefficientH);
                ws.Cell(row, 10).SetValue(qual.EfficiencyCoefficientN);
                ws.Cell(row, 11).SetValue(qual.EfficiencyCoefficientL);
                ws.Cell(row, 12).SetValue(qual.EfficiencyCoefficientLL);
                ws.Cell(row, 13).SetValue(qual.EfficiencyCoefficientLLL);

                // Пороги простоев
                ws.Cell(row, 14).SetValue(qual.DownTimesValueHH);
                ws.Cell(row, 15).SetValue(qual.DownTimesValueH);
                ws.Cell(row, 16).SetValue(qual.DownTimesValueN);
                ws.Cell(row, 17).SetValue(qual.DownTimesValueL);
                ws.Cell(row, 18).SetValue(qual.DownTimesValueLL);
                ws.Cell(row, 19).SetValue(qual.DownTimesValueLLL);

                // Коэффициенты простоев
                ws.Cell(row, 20).SetValue(qual.DownTimesCoefficientHH);
                ws.Cell(row, 21).SetValue(qual.DownTimesCoefficientH);
                ws.Cell(row, 22).SetValue(qual.DownTimesCoefficientN);
                ws.Cell(row, 23).SetValue(qual.DownTimesCoefficientL);
                ws.Cell(row, 24).SetValue(qual.DownTimesCoefficientLL);
                ws.Cell(row, 25).SetValue(qual.DownTimesCoefficientLLL);

                // Пороги эффективности (не серийные)
                ws.Cell(row, 26).SetValue(qual.NonSerialEfficiencyValueHH);
                ws.Cell(row, 27).SetValue(qual.NonSerialEfficiencyValueH);
                ws.Cell(row, 28).SetValue(qual.NonSerialEfficiencyValueN);
                ws.Cell(row, 29).SetValue(qual.NonSerialEfficiencyValueL);
                ws.Cell(row, 30).SetValue(qual.NonSerialEfficiencyValueLL);
                ws.Cell(row, 31).SetValue(qual.NonSerialEfficiencyValueLLL);

                // Коэффициенты эффективности (не серийные)
                ws.Cell(row, 32).SetValue(qual.NonSerialEfficiencyCoefficientHH);
                ws.Cell(row, 33).SetValue(qual.NonSerialEfficiencyCoefficientH);
                ws.Cell(row, 34).SetValue(qual.NonSerialEfficiencyCoefficientN);
                ws.Cell(row, 35).SetValue(qual.NonSerialEfficiencyCoefficientL);
                ws.Cell(row, 36).SetValue(qual.NonSerialEfficiencyCoefficientLL);
                ws.Cell(row, 37).SetValue(qual.NonSerialEfficiencyCoefficientLLL);
                row++;
            }

            // Форматируем заголовки
            var headerRange = ws.Range("A1:Y1");
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        }







        //public static string GetOperatorQualification(this string operatorName)
        //{
        //    if (!File.Exists(AppSettings.Instance.QualificationSourcePath)) return "Н/Д";
        //    try
        //    {
        //        using (var fs = new FileStream(AppSettings.Instance.QualificationSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        //        {
        //            var wb = new XLWorkbook(fs);
        //            foreach (var xlRow in wb.Worksheet(1).Rows().Skip(2))
        //            {
        //                if (xlRow.Cell(2).Value.IsText && xlRow.Cell(2).Value.GetText() == operatorName)
        //                {
        //                    if (xlRow.Cell(4).Value.IsText) return xlRow.Cell(4).Value.GetText();
        //                    else if (xlRow.Cell(4).Value.IsNumber) return xlRow.Cell(4).Value.GetNumber().ToString();
        //                }
        //            }
        //            return "Н/Д";
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Util.WriteLog(ex);
        //        return "Н/Д";
        //    }
        //}

        public static string GetOperatorQualification(this string operatorFullName, IEnumerable<OperatorInfo> operators)
        {
            foreach (var @operator in operators)
            {
                if (@operator.FullName == operatorFullName)
                {
                    return @operator.Qualification.ToString();
                }
            }
            return "Н/Д";
        }

        private static void ConfigureWorksheetStyles(IXLWorksheet ws)
        {
            ws.Style.Font.FontSize = 12;
            ws.Style.Alignment.WrapText = true;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        /// <summary>
        /// Настраивает заголовки на рабочем листе Excel на основе словаря колонок.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="columns">Словарь колонок, где ключ — идентификатор, значение — пара (индекс, заголовок).</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeader(IXLWorksheet ws, Dictionary<string, (int index, string header)> columns, HeaderRotateOption headerRotateOption = HeaderRotateOption.Vertical, int height = 90, int fontSize = 10)
        {
            ConfigureWorksheetHeaderInternal(ws, columns.Select(c => (c.Value.index, c.Value.header)).ToList(), headerRotateOption, height, fontSize);
        }

        /// <summary>
        /// Настраивает заголовки на рабочем листе Excel на основе объекта CM.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="cm">Объект CM, содержащий информацию о колонках.</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeader(IXLWorksheet ws, CM cm, HeaderRotateOption headerRotateOption = HeaderRotateOption.Vertical, int height = 90, int fontSize = 10)
        {
            ConfigureWorksheetHeaderInternal(ws, cm.GetIndexedHeaders().ToList(), headerRotateOption, height, fontSize);
        }

        /// <summary>
        /// Настраивает заголовки на листе Excel.
        /// </summary>
        /// <param name="ws">Рабочий лист Excel.</param>
        /// <param name="columns">Список колонок, где каждая пара состоит из индекса и заголовка.</param>
        /// <param name="headerRotateOption">Опция вращения заголовков (по умолчанию вертикально).</param>
        /// <param name="height">Высота строки заголовков (по умолчанию 90).</param>
        /// <param name="fontSize">Размер шрифта заголовков (по умолчанию 10).</param>
        private static void ConfigureWorksheetHeaderInternal(IXLWorksheet ws, List<(int index, string header)> columns, HeaderRotateOption headerRotateOption, int height, int fontSize)
        {
            foreach (var (index, header) in columns)
            {
                ws.Cell(2, index).Value = header;
            }

            var headerRange = ws.Range(2, 1, 2, columns.Count);
            if (headerRotateOption == HeaderRotateOption.Vertical)
                headerRange.Style.Alignment.TextRotation = 90;

            headerRange.Style.Font.FontName = "Segoe UI Semibold";
            headerRange.Style.Font.FontSize = fontSize;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Alignment.WrapText = true;
            ws.Row(2).Height = height;
        }

        /// <summary>
        /// Устанавливает заголовок в первую строку рабочего листа, растягивая его на указанные столбцы.
        /// </summary>
        /// <param name="ws">Рабочий лист, на котором устанавливается заголовок.</param>
        /// <param name="columnsCount">Количество столбцов, на которые будет растянут заголовок (для объединения ячеек).</param>
        /// <param name="title">Текст заголовка, который будет выведен в первой ячейке.</param>
        /// <param name="fontSize">Размер шрифта для заголовка. По умолчанию 14.</param>
        /// <param name="bold">Указывает, будет ли заголовок полужирным. По умолчанию true.</param>
        /// <param name="merge">Указывает, нужно ли объединять ячейки заголовка на весь диапазон столбцов. По умолчанию true.</param>
        /// <param name="alignment">Выравнивание текста в ячейке. По умолчанию выравнивание по левому краю.</param>
        private static void SetTitle(IXLWorksheet ws, int columnsCount, string title, int fontSize = 14, bool bold = true, bool merge = true, XLAlignmentHorizontalValues alignment = XLAlignmentHorizontalValues.Left)
        {
            var cell = ws.Cell(1, 1).SetValue(title)
                .Style.Font.SetFontSize(fontSize)
                .Font.SetBold(bold)
                .Alignment.SetHorizontal(alignment)
                .Alignment.SetWrapText(false);

            if (merge)
            {
                ws.Range(1, 1, 1, columnsCount).Merge();
            }
        }

        /// <summary>
        /// Устанавливает два заголовка: один слева, другой справа. Каждый заголовок может быть выделен полужирным.
        /// </summary>
        /// <param name="ws">Рабочий лист, на котором устанавливаются заголовки.</param>
        /// <param name="columnsCount">Количество столбцов, на которые будет растянут второй заголовок (справа).</param>
        /// <param name="left">Текст заголовка, который будет выведен слева в первой ячейке.</param>
        /// <param name="right">Текст заголовка, который будет выведен справа в последней ячейке первой строки.</param>
        /// <param name="fontSize">Размер шрифта для обоих заголовков. По умолчанию 14.</param>
        /// <param name="bold">Указывает, будет ли левый и правый заголовок полужирным. 
        /// Определяется с помощью перечисления <see cref="BoldOption">BoldOption</see>. По умолчанию оба заголовка будут полужирными.</param>
        private static void SetTitle(IXLWorksheet ws, int columnsCount, string left, string right, int fontSize = 14, BoldOption bold = BoldOption.Both)
        {
            var leftCell = ws.Cell(1, 1).SetValue(left)
                .Style.Font.SetFontSize(fontSize)
                .Font.SetBold(bold == BoldOption.Left || bold == BoldOption.Both)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                .Alignment.SetWrapText(false);

            var rightCell = ws.Cell(1, columnsCount).SetValue(right)
                .Style.Font.SetFontSize(fontSize)
                .Font.SetBold(bold == BoldOption.Right || bold == BoldOption.Both)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .Alignment.SetWrapText(false);
        }

        static bool IsInvalidRatio(double ratio) =>
            ratio == 0 || double.IsInfinity(ratio) || double.IsNaN(ratio);

        static TimeSpan NotExcludedTime(Part part, bool includeSmallBatch)
        {
            var span = TimeSpan.Zero;
            if (!part.ExcludeFromReports)
            {
                if (part.SetupRatio is not (0 or double.NaN or double.NegativeInfinity or double.PositiveInfinity))
                {
                    span = span.Add(TimeSpan.FromMinutes(part.SetupTimeFact)).Add(TimeSpan.FromMinutes(part.SetupDowntimes));
                }
                if (!IsInvalidRatio(part.ProductionRatio) && (includeSmallBatch || !part.IsSmallBatch))
                {
                    span = span.Add(TimeSpan.FromMinutes(part.ProductionTimeFact)).Add(TimeSpan.FromMinutes(part.MachiningDowntimes));
                }
            }
            return span;
        }
    }
}
