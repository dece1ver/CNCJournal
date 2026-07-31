using ClosedXML.Excel;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Excel;
using libeLog.Models;
using remeLog.Infrastructure.Extensions;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Xl
    {
        public static string FillPcaReport(Part part, string path)
        {
            if (string.IsNullOrEmpty(AppSettings.PcaReportPath))
                return "Не удалось найти шаблон";

            var tmpFile = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}{Path.GetExtension(AppSettings.PcaReportPath)}");
            File.Copy(AppSettings.PcaReportPath, tmpFile);

            var wb = new XLWorkbook(tmpFile);
            var ws = wb.Worksheets.First();

            var listSheet = wb.Worksheets.Add("Lists");

            ws.Cell("D1").SetValue(part.Machine);
            ws.Cell("D2").SetValue(part.Operator.ToShortFio());
            ws.Cell("D3").SetValue(part.Order);
            ws.Cell("D4").SetValue(part.PartName);

            for (int r = 1; r <= 5; r++)
            {
                var cell = ws.Cell($"D{r}");
                cell.Style.Font.SetFontName("Segoe Script")
                    .Font.SetFontSize(10)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            }

            for (int i = 0; i < AppSettings.CncOperations.Length; i++)
                listSheet.Cell(i + 1, 1).Value = AppSettings.CncOperations[i];
            var operationsRange = listSheet.Range(1, 1, AppSettings.CncOperations.Length, 1);

            listSheet.Cell(1, 2).Value = "";
            listSheet.Cell(2, 2).Value = "✓";
            var checkRange = listSheet.Range(1, 2, 2, 2);

            listSheet.Visibility = XLWorksheetVisibility.VeryHidden;

            var opCell = ws.Cell("D5");
            opCell.Style.Font.SetFontName("Segoe Script")
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            var operationValidation = opCell.CreateDataValidation();
            operationValidation.IgnoreBlanks = true;
            operationValidation.InCellDropdown = true;
            operationValidation.List(operationsRange);

            var yesNoRange = ws.Range("E8:G15");
            yesNoRange.Style.Font.SetFontSize(14)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            var yesNoValidation = yesNoRange.CreateDataValidation();
            yesNoValidation.IgnoreBlanks = true;
            yesNoValidation.InCellDropdown = true;
            yesNoValidation.List(checkRange);

            if (part.SetupRatio is > 0.7 and < 2)
            {
                ws.Cell("E14").Value = listSheet.Cell(2, 2).Value;
            }
            else
            {
                if (part.SetupTimeFact > 0)
                {
                    ws.Cell("F14").Value = listSheet.Cell(2, 2).Value;
                } 
                else
                {
                    ws.Cell("G14").Value = listSheet.Cell(2, 2).Value;
                }
            }
            if (part.ProductionRatio is > 0.7 and < 1.2)
            {
                ws.Cell("E15").Value = listSheet.Cell(2, 2).Value;
            }
            else
            {
                ws.Cell("F15").Value = listSheet.Cell(2, 2).Value;
            }

            ws.Cell("E16").FormulaA1 = "=IF(COUNTA(E8:E15)=COUNTA(B8:D15),\"Отклонений нет\",\"Есть отклонения\")";
            ws.Cell("E17").FormulaA1 = "=IF(COUNTA(E8:E15)=COUNTA(B8:D15),\"Не требуются\",\"\")";

            if (part.SetupTimePlan != part.FixedSetupTimePlan && part.FixedSetupTimePlan > 0)
            {
                if (part.EngineerConclusion.Contains("Изменение технологии"))
                {
                    ws.Cell("E17").SetValue("Изменение технологии");
                    ws.Cell("F8").Value = listSheet.Cell(2, 2).Value;
                } else
                {
                    ws.Cell("H17").SetValue("Корректировка нормативов");
                }
            }


            if (part.SingleProductionTimePlan != part.FixedProductionTimePlan && part.FixedProductionTimePlan > 0)
            {
                if (part.EngineerConclusion.Contains("Изменение технологии"))
                {
                    ws.Cell("E17").SetValue("Изменение технологии");
                    ws.Cell("F8").Value = listSheet.Cell(2, 2).Value;
                }
                else
                {
                    ws.Cell("E17").SetValue("Корректировка нормативов");
                }
            }

            ws.Range("E16:E17").Style.Font.SetFontName("Segoe Script")
                .Font.SetFontSize(10);
            var user = Utils.GetUserInfo();
            
            ws.Cell("H23").SetValue($"{user.Title}{Environment.NewLine}{user.DisplayName}").Style
                .Font.SetFontName("Segoe Script")
                .Font.SetFontSize(10)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            ws.Cell("E25").SetValue(part.EndMachiningTime.ToString("d")).Style
                .Font.SetFontName("Segoe Script")
                .Font.SetFontSize(12)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            ws.Range("H8:H15").Style.Font.SetFontName("Segoe Script").Font.SetFontSize(10)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            ws.RowsUsed().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);


            wb.SaveAndOfferOpen(path);
            return path;
        }
    }
}
