using eLog.Infrastructure.Extensions;
using libeLog.Infrastructure.Sql;
using libeLog.Views;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace eLog.Infrastructure.Services
{
    /// <summary>
    /// Публикует присутствие экземпляра и выполняет команды, приходящие из окна экземпляров
    /// remeLog. Работает с теми же таблицами (remeLog_app_presence, remeLog_app_commands) и
    /// поддерживает тот же набор команд, что и remeLog.Infrastructure.AppPresenceService;
    /// отличаются идентификатор приложения и способ завершения процесса. Набор команд нужно
    /// править в обеих реализациях синхронно.
    /// </summary>
    public sealed class AppPresenceService : IDisposable
    {
        /// <summary>
        /// Значение колонок Application / TargetApplication. Должно совпадать
        /// с remeLog.Core.AppNames.ELog — eLog на remeLog.Core не ссылается.
        /// </summary>
        public const string ApplicationName = "eLog";

        /// <summary>Таймаут подключения к SQL-серверу (секунды).</summary>
        private const int DbConnectTimeoutSeconds = 5;

        /// <summary>Таймаут выполнения SQL-команды (секунды).</summary>
        private const int DbCommandTimeoutSeconds = 8;

        /// <summary>Интервал heartbeat в штатном режиме.</summary>
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

        /// <summary>Интервал heartbeat после ошибки (backoff).</summary>
        private static readonly TimeSpan HeartbeatBackoffInterval = TimeSpan.FromSeconds(30);

        /// <summary>Интервал опроса команд.</summary>
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

        /// <summary>Интервал опроса команд после ошибки (backoff).</summary>
        private static readonly TimeSpan PollingBackoffInterval = TimeSpan.FromSeconds(10);

        /// <summary>Интервал очистки устаревших записей.</summary>
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);

        private readonly string _connectionString;
        private readonly CancellationTokenSource _cts = new();

        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly string _machineName = Environment.MachineName;
        private readonly string _userName = Environment.UserName;
        private readonly string _version = App.CreateUniqueEventName();
        private readonly string _ipAddress;

        private DateTime _lastCleanupUtc = DateTime.MinValue;

        private Task? _heartbeatTask;
        private Task? _pollingTask;

        private bool _disposed;

        public AppPresenceService(string connectionString)
        {
            _connectionString = connectionString;
            try
            {
                _ipAddress = Dns.GetHostEntry(Dns.GetHostName())
                    .AddressList
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.ToString() ?? string.Empty;
            }
            catch
            {
                _ipAddress = string.Empty;
            }
        }

        /// <summary>Уникальный идентификатор текущего экземпляра приложения.</summary>
        public Guid SessionId => _sessionId;

        /// <summary>Запускает фоновые циклы heartbeat и polling.</summary>
        public void Start()
        {
            _heartbeatTask = Task.Run(HeartbeatLoopAsync);
            _pollingTask = Task.Run(CommandPollingLoopAsync);
        }

        /// <summary>Heartbeat-цикл: обновляет присутствие и периодически чистит устаревшие записи.</summary>
        private async Task HeartbeatLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await UpsertPresenceAsync(_cts.Token).ConfigureAwait(false);

                    if ((DateTime.UtcNow - _lastCleanupUtc) >= CleanupInterval)
                    {
                        _lastCleanupUtc = DateTime.UtcNow;
                        await CleanupAsync(_cts.Token).ConfigureAwait(false);
                    }

                    await Task.Delay(HeartbeatInterval, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex, "Ошибка heartbeat");

                    try
                    {
                        await Task.Delay(HeartbeatBackoffInterval, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>Цикл опроса входящих команд.</summary>
        private async Task CommandPollingLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await PollCommandsAsync(_cts.Token).ConfigureAwait(false);

                    await Task.Delay(PollingInterval, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex, "Ошибка polling команд");

                    try
                    {
                        await Task.Delay(PollingBackoffInterval, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>Обновляет (или вставляет) запись о присутствии клиента.</summary>
        private async Task UpsertPresenceAsync(CancellationToken ct)
        {
            const string sql = @"
MERGE remeLog_app_presence AS target
USING (SELECT @SessionId AS SessionId) AS source
ON target.SessionId = source.SessionId

WHEN MATCHED THEN
    UPDATE SET
        LastSeenUtc = SYSUTCDATETIME(),
        IpAddress   = @IpAddress

WHEN NOT MATCHED THEN
    INSERT
    (
        SessionId, Application, MachineName, UserName,
        DisplayName, Status, AppVersion, IpAddress, EnabledFeatures,
        StartedUtc, LastSeenUtc
    )
    VALUES
    (
        @SessionId, @Application, @MachineName, @UserName,
        @DisplayName, 'Online', @AppVersion, @IpAddress, 0,
        SYSUTCDATETIME(), SYSUTCDATETIME()
    );";

            await using var connection = CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = DbCommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("@SessionId", _sessionId);
            command.Parameters.AddWithValue("@Application", ApplicationName);
            command.Parameters.AddWithValue("@MachineName", _machineName);
            command.Parameters.AddWithValue("@UserName", _userName);
            command.Parameters.AddWithValue("@DisplayName", _userName);
            command.Parameters.AddWithValue("@AppVersion", _version);
            command.Parameters.AddWithValue("@IpAddress", string.IsNullOrEmpty(_ipAddress) ? DBNull.Value : _ipAddress);

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Удаляет устаревшие записи присутствия и команд.</summary>
        private async Task CleanupAsync(CancellationToken ct)
        {
            const string sql = @"
DELETE FROM remeLog_app_presence
WHERE LastSeenUtc < DATEADD(DAY, -2, GETUTCDATE());

DELETE FROM remeLog_app_commands
WHERE CreatedUtc < DATEADD(DAY, -7, GETUTCDATE());";

            await using var connection = CreateConnection();
            await SqlSchemaBootstrapper.ExecuteRawAsync(connection, sql).ConfigureAwait(false);
        }

        /// <summary>Читает необработанные команды и выполняет их.</summary>
        private async Task PollCommandsAsync(CancellationToken ct)
        {
            // Фильтр по TargetApplication обязателен: на той же машине очередь по тому же
            // TargetMachine опрашивает и remeLog.
            const string sql = @"
SELECT Id, CommandType, Payload
FROM remeLog_app_commands
WHERE
    TargetMachine = @MachineName
    AND TargetApplication = @Application
    AND ProcessedUtc IS NULL
ORDER BY CreatedUtc;";

            await using var connection = CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = DbCommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("@MachineName", _machineName);
            command.Parameters.AddWithValue("@Application", ApplicationName);

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            var commands = new List<(Guid Id, string Type, string Payload)>();

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                commands.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                ));
            }

            await reader.CloseAsync().ConfigureAwait(false);

            foreach (var item in commands)
            {
                ct.ThrowIfCancellationRequested();
                await HandleCommandAsync(connection, item, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Обрабатывает входящую команду.</summary>
        private async Task HandleCommandAsync(
            SqlConnection connection,
            (Guid Id, string Type, string Payload) cmd,
            CancellationToken ct)
        {
            string? result = null;

            try
            {
                switch (cmd.Type)
                {
                    case "Wake":
                        ShowToast(cmd.Payload);
                        result = "OK";
                        break;

                    case "ActivateWindow":
                        await Application.Current.Dispatcher
                            .InvokeAsync(ActivateMainWindow)
                            .Task.ConfigureAwait(false);
                        result = "OK";
                        break;

                    case "ForceClose":
                        // Результат пишется до выхода — после ForceExitAsync управление не вернётся,
                        // и команда осталась бы в очереди необработанной.
                        await MarkCommandProcessedAsync(connection, cmd.Id, "OK", ct).ConfigureAwait(false);
                        await ForceExitAsync().ConfigureAwait(false);
                        return;

                    case "ShowNotification":
                        // Немодально: см. комментарий у MessageBoxWindow.ShowNonModalAsync —
                        // модальный показ без гарантированного видимого owner блокировал
                        // всё приложение, если диалог открывался вне фокуса оператора.
                        await MessageBoxWindow.ShowNonModalAsync(cmd.Payload, "Уведомление",
                            MessageBoxButton.OK, MessageBoxImage.Information).ConfigureAwait(false);
                        result = "OK";
                        break;

                    case "UpdateNotification":
                        var updateAnswer = await MessageBoxWindow.ShowNonModalAsync(cmd.Payload,
                            "Доступно обновление электронного журнала",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question).ConfigureAwait(false);
                        if (updateAnswer == MessageBoxResult.Yes)
                        {
                            await MarkCommandProcessedAsync(connection, cmd.Id, "OK", ct).ConfigureAwait(false);
                            await ForceExitAsync().ConfigureAwait(false);
                            return;
                        }
                        result = "OK";
                        break;

                    case "CopyShortcut":
                        result = await CopyShortcutAsync(cmd.Payload, ct).ConfigureAwait(false);
                        break;

                    default:
                        Util.WriteLog($"AppPresenceService: неизвестный тип команды '{cmd.Type}'");
                        result = $"Неизвестная команда: {cmd.Type}";
                        break;
                }
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, $"Ошибка обработки команды {cmd.Id} ({cmd.Type})");
                result = $"Ошибка: {ex.Message}";
            }

            await MarkCommandProcessedAsync(connection, cmd.Id, result, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Завершает приложение в обход MainWindow.OnWindowClosing, сохранив настройки
        /// (обычно их сохраняет тот же обработчик).
        /// </summary>
        /// <remarks>
        /// Штатное закрытие при незавершённой смене или незакрытых деталях показывает модальный
        /// вопрос и по умолчанию отменяет выход. У станка отвечать на него некому, поэтому для
        /// команды принудительного закрытия этот путь не годится.
        /// </remarks>
        private static async Task ForceExitAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    AppSettings.Save();
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex, "Ошибка сохранения настроек перед принудительным закрытием");
                }

                Util.WriteLog("Приложение закрывается по команде из окна экземпляров");
                Environment.Exit(0);
            }).Task.ConfigureAwait(false);
        }

        /// <summary>Копирует файл ярлыка на рабочий стол текущего пользователя.</summary>
        private static async Task<string> CopyShortcutAsync(string filePath, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(filePath))
                    return $"Файл не найден: {filePath}";

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var fileName = Path.GetFileName(filePath);
                var dest = Path.Combine(desktop, fileName);

                File.Copy(filePath, dest, overwrite: true);
                return $"OK: {dest}";
            }, ct).ConfigureAwait(false);
        }

        /// <summary>Помечает команду обработанной и записывает результат.</summary>
        private static async Task MarkCommandProcessedAsync(
            SqlConnection connection,
            Guid commandId,
            string? result,
            CancellationToken ct)
        {
            const string sql = @"
UPDATE remeLog_app_commands
SET ProcessedUtc = SYSUTCDATETIME(),
    Result = @Result
WHERE Id = @Id;";

            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = DbCommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("@Id", commandId);
            command.Parameters.AddWithValue("@Result", (object?)result ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Создаёт <see cref="SqlConnection"/> с явным таймаутом подключения.</summary>
        private SqlConnection CreateConnection()
        {
            var builder = new SqlConnectionStringBuilder(_connectionString)
            {
                ConnectTimeout = DbConnectTimeoutSeconds
            };
            return new SqlConnection(builder.ConnectionString);
        }

        /// <summary>Активирует главное окно приложения.</summary>
        private static void ActivateMainWindow()
        {
            if (Application.Current.MainWindow is not Window window) return;

            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Show();
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }

        /// <summary>Показывает системное уведомление Windows.</summary>
        private static void ShowToast(string text)
        {
            const string AppId = "eLog";

            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(
                    "<toast>" +
                    "<visual><binding template=\"ToastGeneric\">" +
                    "<text>eLog</text>" +
                    "<text>" + System.Security.SecurityElement.Escape(text) + "</text>" +
                    "</binding></visual>" +
                    "</toast>");

                var toast = new ToastNotification(xml);
                ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "Ошибка показа Toast-уведомления");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts.Cancel();

            try
            {
                Task.WhenAll(
                        _heartbeatTask ?? Task.CompletedTask,
                        _pollingTask ?? Task.CompletedTask)
                    .Wait(TimeSpan.FromSeconds(5));
            }
            catch
            { }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}
