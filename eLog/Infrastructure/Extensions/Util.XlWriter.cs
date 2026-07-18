using ClosedXML.Excel;
using eLog.Models;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Enums;
using libeLog.Models;
using libeLog.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static eLog.Infrastructure.Text;

namespace eLog.Infrastructure.Extensions
{
    public static partial class Util
    {
        public static bool ValidXl()
        {
            try
            {
                using var wb = new XLWorkbook(AppSettings.Instance.UpdatePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async static Task<int> WriteToXlAsync(this Part part, IProgress<string> progress)
        {
            if (AppSettings.Instance.DebugMode) { WriteLog(part, $"Новая запись информации о детали."); }
            var id = -1;
            if (!File.Exists(AppSettings.Instance.UpdatePath)) return -3;
            var partIndex = AppSettings.Instance.Parts.IndexOf(part);
            var prevPart = partIndex != -1 && AppSettings.Instance.Parts.Count > partIndex + 1 ? AppSettings.Instance.Parts[partIndex + 1] : null;
            try
            {
                progress.Report("Создание бэкапа таблицы...");
                if (! await BackupXlAsync(progress)) throw new IOException("Ошибка при создании бэкапа таблицы.");
                using (var fs = new FileStream(AppSettings.Instance.UpdatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    progress.Report("Создание записи и присвоение номера...");
                    var wb = new XLWorkbook(fs, new LoadOptions() { RecalculateAllFormulas = false });
                    var ws = wb.Worksheet(1);
                    ws.LastRowUsed().InsertRowsBelow(1);
                    IXLRow? prevRow = null;
                    var partial = SetPartialState(ref part, false);
                    var combinedDownTimes = part.DownTimes.Combine();
                    foreach (var xlRow in ws.Rows())
                    {
                        if (xlRow is null) continue;
                        var num = xlRow.Cell(1).Value.IsNumber ? (int)xlRow.Cell(1).Value.GetNumber() : 0;
                        var hasGuidValue = xlRow.Cell(36).Value.IsText;
                        var stringGuid = hasGuidValue ? xlRow.Cell(36).Value.GetText() : "";
                        if (!Guid.TryParse(stringGuid, out Guid guid))
                        {
                            guid = Guid.Empty;
                        }
                        if (guid == part.Guid && part.Guid != Guid.Empty)
                        {
                            if (AppSettings.Instance.DebugMode) { WriteLog($"Найден совпадающий GUID - {part.Guid}\n\tОтмена записи."); }
                            part.Id = num;
                            return -2;
                        }
                        if (id <= num) id = num + 1;
                        if (!xlRow.Cell(6).Value.IsBlank)
                        {
                            prevRow = xlRow;
                            continue;
                        }
                        xlRow.Style = prevRow!.Style;
                        xlRow.Cell(1).Value = id;
                        xlRow.Cell(2).FormulaR1C1 = prevRow.Cell(2).FormulaR1C1;
                        xlRow.Cell(3).FormulaR1C1 = prevRow.Cell(3).FormulaR1C1;
                        xlRow.Cell(4).FormulaR1C1 = prevRow.Cell(4).FormulaR1C1;
                        xlRow.Cell(5).Value = $"{part.OperatorComments}\n{combinedDownTimes.Report()}".Trim();
                        var needDiscrease = part.Shift == NightShift && part.EndMachiningTime < new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddHours(8);
                        xlRow.Cell(6).Value = needDiscrease
                            ? new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddDays(-1)
                            : new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day);
                        xlRow.Cell(7).Value = AppSettings.Instance.Machine?.Name ?? "???";
                        xlRow.Cell(8).Value = part.Shift;
                        xlRow.Cell(9).Value = part.Operator.FullName.Trim();
                        xlRow.Cell(10).Value = part.FullName;
                        xlRow.Cell(11).Value = part.Order;
                        xlRow.Cell(12).Value = part.FinishedCount;
                        xlRow.Cell(13).Value = part.Setup;
                        xlRow.Cell(14).Value = part.StartSetupTime.ToString("HH:mm");
                        xlRow.Cell(15).Value = part.StartMachiningTime.ToString("HH:mm");
                        xlRow.Cell(16).Value = partial ? 0 : part.SetupTimeFact.ToString(@"hh\:mm");
                        xlRow.Cell(17).Value = part.SetupTimePlan;
                        xlRow.Cell(18).FormulaR1C1 = prevRow.Cell(18).FormulaR1C1;
                        xlRow.Cell(19).FormulaR1C1 = prevRow.Cell(19).FormulaR1C1;
                        xlRow.Cell(20).Value = part.StartMachiningTime.ToString("HH:mm");
                        xlRow.Cell(21).Value = part.EndMachiningTime.ToString("HH:mm");
                        xlRow.Cell(22).Value = part.ProductionTimeFact.ToString(@"hh\:mm");
                        xlRow.Cell(23).Value = part.SingleProductionTimePlan;
                        xlRow.Cell(24).Value = Math.Round(part.MachineTime.TotalMinutes, 2);
                        for (var i = 25; i <= 33; i++)
                        {
                            xlRow.Cell(i).FormulaR1C1 = prevRow.Cell(i).FormulaR1C1;
                        }

                        var shiftTime = part.Shift == Text.DayShift ? 660 : 630;
                        xlRow.Cell(34).Value = Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Setup, Type: not DownTime.Types.PartialSetup }).TotalMinutes(), 0);
                        xlRow.Cell(35).Value = Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Machining }).TotalMinutes(), 0);
                        xlRow.Cell(36).Value = part.Guid.ToString();
                        var partialSetupTime = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.PartialSetup }).TotalMinutes(), 0);
                        xlRow.Cell(37).Value = partialSetupTime > shiftTime ? shiftTime : partialSetupTime;
                        xlRow.Cell(38).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Maintenance }).TotalMinutes(), 0);
                        xlRow.Cell(39).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ToolSearching }).TotalMinutes(), 0);
                        xlRow.Cell(40).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Mentoring }).TotalMinutes(), 0);
                        xlRow.Cell(41).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ContactingDepartments }).TotalMinutes(), 0);
                        xlRow.Cell(42).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.FixtureMaking }).TotalMinutes(), 0);
                        xlRow.Cell(43).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.HardwareFailure }).TotalMinutes(), 0);
                        var partSetupTimePlanReport = prevPart != null && prevPart.Order == part.Order && prevPart.Setup == part.Setup ? 0 : part.SetupTimePlan;
                        if (partSetupTimePlanReport == 0 && part.SetupTimeFact.TotalMinutes > 0) partSetupTimePlanReport = part.SetupTimeFact.TotalMinutes;
                        if (partSetupTimePlanReport == 0 && part.SetupTimePlan == 0)
                        {
                            var partialTime = part.DownTimes.Where(x => x.Type == DownTime.Types.PartialSetup).TotalMinutes();
                            if (partialTime > 0) partSetupTimePlanReport = partialTime;
                        }
                        xlRow.Cell(44).Value = partSetupTimePlanReport;
                        xlRow.Cell(45).Value = part.SetupTimePlan;
                        xlRow.Cell(46).Value = part.SingleProductionTimePlan;

                        for (var i = 1; i <= 46; i++)
                        {
                            xlRow.Cell(i).Style = prevRow.Cell(i).Style;
                        }
                        break;
                    }
                    progress.Report($"Присвоен номер {id}. Запись в таблицу...");
                    if (AppSettings.Instance.DebugMode) { WriteLog($"Присвоен номер {id}. Запись в таблицу..."); }
                    Debug.Print("Write");
                    wb.Save(true);
                    if (AppSettings.Instance.DebugMode) { WriteLog($"Записано."); }
                    Debug.Print("Ok");
                }
            }
            catch (IOException ioEx)
            {
                Debug.Print("IOError");
                if (AppSettings.Instance.DebugMode) { WriteLog(ioEx); }
                return -4;
            }
            catch (KeyNotFoundException keyNotFoundEx)
            {
                progress.Report("Ошибка чтения, возможно таблица повреждена");
                WriteLog(keyNotFoundEx, $"(KeyNotFoundException) Не удалось открыть XL файл для записи детали {part.FullName} по заказу {part.Order}.\n\tОператор - {part.Operator.FullName}");
                Debug.Print("KeyNotFoundException");
                TryRestoreXl();
            }
            catch (OutOfMemoryException outOfMemEx)
            {
                progress.Report("Нехватка памяти при работе с XL файлом");
                WriteLog(outOfMemEx, $"Нехватка памяти при работе с XL файлом для записи детали {part.FullName} по заказу {part.Order}.\n\tОператор - {part.Operator.FullName}");
                Debug.Print("OutOfMemoryException");
                TryRestoreXl();
            }
            catch (ArgumentException argEx)
            {
                progress.Report("Ошибка");
                if (AppSettings.Instance.DebugMode) { WriteLog(argEx); }
                MessageBoxWindow.Show($"{argEx.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception e)
            {
                progress.Report("Ошибка");
                WriteLog(e, $"Ошибка при записи детали {part.FullName} по заказу {part.Order}.\n\tОператор - {part.Operator.FullName}");
            }
            if (!ValidXl())
            {
                WriteLog("Файл невалидный, требуется восстановление.");
                TryRestoreXl();
                part.Id = -1;
                return -1;
            }
            return id;
        }

        public async static Task<WriteResult> RewriteToXlAsync(this Part part, IProgress<string> progress, bool doBackup = true)
        {
            var partIndex = AppSettings.Instance.Parts.IndexOf(part);
            var prevPart = partIndex != -1 && AppSettings.Instance.Parts.Count > partIndex + 1 ? AppSettings.Instance.Parts[partIndex + 1] : null;

            if (AppSettings.Instance.DebugMode) { WriteLog(part, $"Обновление информации о детали."); }

            if (AppSettings.Instance.DebugMode && prevPart != null) { WriteLog(prevPart, $"Прошлая деталь."); }

            if (!File.Exists(AppSettings.Instance.UpdatePath))
            {
                if (AppSettings.Instance.DebugMode) { WriteLog($"Путь к таблице не существует."); }
                return WriteResult.FileNotExist;
            }
            if (part.IsFinished == Part.State.InProgress)
            {
                if (AppSettings.Instance.DebugMode) { WriteLog($"Изготовление в процессе, запись не требуется."); }
                return WriteResult.DontNeed;
            }
            var result = WriteResult.NotFinded;
            try
            {
                var partial = SetPartialState(ref part, false);

                if (doBackup)
                {
                    if (!await BackupXlAsync(progress))
                    {
                        throw new IOException("Ошибка при создании бэкапа таблицы.");
                    }
                }
                progress.Report("Чтение таблицы...");
                using (var fs = new FileStream(AppSettings.Instance.UpdatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var wb = new XLWorkbook(fs, new LoadOptions() { RecalculateAllFormulas = false });


                    var combinedDownTimes = part.DownTimes.Combine();
                    if (AppSettings.Instance.DebugMode) { WriteLog($"Поиск позиции..."); }
                    progress.Report("Поиск позиции...");
                    foreach (var xlRow in wb.Worksheet(1).Rows())
                    {
                        var rowWithPart = xlRow.Cell(1).Value.IsNumber;
                        var rowNum = rowWithPart ? (int)xlRow.Cell(1).Value.GetNumber() : 0;
                        var hasGuidValue = xlRow.Cell(36).Value.IsText;
                        var stringGuid = hasGuidValue ? xlRow.Cell(36).Value.GetText() : "";
                        if (!Guid.TryParse(stringGuid, out Guid guid))
                        {
                            guid = Guid.Empty;
                        }
                        if (!rowWithPart || (rowNum == part.Id && guid == Guid.Empty) || guid != part.Guid) continue;
                        if (AppSettings.Instance.DebugMode) { WriteLog($"Позиция найдена на строке №{rowNum}."); }
                        progress.Report($"Позиция найдена на строке №{rowNum}");
                        xlRow.Cell(5).Value = $"{part.OperatorComments}\n{combinedDownTimes.Report()}".Trim();
                        var needDiscrease = part.Shift == NightShift && part.EndMachiningTime < new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddHours(8);
                        xlRow.Cell(6).Value = needDiscrease
                            ? new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddDays(-1)
                            : new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day);
                        xlRow.Cell(7).Value = AppSettings.Instance.Machine?.Name ?? "???";
                        xlRow.Cell(8).Value = part.Shift;
                        xlRow.Cell(9).Value = part.Operator.FullName.Trim();
                        xlRow.Cell(10).Value = part.FullName;
                        xlRow.Cell(11).Value = part.Order;
                        xlRow.Cell(12).Value = part.FinishedCount;
                        xlRow.Cell(13).Value = part.Setup;
                        xlRow.Cell(14).Value = part.StartSetupTime.ToString("HH:mm");
                        xlRow.Cell(15).Value = part.StartMachiningTime.ToString("HH:mm");
                        xlRow.Cell(16).Value = partial ? 0 : part.SetupTimeFact.ToString(@"hh\:mm");
                        xlRow.Cell(17).Value = part.SetupTimePlan;
                        xlRow.Cell(20).Value = part.StartMachiningTime.ToString("HH:mm");
                        xlRow.Cell(21).Value = part.EndMachiningTime.ToString("HH:mm");
                        xlRow.Cell(22).Value = part.ProductionTimeFact.ToString(@"hh\:mm");
                        xlRow.Cell(23).Value = part.SingleProductionTimePlan;
                        xlRow.Cell(24).Value = Math.Round(part.MachineTime.TotalMinutes, 2);
                        var shiftTime = part.Shift == Text.DayShift ? 660 : 630;
                        xlRow.Cell(34).Value = Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Setup, Type: not DownTime.Types.PartialSetup }).TotalMinutes(), 0);
                        xlRow.Cell(35).Value = Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Machining }).TotalMinutes(), 0);
                        xlRow.Cell(36).Value = part.Guid.ToString();
                        var partialSetupTime = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.PartialSetup }).TotalMinutes(), 0);
                        xlRow.Cell(37).Value = partialSetupTime > shiftTime ? shiftTime : partialSetupTime;
                        xlRow.Cell(38).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Maintenance }).TotalMinutes(), 0);
                        xlRow.Cell(39).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ToolSearching }).TotalMinutes(), 0);
                        xlRow.Cell(40).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Mentoring }).TotalMinutes(), 0);
                        xlRow.Cell(41).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ContactingDepartments }).TotalMinutes(), 0);
                        xlRow.Cell(42).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.FixtureMaking }).TotalMinutes(), 0);
                        xlRow.Cell(43).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.HardwareFailure }).TotalMinutes(), 0);
                        var partSetupTimePlanReport = prevPart != null && prevPart.Order == part.Order && prevPart.Setup == part.Setup ? 0 : part.SetupTimePlan;
                        if (partSetupTimePlanReport == 0 && part.SetupTimeFact.TotalMinutes > 0) partSetupTimePlanReport = part.SetupTimeFact.TotalMinutes;
                        if (partSetupTimePlanReport == 0 && part.SetupTimePlan == 0)
                        {
                            var partialTime = part.DownTimes.Where(x => x.Type == DownTime.Types.PartialSetup).TotalMinutes();
                            if (partialTime > 0) partSetupTimePlanReport = partialTime;
                        }
                        xlRow.Cell(44).Value = partSetupTimePlanReport;
                        xlRow.Cell(45).Value = part.SetupTimePlan;
                        xlRow.Cell(46).Value = part.SingleProductionTimePlan;
                        result = WriteResult.Ok;
                        progress.Report("Запись таблицы...");
                        if (AppSettings.Instance.DebugMode) { WriteLog($"Запись в таблицу..."); }
                        Debug.Print("Rewrite");
                        wb.Save(true);
                        Debug.Print("Ok");
                        if (AppSettings.Instance.DebugMode) { WriteLog($"Записано."); }
                        break;
                    }
                    if (AppSettings.Instance.DebugMode && result is WriteResult.NotFinded) { WriteLog($"Деталь не найдена."); }
                }
            }

            catch (IOException ioEx)
            {
                if (AppSettings.Instance.DebugMode) { WriteLog(ioEx); }
                progress.Report("Ошибка ввода/вывода");
                return WriteResult.IOError;
            }
            catch (KeyNotFoundException keyNotFoundEx)
            {
                progress.Report("Ошибка чтения, возможно таблица повреждена");
                WriteLog(keyNotFoundEx, $"(KeyNotFoundException) Не удалось открыть XL файл для записи детали {part.FullName} по заказу {part.Order}.\n\tОператор - {part.Operator.FullName}");
                Debug.Print("KeyNotFoundException");
                TryRestoreXl();
                return WriteResult.Error;
            }
            catch (OutOfMemoryException outOfMemEx)
            {
                progress.Report("Нехватка памяти при работе с XL файлом");
                WriteLog(outOfMemEx, $"Нехватка памяти при работе с XL файлом для записи детали {part.FullName} по заказу {part.Order}.\n\tОператор - {part.Operator.FullName}");
                Debug.Print("OutOfMemoryException");
                TryRestoreXl();
                return WriteResult.Error;
            }
            catch (Exception e)
            {
                progress.Report("Ошибка");
                Debug.Print("Необработанное исключение");
                WriteLog(e, $"Ошибка при перезаписи детали {part.FullName} по заказу {part.Order}.\n\tОператор - {part.Operator.FullName}");
                TryRestoreXl();
                return WriteResult.Error;
            }
            if (!ValidXl())
            {
                progress.Report("Файл невалидный, требуется восстановление");
                WriteLog("Файл невалидный, требуется восстановление.");
                TryRestoreXl();
                return WriteResult.Error;
            }
            return result;
        }

        private async static Task<bool> BackupXlAsync(IProgress<string> progress, int count = 3)
        {
            for (int i = 1; i <= count; i++)
            {

                if (AppSettings.Instance.DebugMode) WriteLog($"Попытка бэкапа таблицы...{i}");
                try
                {
                    await Task.Run(() =>
                    {
                        progress.Report(i == 1 ? "Создание бэкапа таблицы..." : $"Создание бэкапа таблицы...попытка №{i}");
                        using var wb = new XLWorkbook(AppSettings.Instance.UpdatePath, new LoadOptions() { RecalculateAllFormulas = false });
                        File.Copy(AppSettings.Instance.UpdatePath, AppSettings.XlReservedPath, true);
                        if (AppSettings.Instance.DebugMode) WriteLog("Успешно.");
                        Thread.Sleep(200);
                    });
                    return true;
                }
                catch
                {
                    Thread.Sleep(5000);
                }
            }
            return false;

        }

        private static void TryRestoreXl()
        {
            try
            {
                Debug.Print("Restore");
                WriteLog("Попытка восстановления XL файла...");
                File.Copy(AppSettings.XlReservedPath, AppSettings.Instance.UpdatePath, true);
                WriteLog("Успешно.");
                Debug.Print("Ok");
            }
            catch (Exception ex)
            {
                Debug.Print("Failed");
                WriteLog(ex, "Неудачно.");
            }
        }
    }
}
