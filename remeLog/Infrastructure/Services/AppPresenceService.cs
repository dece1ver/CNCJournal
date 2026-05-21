using libeLog.Infrastructure.Sql;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Сервис присутствия приложения и обмена командами между экземплярами.
    /// </summary>
    public sealed class AppPresenceService : IDisposable
    {
        private readonly string _connectionString;

        private readonly CancellationTokenSource _cts = new();

        private readonly Guid _sessionId = Guid.NewGuid();

        private readonly string _machineName = Environment.MachineName;

        private readonly string _userName = Environment.UserName;

        private readonly string _applicationName = "remeLog";

        private DateTime _lastCleanupUtc = DateTime.MinValue;

        private readonly string _version = App.CreateUniqueEventName();

        public AppPresenceService(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Уникальный идентификатор текущего экземпляра приложения.
        /// </summary>
        public Guid SessionId => _sessionId;

        /// <summary>
        /// Запускает фоновые циклы heartbeat и polling.
        /// </summary>
        public void Start()
        {
            Task.Run(HeartbeatLoopAsync);
            Task.Run(CommandPollingLoopAsync);
        }

        /// <summary>
        /// Основной heartbeat-цикл.
        /// </summary>
        private async Task HeartbeatLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await UpsertPresenceAsync();
                    if ((DateTime.UtcNow - _lastCleanupUtc).TotalMinutes >= 30)
                    {
                        _lastCleanupUtc = DateTime.UtcNow;
                        await CleanupAsync();
                    }
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex, "Ошибка heartbeat");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
            }
        }

        private async Task CleanupAsync()
        {
            const string sql = @"
                DELETE FROM remeLog_app_presence
                WHERE LastSeenUtc < DATEADD(DAY, -2, GETUTCDATE());

                DELETE FROM remeLog_app_commands
                WHERE CreatedUtc < DATEADD(DAY, -7, GETUTCDATE());
            ";

            await using var connection = new SqlConnection(_connectionString);
            await SqlSchemaBootstrapper.ExecuteRawAsync(connection, sql);
        }

        /// <summary>
        /// Основной polling-цикл команд.
        /// </summary>
        private async Task CommandPollingLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await PollCommandsAsync();
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex, "Ошибка polling команд");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), _cts.Token);
            }
        }

        /// <summary>
        /// Обновляет информацию о присутствии клиента.
        /// </summary>
        private async Task UpsertPresenceAsync()
        {
            const string sql = @"
MERGE remeLog_app_presence AS target
USING
(
    SELECT
        @SessionId      AS SessionId
) AS source
ON target.SessionId = source.SessionId

WHEN MATCHED THEN
    UPDATE SET
        LastSeenUtc = SYSUTCDATETIME()

WHEN NOT MATCHED THEN
    INSERT
    (
        SessionId,
        Application,
        MachineName,
        UserName,
        DisplayName,
        Status,
        AppVersion,
        StartedUtc,
        LastSeenUtc
    )
    VALUES
    (
        @SessionId,
        @Application,
        @MachineName,
        @UserName,
        @DisplayName,
        'Online',
        @AppVersion,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );";

            await using var connection = new SqlConnection(_connectionString);

            await connection.OpenAsync(_cts.Token);

            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@SessionId", _sessionId);

            command.Parameters.AddWithValue("@Application", _applicationName);

            command.Parameters.AddWithValue("@MachineName", _machineName);

            command.Parameters.AddWithValue("@UserName", _userName);

            command.Parameters.AddWithValue("@DisplayName", _userName);

            command.Parameters.AddWithValue("@AppVersion", _version);

            await command.ExecuteNonQueryAsync(_cts.Token);
        }

        /// <summary>
        /// Проверяет наличие новых команд.
        /// </summary>
        private async Task PollCommandsAsync()
        {
            const string sql = @"
SELECT
    Id,
    CommandType,
    Payload
FROM remeLog_app_commands
WHERE
    TargetMachine = @MachineName
    AND ProcessedUtc IS NULL
ORDER BY CreatedUtc;";

            await using var connection = new SqlConnection(_connectionString);

            await connection.OpenAsync(_cts.Token);

            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@MachineName", _machineName);

            await using var reader = await command.ExecuteReaderAsync(_cts.Token);

            var commands = new List<(Guid Id, string Type, string Payload)>();

            while (await reader.ReadAsync(_cts.Token))
            {
                commands.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2)
                ));
            }

            await reader.CloseAsync();

            foreach (var item in commands)
            {
                await HandleCommandAsync(connection, item);
            }
        }

        /// <summary>
        /// Обрабатывает входящую команду.
        /// </summary>
        private async Task HandleCommandAsync(
            SqlConnection connection,
            (Guid Id, string Type, string Payload) command)
        {
            try
            {
                switch (command.Type)
                {
                    case "Wake":
                        {
                            ShowToast(command.Payload);
                            break;
                        }

                    case "ActivateWindow":
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (Application.Current.MainWindow is Window window)
                                {
                                    if (window.WindowState == WindowState.Minimized)
                                        window.WindowState = WindowState.Normal;

                                    window.Show();

                                    window.Activate();

                                    window.Topmost = true;
                                    window.Topmost = false;

                                    window.Focus();
                                }
                            });

                            break;
                        }
                }

                await MarkCommandProcessedAsync(connection, command.Id);
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, $"Ошибка обработки команды {command.Id}");
            }
        }

        /// <summary>
        /// Помечает команду обработанной.
        /// </summary>
        private async Task MarkCommandProcessedAsync(
            SqlConnection connection,
            Guid commandId)
        {
            const string sql = @"
UPDATE remeLog_app_commands
SET ProcessedUtc = SYSUTCDATETIME()
WHERE Id = @Id;";

            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@Id", commandId);

            await command.ExecuteNonQueryAsync(_cts.Token);
        }

        /// <summary>
        /// Показывает системное уведомление Windows.
        /// </summary>
        private static void ShowToast(string text)
        {
            const string AppId = "remeLog";

            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast>" +
                "<visual>" +
                "<binding template=\"ToastGeneric\">" +
                "<text>remeLog</text>" +
                "<text>" + System.Security.SecurityElement.Escape(text) + "</text>" +
                "</binding>" +
                "</visual>" +
                "</toast>");

            var toast = new ToastNotification(xml);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
        }



        public void Dispose()
        {
            _cts.Cancel();
        }
    }
}