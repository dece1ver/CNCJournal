using ClosedXML.Excel;
using libeLog.Views;
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
                ? MessageBoxWindow.Show(owner, "Открыть сохраненный файл?", "Вопросик",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.Yes)
                : MessageBoxWindow.Show("Открыть сохраненный файл?", "Вопросик",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.Yes);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
            }
        }
    }
}
