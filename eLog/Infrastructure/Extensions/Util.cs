using ClosedXML.Excel;
using DocumentFormat.OpenXml.Bibliography;
using eLog.Models;
using eLog.Views.Windows.Dialogs;
using libeLog;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Enums;
using libeLog.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static eLog.Infrastructure.Extensions.Text;

namespace eLog.Infrastructure.Extensions
{
    public static class Util
    {

        public enum WriteResult
        {
            Ok, IOError, NotFinded, Error, FileNotExist, DontNeed
        }

        /// <summary>
        /// Расширение для получения информации о заказе по номеру М/Л.
        /// </summary>
        /// <param name="orderNumber">Номер М/Л.</param>
        /// <returns>Список объектов <see cref="Part"/> с деталями заказа.</returns>
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

        /// <summary>
        /// Обрабатывает Excel-файл для поиска деталей заказа по номеру М/Л.
        /// </summary>
        /// <param name="filePath">Путь к файлу Excel.</param>
        /// <param name="orderNumber">Номер М/Л.</param>
        /// <returns>Список объектов <see cref="Part"/> с деталями заказа.</returns>
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

        /// <summary>
        /// Проверяет, является ли строка валидной для обработки.
        /// </summary>
        /// <param name="row">Объект строки <see cref="IXLRow"/>.</param>
        /// <param name="searchValue">Значение для поиска.</param>
        /// <returns>Истина, если строка валидна; иначе ложь.</returns>
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

        /// <summary>
        /// Создает объект <see cref="Part"/> из строки Excel.
        /// </summary>
        /// <param name="row">Объект строки <see cref="IXLRow"/>.</param>
        /// <returns>Объект <see cref="Part"/>, если данные корректны; иначе null.</returns>
        private static Part CreatePartFromRow(IXLRow row)
        {
            try
            {
                // если характеристика есть в 3 столбце
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
                // если нету, то пробуем вытянуть из первого
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

        /// <summary>
        /// Форматирует имя детали с учетом префикса.
        /// </summary>
        /// <param name="name">Исходное имя детали.</param>
        /// <param name="prefix">Префикс для форматирования.</param>
        /// <returns>Отформатированное имя детали.</returns>
        private static string FormatPartName(string name, string prefix)
        {
            var cleanName = name.Replace(prefix, "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("готовая продукция", "")
                .Trim();

            return $"{prefix} {cleanName}".Trim();
        }

        public static bool GetBarCode(ref string barCode)
        {
            var dlg = new ReadBarCodeWindow()
            {
                BarCode = string.Empty,
                Owner = Application.Current.MainWindow,
            };
            if (dlg.ShowDialog() != true) return false;
            barCode = dlg.BarCode;
            return true;
        }

        /// <summary>
        /// Получение информации о детали с БД (пока имитация)
        /// </summary>
        /// <param name="barCode">Шрихкод</param>
        /// <returns></returns>
        public static Part GetPartFromBarCode(this string barCode)
        {
            var names = new[] { "Ниппель", "Корпус", "Гайка", "Фланец", "Штуцер", "Седло", "Крышка", "Корпус приводной камеры", "Корпус проточной части" };
            var numbers = new[] { "АР110-01-001", "АР110-01-002", "АР110-01-003", "АР110-01-004", "АР110-01-005", "АРМ2-31.4-02-340-Х6-081-01", "АРМ2-31.4-02-340-Х6-071" };
            var orders = new[] { "УЧ-1/00001.1.1", "УЧ-1/00001.1.2", "УЧ-1/00001.1.3", "УЧ-1/00001.1.4", "УЧ-1/00001.1.5", "УЧ-1/00001.1.6", "УЧ-1/00001.1.7", "УЧ-1/00001.1.8" };

            return new Part(
                names[new Random().Next(0, names.Length)],
                numbers[new Random().Next(0, numbers.Length)],
                (byte)new Random().Next(1, 4),
                orders[new Random().Next(0, orders.Length)],
                new Random().Next(1, 20) * 10,
                new Random().Next(4, 18) * 10,
                new Random().Next(1, 5));
        }

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

        /// <summary>
        /// Устанавливает простой "Частичная наладка" на наладку данной детали, если предыдущая деталь была завершена с таким же простоем без выполненных деталей. 
        /// </summary>
        /// <param name="part">Текущая деталь</param>
        /// <returns>Является ли наладка данной детали "доналадкой" после частичной наладки.</returns>
        public static bool SetPartialState(ref Part part, bool update = true)
        {
            var index = AppSettings.Instance.Parts.IndexOf(part);
            var prevPart = index != -1 && AppSettings.Instance.Parts.Count > index + 1 ? AppSettings.Instance.Parts[index + 1] : null;
            if (prevPart is null) return false;
            var partial = part.IsFinished == Part.State.PartialSetup ||
                          prevPart is { IsFinished: Part.State.PartialSetup, FinishedCount: 0 }
                          && part.SetupTimeFact.Ticks > 0
                          && part.FullName == prevPart.FullName
                          && part.Order == prevPart.Order;
            if (partial)
            {
                if (!update) return partial;
                if (part.DownTimes.Any(x => x.Type is not DownTime.Types.PartialSetup))
                {
                    part.DownTimes = new DeepObservableCollection<DownTime>(
                        part.DownTimes.Where(downtime => downtime.Type != DownTime.Types.PartialSetup));
                    var sortedDowntimes = part.DownTimes.Where(d => d.Relation is DownTime.Relations.Setup).OrderBy(d => d.StartTime).ToList();
                    DateTime currentStartTime = part.StartSetupTime;
                    foreach (var downtime in sortedDowntimes)
                    {
                        if (currentStartTime < downtime.StartTime)
                        {
                            part.DownTimes.Add(new DownTime(part, DownTime.Types.PartialSetup, currentStartTime, downtime.StartTime));
                        }
                        currentStartTime = downtime.EndTime;
                    }
                    if (currentStartTime < part.StartMachiningTime)
                    {
                        part.DownTimes.Add(new DownTime(part, DownTime.Types.PartialSetup, currentStartTime, part.StartMachiningTime));
                    }
                }
                else if (part.DownTimes.Count == 0)
                {
                    part.DownTimes.Add(new DownTime(part, DownTime.Types.PartialSetup, part.StartSetupTime, part.StartMachiningTime));
                }

                //part.DownTimes = new DeepObservableCollection<DownTime>(part.DownTimes.Where(x => x.Relation == DownTime.Relations.Machining))
                //{
                //    new DownTime(part, DownTime.Types.PartialSetup)
                //    {

                //        StartTimeText = part.StartSetupTime.ToString(Constants.DateTimeFormat),
                //        EndTimeText = part.StartMachiningTime.ToString(Constants.DateTimeFormat)
                //    }
                //};
            }
            return partial;
        }


        /// <summary>
        /// Запись изготовления в Excel таблицу
        /// </summary>
        /// <param name="part">Информация об изготовлении</param>
        /// <returns>Int32 - Id записи в таблице</returns>
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
                        // номер 
                        xlRow.Cell(1).Value = id;
                        // выработка по наладке
                        xlRow.Cell(2).FormulaR1C1 = prevRow.Cell(2).FormulaR1C1;
                        // выработка штучная 1С
                        xlRow.Cell(3).FormulaR1C1 = prevRow.Cell(3).FormulaR1C1;
                        // выработка штучная м/в + 2м
                        xlRow.Cell(4).FormulaR1C1 = prevRow.Cell(4).FormulaR1C1;
                        // комментарий оператора + отчет по простоям
                        xlRow.Cell(5).Value = $"{part.OperatorComments}\n{combinedDownTimes.Report()}".Trim();
                        // если время завершения раньше 07:10, то отнимаем сутки для корректности отчетов
                        var needDiscrease = part.Shift == NightShift && part.EndMachiningTime < new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddHours(8);
                        // дата смены
                        xlRow.Cell(6).Value = needDiscrease
                            ? new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddDays(-1)
                            : new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day);
                        // станок
                        xlRow.Cell(7).Value = AppSettings.Instance.Machine.Name;
                        // смена день/ночь
                        xlRow.Cell(8).Value = part.Shift;
                        // оператор
                        xlRow.Cell(9).Value = part.Operator.FullName.Trim();
                        // деталь
                        xlRow.Cell(10).Value = part.FullName;
                        // номер м/л
                        xlRow.Cell(11).Value = part.Order;
                        // количетсво изготовлено
                        xlRow.Cell(12).Value = part.FinishedCount;
                        // номер установки
                        xlRow.Cell(13).Value = part.Setup;
                        // начало наладки
                        xlRow.Cell(14).Value = part.StartSetupTime.ToString("HH:mm");
                        // завешение наладки = начало изготовления
                        xlRow.Cell(15).Value = part.StartMachiningTime.ToString("HH:mm");
                        // фактическое фремя наладки
                        xlRow.Cell(16).Value = partial ? 0 : part.SetupTimeFact.ToString(@"hh\:mm");
                        // норматив на наладку
                        xlRow.Cell(17).Value = part.SetupTimePlan;
                        // фактическое время наладки в минутах
                        xlRow.Cell(18).FormulaR1C1 = prevRow.Cell(18).FormulaR1C1;
                        // разница план/факт наладки
                        xlRow.Cell(19).FormulaR1C1 = prevRow.Cell(19).FormulaR1C1;
                        // завешение наладки = начало изготовления
                        xlRow.Cell(20).Value = part.StartMachiningTime.ToString("HH:mm");
                        // завершение изготовления
                        xlRow.Cell(21).Value = part.EndMachiningTime.ToString("HH:mm");
                        // фактическое время изготовления
                        xlRow.Cell(22).Value = part.ProductionTimeFact.ToString(@"hh\:mm");
                        // норматив штучный
                        xlRow.Cell(23).Value = part.SingleProductionTimePlan;
                        // машинное время
                        xlRow.Cell(24).Value = Math.Round(part.MachineTime.TotalMinutes, 2);
                        // вычисления на 1 шт
                        for (var i = 25; i <= 33; i++)
                        {
                            xlRow.Cell(i).FormulaR1C1 = prevRow.Cell(i).FormulaR1C1;
                        }

                        var shiftTime = part.Shift == Text.DayShift ? 660 : 630;
                        // все простои в наладке кроме частичной
                        xlRow.Cell(34).Value = Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Setup, Type: not DownTime.Types.PartialSetup }).TotalMinutes(), 0);
                        // все простои в изготовлении
                        xlRow.Cell(35).Value = Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Machining }).TotalMinutes(), 0);
                        // guid
                        xlRow.Cell(36).Value = part.Guid.ToString();
                        var partialSetupTime = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.PartialSetup }).TotalMinutes(), 0);
                        // частичная наладка
                        xlRow.Cell(37).Value = partialSetupTime > shiftTime ? shiftTime : partialSetupTime;
                        // обслуживание
                        xlRow.Cell(38).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Maintenance }).TotalMinutes(), 0);
                        // поиск и получение инструмента
                        xlRow.Cell(39).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ToolSearching }).TotalMinutes(), 0);
                        // обучение и все такое
                        xlRow.Cell(40).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Mentoring }).TotalMinutes(), 0);
                        // обращение в другие службы
                        xlRow.Cell(41).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ContactingDepartments }).TotalMinutes(), 0);
                        // изготовление оснастки
                        xlRow.Cell(42).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.FixtureMaking }).TotalMinutes(), 0);
                        // отказ оборудования
                        xlRow.Cell(43).Value = Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.HardwareFailure }).TotalMinutes(), 0);
                        // если текущая деталь уже записывалась, то норматив наладки 0, чтобы они не суммировались при подсчете выработки
                        var partSetupTimePlanReport = prevPart != null && prevPart.Order == part.Order && prevPart.Setup == part.Setup ? 0 : part.SetupTimePlan;
                        if (partSetupTimePlanReport == 0 && part.SetupTimeFact.TotalMinutes > 0) partSetupTimePlanReport = part.SetupTimeFact.TotalMinutes;
                        if (partSetupTimePlanReport == 0 && part.SetupTimePlan == 0)
                        {
                            var partialTime = part.DownTimes.Where(x => x.Type == DownTime.Types.PartialSetup).TotalMinutes();
                            if (partialTime > 0) partSetupTimePlanReport = partialTime;
                        }
                        // норматив наладки для отчета
                        xlRow.Cell(44).Value = partSetupTimePlanReport;
                        // норматив наладки для исправления
                        xlRow.Cell(45).Value = part.SetupTimePlan;
                        // норматив изготовления для исправления
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
                WriteLog(outOfMemEx, $"Нехватка оперативной памяти при работе с XL файлом для записи детали {part.FullName} по заказу {part.Order}.\n\tОператор - {part.Operator.FullName}");
                Debug.Print("OutOfMemoryException");
                TryRestoreXl();
            }
            catch (ArgumentException argEx)
            {
                progress.Report("Ошибка");
                if (AppSettings.Instance.DebugMode) { WriteLog(argEx); }
                MessageBox.Show($"{argEx.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        xlRow.Cell(7).Value = AppSettings.Instance.Machine.Name;
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


        /// <summary>
        /// Получает директорию для копирования логов, которой является директория таблицы.
        /// </summary>
        /// <returns></returns>
        private static string GetCopyDir()
        {
            if (File.Exists(AppSettings.Instance.UpdatePath) && Directory.GetParent(AppSettings.Instance.UpdatePath) is { Exists: true } parent)
            {
                return parent.FullName;
            }
            return "";
        }

        /// <summary>
        /// Асинхронно получает директорию для копирования логов, которой является директория таблицы.
        /// </summary>
        /// <returns>Путь к директории или пустая строка, если директория не найдена.</returns>
        private static async Task<string> GetCopyDirAsync()
        {
            return await Task.Run(() =>
            {
                if (File.Exists(AppSettings.Instance.UpdatePath) && Directory.GetParent(AppSettings.Instance.UpdatePath) is { Exists: true } parent)
                {
                    return parent.FullName;
                }
                return "";
            });
        }

        /// <summary>
        /// Записывает информацию об исключении в лог.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="additionMessage"></param>
        public static void WriteLog(Exception exception, string additionMessage = "")
            => Task.Run(() => Logs.Write(AppSettings.LogFile, exception, additionMessage, GetCopyDirAsync().Result));


        /// <summary>
        /// Асинхронно записывает информацию об исключении в лог.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="additionMessage"></param>
        /// <returns></returns>
        public static async Task WriteLogAsync(Exception exception, string additionMessage = "")
        {
            string copyDir = await GetCopyDirAsync();
            await Logs.Write(AppSettings.LogFile, exception, additionMessage, copyDir);
        }

        /// <summary>
        /// Записывает сообщение в лог.
        /// </summary>
        /// <param name="message"></param>
        public static void WriteLog(string message)
            => Task.Run(() => Logs.Write(AppSettings.LogFile, message, GetCopyDir()));

        /// <summary>
        /// Асинхронно записывает сообщение в лог.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="additionMessage"></param>
        /// <returns></returns>
        public static async Task WriteLogAsync(string message)
        {
            string copyDir = await GetCopyDirAsync();
            await Logs.Write(AppSettings.LogFile, message, copyDir);
        }


        /// <summary>
        /// Записывает информацию о детали в лог.
        /// </summary>
        /// <param name="part"></param>
        /// <param name="message"></param>
        public static void WriteLog(Part part, string message)
        {
            var content = $"[{DateTime.Now.ToString(Constants.DateTimeWithSecsFormat)}]: {message}\n\t" +
                        $"Оператор: {AppSettings.Instance.CurrentOperator?.DisplayName}\n\t" +
                        $"Деталь №{part.Id}: {part.Name} | {part.Setup} уст.\n\t" +
                        $"М/Л: {part.Order} | {part.TotalCountInfo}\n\t" +
                        $"GUID: {part.Guid}\n\n";
            Task.Run(() => Logs.Write(AppSettings.LogFile, content, GetCopyDir()));
        }


        /// <summary>
        /// Список простоев с временами.
        /// </summary>
        /// <param name="downTimes"></param>
        /// <returns></returns>
        public static string Report(this IEnumerable<DownTime> downTimes) => downTimes.Any()
            ? downTimes.Aggregate("Отмеченные простои:\n", (current, downTime) => current + $"{downTime.Name}: {Math.Round(downTime.Time.TotalMinutes, 1)} м\n")
            : string.Empty;

        public static string Report(this IEnumerable<CombinedDownTime> downTimes) => downTimes.Any()
            ? downTimes.Aggregate("Отмеченные простои:\n", (current, downTime) => current + $"{downTime.Description}")
            : string.Empty;

        public static double TotalMinutes(this IEnumerable<DownTime> downTimes) =>
            downTimes.Aggregate(0.0, (sum, downTime) => sum + downTime.Time.TotalMinutes);

        public static double TotalMinutes(this IEnumerable<CombinedDownTime> downTimes) =>
            downTimes.Aggregate(0.0, (sum, downTime) => sum + downTime.Time.TotalMinutes);

        public static IEnumerable<CombinedDownTime> Combine(this ICollection<DownTime> downTimes)
        {
            if (downTimes == null || downTimes.Count < 1)
            {
                return new List<CombinedDownTime>();
            }

            var groupedDownTimes = downTimes.GroupBy(x => new { x.Type, x.Relation });

            return groupedDownTimes.Select(group =>
            {
                var combinedDownTime = new CombinedDownTime(group.ToList())
                {
                    Type = group.Key.Type,
                    Relation = group.Key.Relation
                };
                return combinedDownTime;
            });
        }

        /// <summary>
        /// Пытается распарсить строку в int. 
        /// Пустая или содержащая только пробелы строка интерпретируется как 0.
        /// </summary>
        /// <param name="s">Строка для парсинга</param>
        /// <param name="value">Результат парсинга</param>
        /// <returns>true, если строка пустая или корректное число; false при некорректном формате</returns>
        public static bool TryParseEmptyAsZero(this string? s, out int value)
        {
            s ??= string.Empty;
            s = s.Replace("0", "").Replace(" ", "");
            if (string.IsNullOrWhiteSpace(s))
            {
                value = 0;
                return true;
            }

            return int.TryParse(s, out value);
        }


        public static DateTime GetStartShiftTime()
        {
            return AppSettings.Instance.CurrentShift == Text.DayShift ? DateTime.Today.AddHours(7) :
                DateTime.Now.Hour < 7 ? DateTime.Today.AddDays(-1).AddHours(19) : DateTime.Today.AddHours(19);
        }

        public static DateTime GetEndShiftTime()
        {
            return AppSettings.Instance.CurrentShift == Text.DayShift
                ? DateTime.Today.AddHours(19)
                : DateTime.Now.Hour < 8
                    ? DateTime.Today.AddHours(7)
                    : DateTime.Today.AddDays(1).AddHours(7);
        }

        public static void AddDownTime(this Part part, DownTime.Types type)
        {
            part.DownTimes.Add(new DownTime(part, type));
        }

        /// <summary>
        /// Получает список получателей электронной почты определенной секции из локального файла. 
        /// Если удаленный файл новее локального, локальный файл обновляется.
        /// В случае ошибки при работе с удаленным файлом, используется локальный файл, если он существует.
        /// Если локальный файл пуст, не содержит указанной секции или его нет, возвращается пустой список.
        /// Любые исключения логируются, и также возвращается пустой список.
        /// </summary>
        /// <param name="receiversType">Тип получателей (секция) для чтения из файла</param>
        /// <returns>Список строк, содержащий адреса получателей электронной почты для указанной секции. 
        /// Возвращает пустой список, если файл пустой, секция не найдена или возникла ошибка.</returns>
        public static List<string> GetMailReceivers(ReceiversType receiversType)
        {
            try
            {
                UpdateLocalFileIfNeeded();
                return Utils.ReadReceiversFromFile(receiversType, AppSettings.LocalMailRecieversFile);
            }
            catch (Exception ex)
            {
                WriteLog(ex);
                return new List<string>();
            }
        }

        private static void UpdateLocalFileIfNeeded()
        {
            try
            {
                if (AppSettings.LocalMailRecieversFile == null || AppSettings.Instance.PathToRecievers == null) return;
                if (!File.Exists(AppSettings.Instance.PathToRecievers)) return;
                if (!File.Exists(AppSettings.LocalMailRecieversFile) ||
                    AppSettings.Instance.PathToRecievers.IsFileNewerThan(AppSettings.LocalMailRecieversFile))
                {
                    File.Copy(AppSettings.Instance.PathToRecievers, AppSettings.LocalMailRecieversFile, true);
                }
            }
            catch (Exception ex)
            {
                WriteLog(ex);
            }
        }        
    }
}