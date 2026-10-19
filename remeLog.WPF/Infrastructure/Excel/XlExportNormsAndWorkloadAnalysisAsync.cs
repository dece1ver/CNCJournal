using ClosedXML.Excel;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Excel;
using libeLog.Models;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using remeLog.Models.Reports;
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
        public async static Task<string> ExportNormsAndWorkloadAnalysisAsync(ICollection<Part> parts, string path, IProgress<string> progress)
        {
            var wb = new XLWorkbook();
            var partsDict = parts
                .Select(p => new {
                    Part = p,
                    NormalizedName = p.PartName.NormalizedPartName()
                })
                .GroupBy(x => x.NormalizedName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Part).ToList()
                );

            // Выбор уникальных деталей, которые встречаются в 3+ заказах с количеством >= 50
            var totalUnique = partsDict
                .Where(kvp => kvp.Value
                    .Where(p => p.TotalCount >= 50)
                    .Select(p => p.Order)
                    .Distinct()
                    .Take(3)
                    .Count() == 3)
                .Select(kvp => kvp.Value.First())
                .ToList();

            var totalUniqueNames = totalUnique.Select(p => p.PartName.NormalizedPartName()).ToHashSet();

            Dictionary<string, int> serialPartsDict = (await libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!))
                .ToDictionary(p => p.PartName.NormalizedPartNameWithoutComments(), p => p.YearCount);

            var wsChanges = wb.AddWorksheet("Изменения нормативов");
            wsChanges.Style.Alignment.WrapText = false;
            progress.Report("Формирование листа \"Изменения нормативов\"");

            var changesBuilder = new CM.Builder()
                .Add(CM.Part)
                .Add(CM.YearCount)
                .Add(CM.Machine)
                .Add(CM.Setup)
                .Add(CM.Type)
                .Add(CM.Date)
                .Add(CM.OldValue)
                .Add(CM.ExcludedOperationsTime)
                .Add(CM.NewValue)
                .Add(CM.Change)
                .Add(CM.ChangeRatio)
                .Add(CM.OldYearValue)
                .Add(CM.NewYearValue)
                .Add(CM.YearChange)
                .Add(CM.YearChangeRatio)
                .Add(CM.SerialPerRuns)
                .Add(CM.SerialPerList)
                .Add(CM.IncreaseReason);

            var changesMap = changesBuilder.Build();
            ConfigureWorksheetHeader(wsChanges, changesMap, HeaderRotateOption.Horizontal, 65, 10);
            var changesCI = changesMap.GetIndexes();
            int changesRow = 3;

            var allChanges = new List<NormChange>();

            foreach (var partGroup in partsDict)
            {
                var partsForName = partGroup.Value;
                bool isInTotalUnique = totalUniqueNames.Contains(partGroup.Key);
                var normalizedPartName = partGroup.Key.NormalizedPartNameWithoutComments();
                bool isInSerialList = serialPartsDict.ContainsKey(normalizedPartName);

                var partsWithChanges = partsForName
                    .Where(p => (p.FixedSetupTimePlan != 0 && p.SetupTimePlan != 0) ||
                                (p.FixedProductionTimePlan != 0 && p.SingleProductionTimePlan != 0))
                    .OrderBy(p => p.EndMachiningTime)
                    .ToList();

                if (partsWithChanges.Count == 0)
                    continue;

                foreach (var changedPart in partsWithChanges)
                {
                    // наладка
                    if (changedPart.FixedSetupTimePlan != 0 && changedPart.SetupTimePlan != 0 &&
                        Math.Abs(changedPart.FixedSetupTimePlan - changedPart.SetupTimePlan) > 0.001)
                    {
                        allChanges.Add(new NormChange(
                            changedPart.PartName,
                            changedPart.Machine,
                            changedPart.Setup,
                            "Наладка",
                            changedPart.SetupTimePlan,
                            0.0,
                            changedPart.FixedSetupTimePlan,
                            changedPart.EndMachiningTime,
                            isInTotalUnique,
                            isInSerialList,
                            changedPart.IncreaseReason
                        ));
                    }

                    // изготовление
                    if (changedPart.FixedProductionTimePlan != 0 && changedPart.SingleProductionTimePlan != 0 &&
                        Math.Abs(changedPart.FixedProductionTimePlan - changedPart.SingleProductionTimePlan) > 0.001)
                    {
                        allChanges.Add(new NormChange(
                            changedPart.PartName,
                            changedPart.Machine,
                            changedPart.Setup,
                            "Изготовление",
                            changedPart.SingleProductionTimePlan,
                            changedPart.ExcludedOperationsTime,
                            changedPart.FixedProductionTimePlan,
                            changedPart.EndMachiningTime,
                            isInTotalUnique,
                            isInSerialList,
                            changedPart.IncreaseReason
                        ));
                    }
                }
            }
            var uniqueChanges = allChanges
                .GroupBy(c => new
                {
                    c.PartName,
                    //c.Machine,
                    c.Setup,
                    c.ChangeType,
                    c.OldValue,
                    c.ExcludedOperationsTime,
                    c.NewValue,
                    c.IsInTotalUnique,
                    c.IsInSerialList,
                    //c.IncreaseReason
                })
                .Select(g => g.OrderByDescending(c => c.Date).First())
                .ToList();

            foreach (var change in uniqueChanges)
            {
                wsChanges.Cell(changesRow, changesCI[CM.Part]).Value = change.PartName;

                var key = change.PartName.NormalizedPartNameWithoutComments();
                if (!serialPartsDict.TryGetValue(key, out var yearCount))
                {
                    wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Value = "Н/Д";
                }
                else
                {
                    wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Value = yearCount == 0 ? 1 : yearCount;
                }

                wsChanges.Cell(changesRow, changesCI[CM.Machine]).Value = change.Machine;
                wsChanges.Cell(changesRow, changesCI[CM.Setup]).Value = change.Setup;
                wsChanges.Cell(changesRow, changesCI[CM.Type]).Value = change.ChangeType;
                wsChanges.Cell(changesRow, changesCI[CM.Date]).Value = change.Date;
                wsChanges.Cell(changesRow, changesCI[CM.OldValue]).Value = change.OldValue;
                wsChanges.Cell(changesRow, changesCI[CM.ExcludedOperationsTime]).Value = change.ExcludedOperationsTime;
                wsChanges.Cell(changesRow, changesCI[CM.NewValue]).Value = change.NewValue;
                
                var oldYearValueFormula = $"({wsChanges.Cell(changesRow, changesCI[CM.OldValue]).Address.ToStringRelative()}" +
                    $"+{wsChanges.Cell(changesRow, changesCI[CM.ExcludedOperationsTime]).Address.ToStringRelative()})" +
                    $"*{(yearCount != 0 ? (change.ChangeType == "Изготовление" ? wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Address.ToStringRelative() : 1) : 1)}";

                wsChanges.Cell(changesRow, changesCI[CM.OldYearValue])
                    .FormulaA1 = $"={oldYearValueFormula}";

                var newYearValueFormula = $"({wsChanges.Cell(changesRow, changesCI[CM.NewValue]).Address.ToStringRelative()}" +
                    $"*{(yearCount != 0 ? (change.ChangeType == "Изготовление" ? wsChanges.Cell(changesRow, changesCI[CM.YearCount]).Address.ToStringRelative() : 1) : 1)})";

                wsChanges.Cell(changesRow, changesCI[CM.NewYearValue])
                    .FormulaA1 = $"={newYearValueFormula}";

                wsChanges.Cell(changesRow, changesCI[CM.SerialPerRuns]).Value = change.IsInTotalUnique;
                wsChanges.Cell(changesRow, changesCI[CM.SerialPerList]).Value = change.IsInSerialList;

                var newValueCell = wsChanges.Cell(changesRow, changesCI[CM.NewValue]).Address;
                var oldValueCell = wsChanges.Cell(changesRow, changesCI[CM.OldValue]).Address;
                var excludedOperationsTimeCell = wsChanges.Cell(changesRow, changesCI[CM.ExcludedOperationsTime]).Address.ToStringRelative();

                var newYearValueCell = wsChanges.Cell(changesRow, changesCI[CM.NewYearValue]).Address.ToStringRelative();
                var oldYearValueCell = wsChanges.Cell(changesRow, changesCI[CM.OldYearValue]).Address.ToStringRelative();

                wsChanges.Cell(changesRow, changesCI[CM.Change]).FormulaA1 = $"={newValueCell}-({oldValueCell}+{excludedOperationsTimeCell})";
                wsChanges.Cell(changesRow, changesCI[CM.ChangeRatio]).FormulaA1 = $"=IF({oldValueCell}+{excludedOperationsTimeCell}=0,0,{newValueCell}/({oldValueCell}+{excludedOperationsTimeCell})-1)";

                wsChanges.Cell(changesRow, changesCI[CM.YearChange]).FormulaA1 = $"={newYearValueCell}-{oldYearValueFormula}";
                wsChanges.Cell(changesRow, changesCI[CM.YearChangeRatio]).FormulaA1 = $"=IF({oldYearValueFormula}=0,0,({newYearValueCell}/({oldYearValueFormula}))-1)";

                wsChanges.Cell(changesRow, changesCI[CM.IncreaseReason]).Value = change.IncreaseReason;

                changesRow++;
            }

            if (changesRow > 3)
            {
                wsChanges.Range(2, 1, changesRow - 1, changesCI.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                wsChanges.Range(2, 1, changesRow - 1, changesCI.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                wsChanges.Range(3, changesCI[CM.ChangeRatio], changesRow - 1, changesCI[CM.ChangeRatio]).Style.NumberFormat.Format = "0.00%";
                wsChanges.Range(3, changesCI[CM.YearChangeRatio], changesRow - 1, changesCI[CM.YearChangeRatio]).Style.NumberFormat.Format = "0.00%";

                var dateRange = wsChanges.Range(3, changesCI[CM.Date], changesRow - 1, changesCI[CM.Date]);
                dateRange.Style.NumberFormat.Format = Constants.ShortDateFormat;

                var valueColumns = new[] { CM.OldValue, CM.ExcludedOperationsTime, CM.NewValue, CM.Change, CM.OldYearValue, CM.NewYearValue, CM.YearChange };
                foreach (var colName in valueColumns)
                {
                    var valueRange = wsChanges.Range(3, changesCI[colName], changesRow - 1, changesCI[colName]);
                    valueRange.Style.NumberFormat.Format = "0.00";
                }

                ApplyConditionalFormatting(wsChanges, changesRow, changesCI);
            }

            wsChanges.AdjustColumns();
            var table = wsChanges.RangeUsed().CreateTable();
            table.Name = "ИзмененияНормативов";
            table.Theme = XLTableTheme.TableStyleLight1;

            CalculateTotals(wsChanges, changesRow, changesCI);

            SetTitle(wsChanges, changesCI.Count, "История изменений нормативов");
            wsChanges.Column(changesCI[CM.Part]).Width = 70;
            wsChanges.Column(changesCI[CM.YearCount]).Width = 12;
            wsChanges.Columns(changesCI[CM.OldValue], changesCI[CM.SerialPerList]).Width = 14;
            wsChanges.Column(changesCI[CM.IncreaseReason]).Width = 70;

            progress.Report("Сохранение файла");
            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
