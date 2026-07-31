using libeLog.Infrastructure.Sql;
using Microsoft.Data.SqlClient;
using remeLog.Infrastructure.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.Data.Xml.Dom;
using libeLog.Views;
using Windows.UI.Notifications;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Сервис присутствия приложения и обмена командами между экземплярами.
    /// </summary>
    public sealed class AppPresenceService : IDisposable
    {
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
        private readonly string _applicationName = "remeLog";
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
        EnabledFeatures = @EnabledFeatures,
        IpAddress      = @IpAddress

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
        @DisplayName, 'Online', @AppVersion, @IpAddress, @EnabledFeatures,
        SYSUTCDATETIME(), SYSUTCDATETIME()
    );";

            await using var connection = CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = DbCommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("@SessionId", _sessionId);
            command.Parameters.AddWithValue("@Application", _applicationName);
            command.Parameters.AddWithValue("@MachineName", _machineName);
            command.Parameters.AddWithValue("@UserName", _userName);
            command.Parameters.AddWithValue("@DisplayName", _userName);
            command.Parameters.AddWithValue("@AppVersion", _version);
            command.Parameters.AddWithValue("@IpAddress", string.IsNullOrEmpty(_ipAddress) ? DBNull.Value : _ipAddress);
            // Именно фактическая маска, а не сырая AppSettings.EnabledFeatures: у админа она
            // пуста, хотя доступно всё, и окно инстансов показывало бы ему «—».
            command.Parameters.AddWithValue("@EnabledFeatures", (int)Util.EffectiveFeatures);

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
            const string sql = @"
SELECT Id, CommandType, Payload
FROM remeLog_app_commands
WHERE
    TargetMachine  = @MachineName
    AND ProcessedUtc IS NULL
ORDER BY CreatedUtc;";

            await using var connection = CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = DbCommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("@MachineName", _machineName);

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
                        await Application.Current.Dispatcher
                            .InvokeAsync(() => Application.Current.Shutdown())
                            .Task.ConfigureAwait(false);
                        result = "OK";
                        break;

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
                            await Application.Current.Dispatcher
                                .InvokeAsync(() => App.Current.Dispatcher.InvokeShutdown());
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
            const string AppId = "remeLog";

            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(
                    "<toast>" +
                    "<visual><binding template=\"ToastGeneric\">" +
                    "<text>remeLog</text>" +
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