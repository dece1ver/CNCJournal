using libeLog.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
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
        /// Флаг, что ошибку записи в лог уже показывали оператору.
        /// </summary>
        private static int _errorShown;

        /// <summary>
        /// Флаг, что переход на запасной лог уже показывали оператору.
        /// </summary>
        private static int _fallbackNotified;

        /// <summary>
        /// Короткий текст предупреждения о запасном логе для статусбара
        /// (null — общий лог доступен, показывать нечего).
        /// </summary>
        public static string? FallbackWarning { get; private set; }

        /// <summary>Путь запасного лога для тултипа строки статуса.</summary>
        public static string? FallbackPath { get; private set; }

        /// <summary>
        /// Поднято один раз за сеанс при переходе на запасной лог.
        /// Подписчик сам маршалит на UI-поток; ранние срабатывания
        /// (до создания главного окна) видны через <see cref="FallbackWarning"/>.
        /// </summary>
        public static event Action? FallbackWarningChanged;

        /// <summary>Пути, для которых ACL уже прогревали в этом сеансе.</summary>
        private static readonly HashSet<string> _aclWarmedPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _aclWarmLock = new();

        /// <summary>
        /// Как часто дописывать напоминание, пока сервер недоступен (первый обрыв пишется подробно).
        /// </summary>
        private static readonly TimeSpan ConnectionReportInterval = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Число неудачных обращений к серверу с момента обрыва (0 — связи нет проблем).
        /// Читается и пишется только под семафором.
        /// </summary>
        private static int _connectionErrors;

        /// <summary>Момент начала текущего обрыва.</summary>
        private static DateTime _connectionSince;

        /// <summary>Момент последней подробной записи об обрыве.</summary>
        private static DateTime _connectionLastReport;

        /// <summary>
        /// Проверяет размер файла логов, если размер превышает допустимый, то он уходит в бэкап.
        /// Сбой самой ротации не должен мешать записи в лог, поэтому ошибки гасим.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="copyDir">Папка для копирования бэкапа</param>
        public static void CheckLogSize(string path, string copyDir = "")
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length <= Constants.MaxLogSize) return;

                var backupLog = GetFreeBackupName(path);
                File.Move(path, backupLog, true);
                RemoveOldBackups(path);
                if (!string.IsNullOrWhiteSpace(copyDir)) TryCopyBackup(backupLog, copyDir);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Не удалось переименовать файл лога '{path}'. {e}");
            }
        }

        /// <summary>
        /// Подбирает незанятое имя бэкапа: log.bkГГГГММДД_ЧЧммсс, log.bkГГГГММДД_ЧЧммсс_1 и т.д.
        /// В имени дата и время, иначе вторая ротация за сутки падала бы на уже существующий файл.
        /// </summary>
        private static string GetFreeBackupName(string path)
        {
            var name = $"{path}.bk{DateTime.Now:yyyyMMdd_HHmmss}";
            if (!File.Exists(name)) return name;
            for (int i = 1; i < 100; i++)
            {
                var candidate = $"{name}_{i}";
                if (!File.Exists(candidate)) return candidate;
            }
            return $"{name}_{Guid.NewGuid():N}";
        }

        /// <summary>
        /// Удаляет бэкапы файла лога старше Constants.LogBackupsToKeep.
        /// </summary>
        private static void RemoveOldBackups(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return;
                var backups = Directory.GetFiles(dir, $"{Path.GetFileName(path)}.bk*")
                                       .OrderByDescending(File.GetLastWriteTimeUtc)
                                       .Skip(Constants.LogBackupsToKeep);
                foreach (var backup in backups)
                {
                    try { File.Delete(backup); }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Не удалось удалить старые бэкапы файла лога '{path}'. {e}");
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
            if (DbErrors.IsConnectionError(exception))
            {
                await WriteConnectionError(path, exception, additionMessage, copyDir);
                return;
            }
            await AppendAsync(path, Stamp() +
                                $"{(string.IsNullOrEmpty(additionMessage) ? string.Empty : $"{additionMessage}\n")}" +
                                $"{exception.Message}{(exception.TargetSite is null ? string.Empty : $"\n\tCaller: {exception.TargetSite}")}\n" +
                                $"{exception.GetType()}\n" +
                                $"{exception.StackTrace}\n\n", copyDir);
        }

        /// <summary>
        /// Пишет потерю связи с сервером: подробно — только для первого обрыва, дальше короткой
        /// строкой со счётчиком. Завершает запись <see cref="ConnectionOk"/>.
        /// </summary>
        private static async Task WriteConnectionError(string path, Exception exception, string additionMessage, string copyDir)
        {
            try
            {
                await semaphore.WaitAsync();
                try
                {
                    var first = _connectionErrors == 0;
                    var report = first || DateTime.Now - _connectionLastReport >= ConnectionReportInterval;
                    if (first) _connectionSince = DateTime.Now;
                    _connectionErrors++;
                    if (report) _connectionLastReport = DateTime.Now;

                    var text = report
                        ? Stamp() +
                          $"Нет связи с сервером{DescribeContext(additionMessage)}" +
                          $"{(first ? string.Empty : $"\n\tНеудачных обращений: {_connectionErrors}, прошло: {DateTime.Now - _connectionSince:mm\\:ss}")}\n" +
                          $"{exception.Message}{(exception.TargetSite is null ? string.Empty : $"\n\tCaller: {exception.TargetSite}")}\n" +
                          $"{exception.GetType()}\n{exception.StackTrace}\n\n"
                        : Stamp() + $"Нет связи с сервером. Неудачных обращений: {_connectionErrors}, прошло: {DateTime.Now - _connectionSince:mm\\:ss}\n\n";
                    await AppendLockedAsync(path, text, copyDir);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception e)
            {
                ReportWriteError(e);
            }
            if (!string.IsNullOrWhiteSpace(copyDir)) TryCopyLog(path, copyDir);
        }

        /// <summary>
        /// Сообщает, что обращение к серверу прошло успешно. Если до этого был обрыв,
        /// в лог пишется одно сообщение о восстановлении. Вызывать из кода, который ходит в БД.
        /// </summary>
        /// <param name="path">Файл лога</param>
        /// <param name="copyDir">Папка для копирования логов</param>
        public static async Task ConnectionOk(string path, string copyDir = "")
        {
            if (string.IsNullOrEmpty(path)) return;
            var written = false;
            try
            {
                await semaphore.WaitAsync();
                try
                {
                    if (_connectionErrors == 0) return;
                    var duration = DateTime.Now - _connectionSince;
                    var errors = _connectionErrors;
                    _connectionErrors = 0;
                    _connectionLastReport = default;
                    await AppendLockedAsync(path, Stamp() +
                        $"Связь с сервером восстановлена через {duration.TotalMinutes:F1} мин. Неудачных обращений: {errors}.\n\n", copyDir);
                    written = true;
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception e)
            {
                ReportWriteError(e);
            }
            // Вызывается из фоновых циклов каждые несколько секунд, поэтому копируем лог
            // в общую папку только когда действительно что-то записали.
            if (written && !string.IsNullOrWhiteSpace(copyDir)) TryCopyLog(path, copyDir);
        }

        private static string DescribeContext(string additionMessage)
            => string.IsNullOrEmpty(additionMessage) ? "." : $". {additionMessage}";

        private static string Stamp() => $"[{DateTime.Now.ToString(Constants.DateTimeWithSecsFormat)} v{GetVersion()}]: ";

        /// <summary>
        /// Дописывает текст в файл лога под семафором и копирует лог в общую папку.
        /// </summary>
        private static async Task AppendAsync(string path, string text, string copyDir)
        {
            try
            {
                await semaphore.WaitAsync();
                try
                {
                    await AppendLockedAsync(path, text, copyDir);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception e)
            {
                ReportWriteError(e);
            }
            if (!string.IsNullOrWhiteSpace(copyDir)) TryCopyLog(path, copyDir);
        }

        /// <summary>
        /// Дописывает текст в файл лога. Семафор должен быть уже захвачен.
        /// При недоступности основного пути (нет прав, путь занят папкой)
        /// пишет в запасной лог в %LOCALAPPDATA%, работа программы продолжается.
        /// </summary>
        private static async Task AppendLockedAsync(string path, string text, string copyDir)
        {
            try
            {
                await WritePrimaryAsync(path, text, copyDir);
                return;
            }
            catch (UnauthorizedAccessException primaryEx)
            {
                // Общий файл создан другим юзером/админом без прав для остальных —
                // пробуем починить ACL (получится только с правами) и повторить раз.
                if (TryHealAcl(path))
                {
                    try
                    {
                        await WritePrimaryAsync(path, text, copyDir);
                        return;
                    }
                    catch { }
                }
                await WriteFallbackAsync(path, text, copyDir, primaryEx);
                return;
            }
            catch (IOException primaryEx)
            {
                // Путь занят папкой — сразу в fallback, без ретрая.
                if (Directory.Exists(path))
                {
                    await WriteFallbackAsync(path, text, copyDir, primaryEx);
                    return;
                }
                // Транзиент (второй юзер крутит ротацию, антивирус держит файл):
                // одна короткая пауза и повтор, иначе запись уходит в fallback.
                try
                {
                    await Task.Delay(150);
                    await WritePrimaryAsync(path, text, copyDir);
                    return;
                }
                catch { }
                await WriteFallbackAsync(path, text, copyDir, primaryEx);
                return;
            }
            catch (Exception primaryEx)
            {
                await WriteFallbackAsync(path, text, copyDir, primaryEx);
                return;
            }
        }

        private static async Task WritePrimaryAsync(string path, string text, string copyDir)
        {
            if (Directory.Exists(path))
                throw new IOException($"Путь лога '{path}' занят папкой.");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                TryGrantUsersModify(dir);
            }
            if (!File.Exists(path))
            {
                File.Create(path).Dispose();
                // Владелец может править DACL своего файла, поэтому сразу
                // расширяем права до общих — иначе второй юзер не допишет.
                TryGrantUsersModifyFile(path);
            }
            CheckLogSize(path, copyDir);
            await File.AppendAllTextAsync(path, text);
            // Успешная запись: один раз за сеанс дотягиваем Users:Modify
            // на папку и файл. Создатель чинит доступ остальным заранее,
            // до их первой ошибки — владелец на это прав имеет.
            WarmAclOnce(path);
        }

        private static async Task WriteFallbackAsync(string primaryPath, string text, string copyDir, Exception cause)
        {
            var fallback = GetFallbackPath(primaryPath);
            try
            {
                if (Directory.Exists(fallback)) fallback += ".log";
                var dir = Path.GetDirectoryName(fallback);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(fallback)) File.Create(fallback).Dispose();
                CheckLogSize(fallback, copyDir);
                await File.AppendAllTextAsync(fallback, text);
                if (!string.IsNullOrWhiteSpace(copyDir)) TryCopyLog(fallback, copyDir);
                ReportFallbackUsed(primaryPath, fallback, cause);
            }
            catch (Exception fallbackEx)
            {
                ReportWriteError(fallbackEx);
            }
        }

        private static string GetFallbackPath(string primaryPath)
        {
            try
            {
                var fileName = Path.GetFileName(primaryPath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "log";
                var parentName = Path.GetFileName(Path.GetDirectoryName(primaryPath) ?? "");
                if (string.IsNullOrWhiteSpace(parentName)) parentName = "logs";
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "dece1ver", parentName, fileName);
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "dece1ver-log");
            }
        }

        /// <summary>
        /// Создаёт каталог при необходимости и best-effort выдаёт Users Modify
        /// (наследование на содержимое). Без прав молча ничего не делает.
        /// </summary>
        public static void EnsureWritableDirectory(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) return;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                TryGrantUsersModify(dir);
            }
            catch { }
        }

        private static bool TryHealAcl(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    TryGrantUsersModify(dir);
                // Наследование с папки уже существующий чужой файл чинит не всегда,
                // поэтому правило дотягиваем и на сам файл напрямую.
                if (File.Exists(path))
                    TryGrantUsersModifyFile(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryGrantUsersModify(string dir)
        {
            try
            {
                var info = new DirectoryInfo(dir);
                var security = info.GetAccessControl();
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                security.AddAccessRule(new FileSystemAccessRule(users,
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                info.SetAccessControl(security);
            }
            catch { }
        }

        private static void TryGrantUsersModifyFile(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                var security = info.GetAccessControl();
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                security.AddAccessRule(new FileSystemAccessRule(users,
                    FileSystemRights.Modify,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
                info.SetAccessControl(security);
            }
            catch { }
        }

        /// <summary>
        /// Один раз за сеанс на путь дотягивает Users:Modify на папку и файл.
        /// Вызывать после успешной записи: создатель файла чинит доступ
        /// остальным заранее. Без прав молча ничего не делает.
        /// </summary>
        private static void WarmAclOnce(string path)
        {
            lock (_aclWarmLock)
            {
                if (!_aclWarmedPaths.Add(path)) return;
            }
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    TryGrantUsersModify(dir);
                if (File.Exists(path))
                    TryGrantUsersModifyFile(path);
            }
            catch { }
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
            if (string.IsNullOrEmpty(path)) return;
            await AppendAsync(path, Stamp() + $"{message}\n\n", copyDir);
        }

        /// <summary>
        /// Ошибку записи в лог показываем оператору только один раз за сеанс
        /// дружелюбным текстом без стека: при неисправном логе иначе каждая
        /// запись вызывала бы своё модальное окно. Стек уходит только в Debug
        /// и в запасной файл, оператору он не нужен.
        /// </summary>
        private static void ReportWriteError(Exception e)
        {
            Debug.WriteLine($"Не удалось записать в лог. {e}");
            if (Interlocked.Exchange(ref _errorShown, 1) == 0)
            {
                try
                {
                    MessageBoxWindow.Show("Не удалось записать в файл журнала. Работа программы продолжается.",
                        "Журнал недоступен", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
            }
        }

        /// <summary>
        /// Переход на запасной лог показываем разово строкой в статусбаре
        /// (подписка — в MainWindowViewModel), без модального окна.
        /// </summary>
        private static void ReportFallbackUsed(string primaryPath, string fallbackPath, Exception cause)
        {
            Debug.WriteLine($"Лог '{primaryPath}' недоступен ({cause.GetType().Name}: {cause.Message}). Пишем в '{fallbackPath}'.");
            if (Interlocked.Exchange(ref _fallbackNotified, 1) == 0)
            {
                FallbackWarning = "Журнал пишется во временный файл";
                FallbackPath = fallbackPath;
                try { FallbackWarningChanged?.Invoke(); }
                catch (Exception e) { Debug.WriteLine($"Подписчик FallbackWarningChanged упал. {e}"); }
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
