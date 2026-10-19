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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CM = remeLog.Infrastructure.ColumnManager;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        /// <summary>
        /// Экспорт отчета по операторам
        /// </summary>
        /// <param name="parts">Отметки по которым будут производиться расчеты</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="toDate">Конечная дата</param>
        /// <param name="path">Путь к формируемому файлу</param>
        /// <param name="includeSmallBatch">false (по умолчанию) — изготовление без штучных партий;
        /// true — включая штучные (все записи). Штучная партия по регламенту: м/в &lt; 3 мин и ≤ 10 шт,
        /// либо м/в ≥ 3 мин и ≤ 5 шт (Part.IsSmallBatch)</param>
        /// <param name="serialParts">Серийные детали (опционально)</param>
        /// <returns>При удачном выполнении возвращает путь к записанному файлу</returns>
        public static async Task<string> ExportOperatorReportAsync(IEnumerable<Part> parts,
                                                                    DateTime fromDate,
                                                                    DateTime toDate,
                                                                    string path,
                                                                    bool includeSmallBatch,
                                                                    HashSet<string>? serialParts = null,
                                                                    bool includeExcludedParts = false,
                                                                    IProgress<string>? progress = null)
        {
            progress?.Report("Проверка вводных данных");
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Путь не может быть пустым", nameof(path));
            if (fromDate > toDate) throw new ArgumentException("Начальная дата не может быть позже конечной");

            progress?.Report("Получения данных об операторах");
            var operatorsTask = Database.GetOperatorsAsync();
            var qualificationsTask = Database.GetQualificationsAsync();
            await Task.WhenAll(operatorsTask, qualificationsTask);
            var operators = await operatorsTask;
            var qualifications = await qualificationsTask;
            
            var workDays = Util.GetWorkDaysBeetween(fromDate, toDate);

            progress?.Report("Фильтрация данных");
            var filteredParts = parts;
            if (!includeExcludedParts)
            {
                filteredParts = parts.Where(p => !p.ExcludeFromReports).ToList();
            }
            var onlySerial = serialParts?.Any() == true;

            if (onlySerial)
            {
                filteredParts = filteredParts
                    .Where(p => serialParts!.Contains(p.PartName.NormalizedPartNameWithoutComments()))
                    .ToList();
            }

            progress?.Report("Создание Ecxel книги");
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Отчет по операторам");
            var cm = new CM.Builder()
                .Add(CM.Operator)
                .Add(CM.Qualification)
                .Add(CM.Machine)
                .Add(CM.SerialOrders, "Не штучный")
                .Add(CM.SetupRatio, "Наладка средняя")
                .Add(CM.ProductionRatio, "Изготовление общее")
                .Add(CM.AverageReplacementTime)
                .Add(CM.CreateNcProgramTime)
                .Add(CM.MaintenanceTime)
                .Add(CM.ToolSearchingTime)
                .Add(CM.ToolChangingTime)
                .Add(CM.MentoringTime)
                .Add(CM.ContactingDepartmentsTime)
                .Add(CM.FixtureMakingTime)
                .Add(CM.HardwareFailureTime)
                .Add(CM.SpecialDowntimeTime)
                .Add(CM.SpecifiedDowntimes)
                .Add(CM.SpecifiedDowntimesEx, $"Простои{Environment.NewLine}(без отказа оборудования и обучения)")
                .Add(CM.GeneralRatio)
                .Add(CM.SetupsCount)
                .Add(CM.ProductionsCount)
                .Add(CM.TotalSetupsCount)
                .Add(CM.TotalProductionsCount)
                .Add(CM.WorkedShifts)
                .Add(CM.IncludedOperationsTime)
                .Add(CM.TotalTime)
                .Add(CM.EfficiencyCoefficient)
                .Add(CM.DowntimesCoefficient)
                .Add(CM.Coefficient)
                .Build();

            var ci = cm.GetIndexes();
            ConfigureWorksheetHeader(ws, cm);
            var coeffWs = wb.AddWorksheet("Коэффициенты");
            CreateCoefficientsWorksheet(coeffWs, qualifications);

            var row = 3;

            var minimumIncludedTimeRatioCell = ws.Cell(2, cm.Count + 2);
            var minimumIncludedTimeRatioAddr = minimumIncludedTimeRatioCell.Address.ToStringFixed();

            foreach (var partGroup in filteredParts
                .GroupBy(p => new { p.Operator, p.Machine })
                .OrderBy(g => g.Key.Machine)
                .ThenBy(g => g.Key.Operator))
            {
                progress?.Report($"Сбор данных о операторе {partGroup.Key.Operator} на станке {partGroup.Key.Machine}");
                if (partGroup.Key.Operator.ToLower() == "ученик") continue;

                var isSerialMachine = await Database.GetMachineSerialStatus(partGroup.Key.Machine);
                var groupParts = partGroup.ToList();

                var machine = groupParts.First().Machine;

                ws.Cell(row, ci[CM.Operator]).SetValue(partGroup.Key.Operator);

                var qualification = partGroup.Key.Operator.GetOperatorQualification(operators);
                var validQualification = int.TryParse(qualification, out int qualificationNumber);
                if (qualificationNumber == 0)
                {
                    continue;
                }
                var qual = qualifications.First(q => q.Value == qualificationNumber);
                ws.Cell(row, ci[CM.Qualification]).SetValue(validQualification ? qualificationNumber : qualification);
                ws.Cell(row, ci[CM.Machine]).SetValue(machine);
                ws.Cell(row, ci[CM.SerialOrders]).SetValue(isSerialMachine ? 1 : 0)
                    .Style.NumberFormat.SetFormat("\"✓\";;\"✗\"")
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                var averageSetupRatio = groupParts.AverageSetupRatio(machine);
                ws.Cell(row, ci[CM.SetupRatio])
                    .SetValue(averageSetupRatio)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                var productionRatio = groupParts
                    .Where(p => includeSmallBatch || !p.IsSmallBatch)
                    .ProductionRatio();
                ws.Cell(row, ci[CM.ProductionRatio])
                    .SetValue(productionRatio)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                var setupsCount = groupParts.Count(p =>
                    p.SetupRatio is not (0 or double.NaN or double.NegativeInfinity or double.PositiveInfinity));
                var productionsCount = groupParts.Count(p =>
                    !IsInvalidRatio(p.ProductionRatio) && (includeSmallBatch || !p.IsSmallBatch));

                // Знаменатели для setupsCount/productionsCount: сколько наладок/изготовлений
                // было предпринято вообще (факт есть), а не только сколько из них засчиталось
                // в КПД. Разница между этой парой и учтённой — записи без норматива, б/н, б/и
                // и т.п., а не «ничего не делал».
                var totalSetupsCount = groupParts.Count(p => p.SetupTimeFact > 0);
                var totalProductionsCount = groupParts.Count(p => p.FinishedCount > 0 || p.ProductionTimeFact > 0);

                ws.Cell(row, ci[CM.SetupsCount]).SetValue(setupsCount);
                ws.Cell(row, ci[CM.ProductionsCount]).SetValue(productionsCount);
                ws.Cell(row, ci[CM.TotalSetupsCount]).SetValue(totalSetupsCount);
                ws.Cell(row, ci[CM.TotalProductionsCount]).SetValue(totalProductionsCount);

                string setupRatioAddr = ws.Cell(row, ci[CM.SetupRatio]).Address.ToStringRelative();
                string productionRatioAddr = ws.Cell(row, ci[CM.ProductionRatio]).Address.ToStringRelative();
                string setupsCountAddr = ws.Cell(row, ci[CM.SetupsCount]).Address.ToStringRelative();
                string productionsCountAddr = ws.Cell(row, ci[CM.ProductionsCount]).Address.ToStringRelative();
                string qualificationAddr = ws.Cell(row, ci[CM.Qualification]).Address.ToStringRelative();
                string efficiencyCoeffAddr = ws.Cell(row, ci[CM.EfficiencyCoefficient]).Address.ToStringRelative();
                string downtimeCoeffAddr = ws.Cell(row, ci[CM.DowntimesCoefficient]).Address.ToStringRelative();

                if (isSerialMachine)
                {
                    // Для серийных станков: взвешенное среднее или только изготовление для 1 разряда
                    string generalRatioFormula =
                        $"=IF({qualificationAddr}=1,{productionRatioAddr}," +
                        $"IFERROR(({setupsCountAddr}*{setupRatioAddr}+{productionsCountAddr}*{productionRatioAddr})/({setupsCountAddr}+{productionsCountAddr}),0))";

                    ws.Cell(row, ci[CM.GeneralRatio]).FormulaA1 = generalRatioFormula;
                }
                else
                {
                    // Для несерийных станков: только наладка
                    ws.Cell(row, ci[CM.GeneralRatio]).FormulaA1 = $"={setupRatioAddr}";
                }
                ws.Cell(row, ci[CM.GeneralRatio]).Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                ws.Cell(row, ci[CM.AverageReplacementTime])
                    .SetValue(groupParts.AverageReplacementTimeRatio())
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;

                ws.Cell(row, ci[CM.CreateNcProgramTime]).SetValue(groupParts.Sum(p => p.CreateNcProgramTime));
                ws.Cell(row, ci[CM.MaintenanceTime]).SetValue(groupParts.Sum(p => p.MaintenanceTime));
                ws.Cell(row, ci[CM.ToolSearchingTime]).SetValue(groupParts.Sum(p => p.ToolSearchingTime));
                ws.Cell(row, ci[CM.ToolChangingTime]).SetValue(groupParts.Sum(p => p.ToolChangingTime));
                ws.Cell(row, ci[CM.MentoringTime]).SetValue(groupParts.Sum(p => p.MentoringTime));
                ws.Cell(row, ci[CM.ContactingDepartmentsTime]).SetValue(groupParts.Sum(p => p.ContactingDepartmentsTime));
                ws.Cell(row, ci[CM.FixtureMakingTime]).SetValue(groupParts.Sum(p => p.FixtureMakingTime));
                ws.Cell(row, ci[CM.HardwareFailureTime]).SetValue(groupParts.Sum(p => p.HardwareFailureTime));
                ws.Cell(row, ci[CM.SpecialDowntimeTime]).SetValue(groupParts.Sum(p => p.SpecialDowntimeTime));

                ws.Cell(row, ci[CM.SpecifiedDowntimes])
                    .SetValue(groupParts.SpecifiedDowntimesRatio(ShiftType.All))
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                var specDowntimesEx = groupParts.SpecifiedDowntimesRatioExcluding(new[] { Downtime.HardwareFailure, Downtime.Mentoring, Downtime.Special });

                ws.Cell(row, ci[CM.SpecifiedDowntimesEx])
                    .SetValue(specDowntimesEx)
                    .Style.NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.PercentInteger;

                var workedShifts = parts
                    .Where(p => p.Operator == partGroup.Key.Operator && p.Machine == partGroup.Key.Machine)
                    .Select(p => p.ShiftDate)
                    .Distinct()
                    .Count();
                ws.Cell(row, ci[CM.WorkedShifts]).SetValue(workedShifts);

                (TimeSpan totalTime, TimeSpan includedOperationsTime) = filteredParts
                .Where(p => p.Operator == partGroup.Key.Operator && p.Machine == partGroup.Key.Machine)
                .Aggregate(
                    (Total: TimeSpan.Zero, Excluded: TimeSpan.Zero),
                    (acc, p) => (
                        Total: acc.Total + p.FullWorkedTime(),
                        Excluded: acc.Excluded + NotExcludedTime(p, includeSmallBatch)
                    )
                );

                ws.Cell(row, ci[CM.IncludedOperationsTime]).SetValue(includedOperationsTime.TotalHours)
                    .Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.Integer);
                ws.Cell(row, ci[CM.TotalTime]).SetValue(totalTime.TotalHours)
                    .Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.Integer);

                if (validQualification)
                {
                    string generalRatioAddr = ws.Cell(row, ci[CM.GeneralRatio]).Address.ToStringRelative();
                    string specDowntimesExAddr = ws.Cell(row, ci[CM.SpecifiedDowntimesEx]).Address.ToStringRelative();
                    string workedShiftsAddr = ws.Cell(row, ci[CM.WorkedShifts]).Address.ToStringRelative();
                    string includedOperationsTimeAddr = ws.Cell(row, ci[CM.IncludedOperationsTime]).Address.ToStringRelative();
                    string totalTimeAddr = ws.Cell(row, ci[CM.TotalTime]).Address.ToStringRelative();

                    // Находим правильную строку в листе коэффициентов для текущей квалификации
                    var qualRow = qualifications.OrderBy(q => q.Value).ToList().IndexOf(qual) + 2; // +2 потому что первая строка - заголовки, и индексация с 1

                    // Создаем ссылки на ячейки с коэффициентами для текущей квалификации
                    string effHH = isSerialMachine ? $"Коэффициенты!B{qualRow}" : $"Коэффициенты!Z{qualRow}";
                    string effH = isSerialMachine ? $"Коэффициенты!C{qualRow}" : $"Коэффициенты!AA{qualRow}";
                    string effN = isSerialMachine ? $"Коэффициенты!D{qualRow}" : $"Коэффициенты!AB{qualRow}";
                    string effL = isSerialMachine ? $"Коэффициенты!E{qualRow}" : $"Коэффициенты!AC{qualRow}";
                    string effLL = isSerialMachine ? $"Коэффициенты!F{qualRow}" : $"Коэффициенты!AD{qualRow}";
                    string effLLL = isSerialMachine ? $"Коэффициенты!G{qualRow}" : $"Коэффициенты!AE{qualRow}";
                    string effCoeffHH = isSerialMachine ? $"Коэффициенты!H{qualRow}" : $"Коэффициенты!AF{qualRow}";
                    string effCoeffH = isSerialMachine ? $"Коэффициенты!I{qualRow}" : $"Коэффициенты!AG{qualRow}";
                    string effCoeffN = isSerialMachine ? $"Коэффициенты!J{qualRow}" : $"Коэффициенты!AH{qualRow}";
                    string effCoeffL = isSerialMachine ? $"Коэффициенты!K{qualRow}" : $"Коэффициенты!AI{qualRow}";
                    string effCoeffLL = isSerialMachine ? $"Коэффициенты!L{qualRow}" : $"Коэффициенты!AJ{qualRow}";
                    string effCoeffLLL = isSerialMachine ? $"Коэффициенты!M{qualRow}" : $"Коэффициенты!AK{qualRow}";

                    string downHH = $"Коэффициенты!N{qualRow}";
                    string downH = $"Коэффициенты!O{qualRow}";
                    string downN = $"Коэффициенты!P{qualRow}";
                    string downL = $"Коэффициенты!Q{qualRow}";
                    string downLL = $"Коэффициенты!R{qualRow}";
                    string downLLL = $"Коэффициенты!S{qualRow}";
                    string downCoeffHH = $"Коэффициенты!T{qualRow}";
                    string downCoeffH = $"Коэффициенты!U{qualRow}";
                    string downCoeffN = $"Коэффициенты!V{qualRow}";
                    string downCoeffL = $"Коэффициенты!W{qualRow}";
                    string downCoeffLL = $"Коэффициенты!X{qualRow}";
                    string downCoeffLLL = $"Коэффициенты!Y{qualRow}";

                    if (partGroup.Key.Machine.Contains("SKT21"))
                    {
                        downN = 0.15.ToString("#.##");
                    }

                    // Коэффициент эффективности (на основе общего коэффициента)
                    string efficiencyCoeff =
                        $"IF({generalRatioAddr}>{effHH},{effCoeffHH}," +
                        $"IF({generalRatioAddr}>{effH},{effCoeffH}," +
                        $"IF({generalRatioAddr}>{effN},{effCoeffN}," +
                        $"IF({generalRatioAddr}>{effL},{effCoeffL}," +
                        $"IF({generalRatioAddr}>{effLL},{effCoeffLL}," +
                        $"IF({generalRatioAddr}>{effLLL},{effCoeffLLL}," +
                        $"{effCoeffLLL}))))))";
                    efficiencyCoeff = $"=IF(AND({workedShiftsAddr}>={workDays / 6},{includedOperationsTimeAddr}>={totalTimeAddr}*{minimumIncludedTimeRatioAddr}),{efficiencyCoeff},\"\")";

                    ws.Cell(row, ci[CM.EfficiencyCoefficient]).FormulaA1 = efficiencyCoeff;

                    // Коэффициент простоев
                    // Для простоев условие обратное: чем МЕНЬШЕ простой, тем ВЫШЕ коэффициент
                    string downtimeCoeff =
                        $"IF({specDowntimesExAddr}<{downHH},{downCoeffHH}," +
                        $"IF({specDowntimesExAddr}<{downH},{downCoeffH}," +
                        $"IF({specDowntimesExAddr}<{downN},{downCoeffN}," +
                        $"IF({specDowntimesExAddr}<{downL},{downCoeffL}," +
                        $"IF({specDowntimesExAddr}<{downLL},{downCoeffLL}," +
                        $"IF({specDowntimesExAddr}<{downLLL},{downCoeffLLL}," +
                        $"{downCoeffLLL}))))))";

                    downtimeCoeff = $"=IF(AND({workedShiftsAddr}>={workDays / 6},{includedOperationsTimeAddr}>={totalTimeAddr}*{minimumIncludedTimeRatioAddr}),{downtimeCoeff},\"\")";

                    var reasons = new List<string>();

                    ws.Cell(row, ci[CM.DowntimesCoefficient]).FormulaA1 = downtimeCoeff;

                    if (workedShifts < workDays / 6) reasons.Add($"Недостаточно смен (минимум {workDays / 6});");
                    if (includedOperationsTime < totalTime * MinimumIncludedTimeRatio) reasons.Add($"Больше {MinimumIncludedTimeRatio*100:N0}% отработанного времени не учитывается.\n(Учтено {includedOperationsTime/totalTime:0.##%})");

                    // Итоговая формула: коэффициент применяется только при выполнении условий
                    string coefficientFormula = $"=IFERROR({efficiencyCoeffAddr}*{downtimeCoeffAddr},\"\")";
                    ws.Cell(row, ci[CM.Coefficient]).FormulaA1 = coefficientFormula;
                    if (!isSerialMachine && ws.Cell(row, ci[CM.EfficiencyCoefficient]).Value.IsNumber && ws.Cell(row, ci[CM.EfficiencyCoefficient]).Value.GetNumber() is double effCoeff && effCoeff > 1 && productionRatio < 0.75)
                    {
                        ws.Cell(row, ci[CM.Coefficient]).Value = "";
                        reasons.Add("Изготовление менее 75% на несерийном станке");
                    }
                    if (reasons.Count > 0) ws.Cell(row, ci[CM.Coefficient]).CreateComment().AddText($"Причины:\n{string.Join('\n', reasons)}");
                }
                row++;
            }

            ws.ApplyStandardBorders();
            ws.ApplyAutoFilter();
            ws.AdjustColumns();
            ws.Column(ci[CM.Qualification]).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Column(ci[CM.Qualification]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Columns(ci[CM.CreateNcProgramTime], ci[CM.SpecifiedDowntimes]).Group(true);

            // Константа для доли учтённого времени
            minimumIncludedTimeRatioCell.SetValue(MinimumIncludedTimeRatio).Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.PercentInteger);

            // Заголовок отчета
            var batchScope = includeSmallBatch
                ? "включая штучные партии"
                : "без штучных партий (м/в < 3 мин и ≤ 10 шт или м/в ≥ 3 мин и ≤ 5 шт)";
            ws.Cell(1, 1).Value =
                $"Отчёт по операторам за период с {fromDate:dd.MM.yyyy} по {toDate:dd.MM.yyyy} " +
                $"(изготовление: {batchScope}{(onlySerial ? "; только серийка" : "")})";
            ws.Range(1, ci[CM.Operator], 1, cm.Count).Merge();
            ws.Range(1, ci[CM.Operator], 1, 1).Style.Font.FontSize = 14;
            ws.Columns(ci[CM.SetupRatio], cm.Count).Width = 7;

            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
