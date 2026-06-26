using ClosedXML.Excel;
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
using static eLog.Infrastructure.Text;

namespace eLog.Infrastructure.Extensions
{
    public static partial class Util
    {

        public enum WriteResult
        {
            Ok, IOError, NotFinded, Error, FileNotExist, DontNeed
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