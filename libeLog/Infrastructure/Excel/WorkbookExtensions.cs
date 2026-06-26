using ClosedXML.Excel;
using System;
using System.Diagnostics;
using System.Windows;

namespace libeLog.Infrastructure.Excel
{
    public static class WorkbookExtensions
    {
        public static void SaveAndOfferOpen(this XLWorkbook wb, string path, Window? owner = null)
        {
            wb.SaveAs(path);
            var result = owner is not null
                ? MessageBox.Show(owner, "Открыть сохраненный файл?", "Вопросик",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBox.Show("Открыть сохраненный файл?", "Вопросик",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
            }
        }
    }
}
