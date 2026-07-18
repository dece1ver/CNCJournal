using libeLog.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace libeLog.Infrastructure
{
    public static class Logs
    {
        private static SemaphoreSlim semaphore = new SemaphoreSlim(1);

        /// <summary>
        /// Проверяет размер файла логов, если размер превышает допустимый, то он уходит в бэкап.
        /// </summary>
        /// <param name="path"></param>
        public static void CheckLogSize(string path, string copyDir = "")
        {
            if (new FileInfo(path).Length > Constants.MaxLogSize)
            {
                var backupLog = path + $".bk{DateTime.Now:ddMMyy}";
                File.Move(path, backupLog);
            }
        }

        /// <summary>
        /// Записывает информацию об исключении в лог.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="additionMessage"></param>
        public static async Task Write(string path, Exception exception, string additionMessage = "", string copyDir = "")
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) File.Create(path).Dispose();

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        CheckLogSize(path);
                        await File.AppendAllTextAsync(path, $"[{DateTime.Now.ToString(Constants.DateTimeWithSecsFormat)} v{GetVersion()}]: " +
                                                                $"{(string.IsNullOrEmpty(additionMessage) ? string.Empty : $"{additionMessage}\n")}" +
                                                                $"{exception.Message}{(exception.TargetSite is null ? string.Empty : $"\n\tCaller: {exception.TargetSite}")}\n" +
                                                                $"{exception.GetType()}\n" +
                                                                $"{exception.StackTrace}\n\n");
                        if (!string.IsNullOrWhiteSpace(copyDir))
                            await Task.Run(() => TryCopyLog(path, copyDir));
                        return;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                catch (Exception e)
                {
                    if (i == 2)
                    {
                        MessageBoxWindow.Show($"Отправь этот текст разработчику:\n{e.GetBaseException()}", $"{e.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    await Task.Delay(1000);
                }
            }
        }

        public static string GetVersion() {
            try
            {
                var exe = Environment.ProcessPath;
                var date = exe is null ? string.Empty : $" от {File.GetLastWriteTime(exe).ToString(Constants.DateTimeFormat)}";
                var ver = Assembly.GetExecutingAssembly().GetName().Version!;
                return $"{ver.Major}.{ver.Minor}.{ver.Build}{date}";
            }
            catch { return ":N/A"; }
        }

        /// <summary>
        /// Записывает сообщение в лог.
        /// </summary>
        /// <param name="message">Сообщение</param>
        /// <param name="copyDir">Папка для копирования логов</param>
        public static async Task Write(string path, string message, string copyDir = "")
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await semaphore.WaitAsync();
                    if (File.Exists(path)) CheckLogSize(path);
                    await File.AppendAllTextAsync(path, $"[{DateTime.Now.ToString(Constants.DateTimeWithSecsFormat)} v{GetVersion()}]: {message}\n\n");
                    if (!string.IsNullOrWhiteSpace(copyDir)) await Task.Run(() => TryCopyLog(path, copyDir));
                    semaphore.Release();
                    return;
                }
                catch (Exception e)
                {
                    if (i == 2)
                    {
                        MessageBoxWindow.Show($"Покажи этот текст разработчику:\n{e.GetBaseException()}", $"{e.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
                        semaphore.Release();
                        return;
                    }
                    Thread.Sleep(1000);
                }
            }
        }


        public static void TryCopyLog(string src, string dest)
        {
            try
            {
                if (!Directory.Exists(dest)) return;
                var logsPath = Path.Combine(dest, "logs");
                if (!Directory.Exists(logsPath)) { Directory.CreateDirectory(logsPath); }
                File.Copy(src, Path.Combine(logsPath, $"{Environment.UserName}.log"), true);
            }
            catch { }
        }

        public static void TryCopyBackup(string src, string dest)
        {
            try
            {
                if (!Directory.Exists(dest)) return;
                var logsPath = Path.Combine(dest, "logs");
                if (!Directory.Exists(logsPath)) { Directory.CreateDirectory(logsPath); }
                File.Copy(src, Path.Combine(logsPath, $"{Environment.UserName}.log.bk{DateTime.Now:ddMMyy}"), true);
            }
            catch { }
        }
    }
}
