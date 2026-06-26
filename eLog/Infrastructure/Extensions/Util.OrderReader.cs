using ClosedXML.Excel;
using eLog.Models;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace eLog.Infrastructure.Extensions
{
    public static partial class Util
    {
        public static List<Part> GetPartsFromOrder(this string orderNumber)
        {
            try
            {
                return ProcessWorkbook(AppSettings.LocalOrdersFile, orderNumber);
            }
            catch (Exception ex)
            {
                WriteLog(ex, "Ошибка при работе с основным файлом");

                try
                {
                    return ProcessWorkbook(AppSettings.BackupOrdersFile, orderNumber);
                }
                catch (Exception backupEx)
                {
                    WriteLog(backupEx, "Ошибка при работе с резервным файлом");
                }
            }

            return new List<Part>();
        }

        private static List<Part> ProcessWorkbook(string filePath, string orderNumber)
        {
            using var wb = new XLWorkbook(filePath);
            var worksheet = wb.Worksheet(1);
            var searchValue = orderNumber.ToUpper();

            return worksheet.Rows()
                .Where(xlRow => IsRowValid(xlRow, searchValue))
                .Select(xlRow => CreatePartFromRow(xlRow))
                .Where(part => part != null)
                .ToList();
        }

        private static bool IsRowValid(IXLRow row, string searchValue)
        {
            try
            {
                var cell1 = row.Cell(1);
                var cell2 = row.Cell(2);
                var cell4 = row.Cell(4);

                return cell1.Value.IsText
                       && cell2.Value.IsText
                       && cell4.Value.IsNumber
                       && cell1.Value.GetText().Contains(searchValue);
            }
            catch
            {
                return false;
            }
        }

        private static Part CreatePartFromRow(IXLRow row)
        {
            try
            {
                if (row.Cell(3).Value.TryGetText(out string characteristic) && !string.IsNullOrEmpty(characteristic))
                {
                    var partName = row.Cell(2).Value.GetText();
                    if (characteristic.ToLowerInvariant().Trim() != "готовая продукция") partName += $" {characteristic}";
                    return new Part
                    {
                        Name = partName,
                        TotalCount = Convert.ToInt32(row.Cell(4).Value.GetNumber())
                    };
                }
                var prefix = row.Cell(2).Value.GetText();
                var cellValue = row.Cell(1).Value.GetText();
                var suffix = cellValue.Contains(prefix) ? cellValue.Split(
                    new[] { prefix },
                    StringSplitOptions.RemoveEmptyEntries
                )[1] : "";

                var cleanName = (prefix + suffix)
                    .Replace("[", "")
                    .Replace("]", "")
                    .Replace("готовая продукция", "")
                    .Trim();

                return new Part
                {
                    Name = cleanName,
                    TotalCount = Convert.ToInt32(row.Cell(4).Value.GetNumber())
                };
            }
            catch
            {
                return null!;
            }
        }

        private static string FormatPartName(string name, string prefix)
        {
            var cleanName = name.Replace(prefix, "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("готовая продукция", "")
                .Trim();

            return $"{prefix} {cleanName}".Trim();
        }
    }
}
