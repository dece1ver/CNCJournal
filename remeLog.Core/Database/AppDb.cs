using Microsoft.Data.SqlClient;
using remeLog.Core;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        /// <summary>
        /// Читает последние записи присутствия по всем приложениям (remeLog, eLog).
        /// </summary>
        /// <param name="application">
        /// Ограничить выборку одним приложением (<see cref="AppNames"/>); null — вернуть все.
        /// </param>
        public static async Task<List<AppPresence>> ReadActiveInstancesAsync(string? application = null)
        {
            // Application входит в партиционирование: на одной машине под одним пользователем
            // могут работать и eLog, и remeLog — это разные экземпляры, а не дубликаты.
            const string sql = @"
    SELECT
        SessionId,
        Application,
        MachineName,
        UserName,
        AppVersion,
        IpAddress,
        EnabledFeatures,
        StartedUtc,
        LastSeenUtc
    FROM
    (
        SELECT
            SessionId,
            Application,
            MachineName,
            UserName,
            AppVersion,
            IpAddress,
            EnabledFeatures,
            StartedUtc,
            LastSeenUtc,
            ROW_NUMBER() OVER (
                PARTITION BY Application, MachineName, UserName
                ORDER BY LastSeenUtc DESC
            ) AS rn
        FROM remeLog_app_presence
        WHERE LastSeenUtc >= DATEADD(DAY, -1, GETUTCDATE())
          AND (@Application IS NULL OR Application = @Application)
    ) t
    WHERE rn = 1
    ORDER BY LastSeenUtc DESC;";

            var result = new List<AppPresence>();

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Application", (object?)application ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new AppPresence
                {
                    SessionId = reader.GetGuid(0),
                    Application = reader.IsDBNull(1) ? AppNames.RemeLog : reader.GetString(1),
                    MachineName = reader.GetString(2),
                    UserName = reader.GetString(3),
                    AppVersion = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    IpAddress = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    EnabledFeatures = reader.GetInt32(6),
                    StartedLocal = DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc).ToLocalTime(),
                    LastSeenLocal = DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc).ToLocalTime()
                });
            }

            return result;
        }

        /// <summary>
        /// Ставит команду в очередь конкретному экземпляру.
        /// </summary>
        /// <param name="targetApplication">
        /// Приложение-получатель (<see cref="AppNames"/>). Задавать обязательно: очередь на одной
        /// машине опрашивают и eLog, и remeLog, и без этого поля команда достанется не тому.
        /// </param>
        public static async Task<Guid> SendAppCommandAsync(
            Guid? targetSessionId, string targetApplication, string targetMachine, string? targetUser,
            string commandType, string? payload)
        {
            const string sql = @"
INSERT INTO remeLog_app_commands
    (Id, TargetSessionId, TargetApplication, TargetMachine, TargetUser,
     SenderMachine, SenderUser, CommandType, Payload, CreatedUtc)
VALUES
    (@Id, @TargetSessionId, @TargetApplication, @TargetMachine, @TargetUser,
     @SenderMachine, @SenderUser, @CommandType, @Payload, SYSUTCDATETIME());";

            var id = Guid.NewGuid();
            try
            {
                await using var connection = new SqlConnection(DomainSettings.ConnectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Id", id);
                command.Parameters.AddWithValue("@TargetSessionId", (object?)targetSessionId ?? DBNull.Value);
                command.Parameters.AddWithValue("@TargetApplication", targetApplication);
                command.Parameters.AddWithValue("@TargetMachine", targetMachine);
                command.Parameters.AddWithValue("@TargetUser", (object?)targetUser ?? DBNull.Value);
                command.Parameters.AddWithValue("@SenderMachine", Environment.MachineName);
                command.Parameters.AddWithValue("@SenderUser", Environment.UserName);
                command.Parameters.AddWithValue("@CommandType", commandType);
                command.Parameters.AddWithValue("@Payload", (object?)payload ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();

                Log.Write($"Отправлена команда '{commandType}' ({targetApplication}) на {targetMachine}" +
                    (targetUser is not null ? $"\\{targetUser}" : "") +
                    (payload is not null ? $": {payload}" : ""));
            }
            catch (Exception ex)
            {
                Log.WriteError(ex,$"Ошибка отправки команды '{commandType}' на {targetMachine}");
                throw;
            }

            return id;
        }

        public static async Task<string?> GetCommandResultAsync(Guid commandId)
        {
            const string sql = @"
SELECT Result
FROM remeLog_app_commands
WHERE Id = @Id AND ProcessedUtc IS NOT NULL;";

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", commandId);
            var result = await command.ExecuteScalarAsync();
            return result as string;
        }

        public static async Task<List<Guid>> SendAppCommandToAllAsync(
            List<AppPresence> targets, string commandType, string? payload)
        {
            const string sql = @"
INSERT INTO remeLog_app_commands
    (Id, TargetSessionId, TargetApplication, TargetMachine, TargetUser,
     SenderMachine, SenderUser, CommandType, Payload, CreatedUtc)
VALUES
    (@Id, @TargetSessionId, @TargetApplication, @TargetMachine, @TargetUser,
     @SenderMachine, @SenderUser, @CommandType, @Payload, SYSUTCDATETIME());";

            var ids = new List<Guid>();
            try
            {
                await using var connection = new SqlConnection(DomainSettings.ConnectionString);
                await connection.OpenAsync();
                await using var transaction = connection.BeginTransaction();

                foreach (var target in targets)
                {
                    var id = Guid.NewGuid();
                    await using var command = new SqlCommand(sql, connection, transaction);
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@TargetSessionId", (object?)target.SessionId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@TargetApplication", target.Application);
                    command.Parameters.AddWithValue("@TargetMachine", target.MachineName);
                    command.Parameters.AddWithValue("@TargetUser", target.UserName);
                    command.Parameters.AddWithValue("@SenderMachine", Environment.MachineName);
                    command.Parameters.AddWithValue("@SenderUser", Environment.UserName);
                    command.Parameters.AddWithValue("@CommandType", commandType);
                    command.Parameters.AddWithValue("@Payload", (object?)payload ?? DBNull.Value);
                    await command.ExecuteNonQueryAsync();
                    ids.Add(id);
                }

                await transaction.CommitAsync();

                Log.Write($"Отправлена команда '{commandType}' на {targets.Count} экземпляров" +
                    (payload is not null ? $": {payload}" : ""));
            }
            catch (Exception ex)
            {
                Log.WriteError(ex,$"Ошибка массовой отправки команды '{commandType}'");
                throw;
            }

            return ids;
        }

        /// <param name="application">
        /// Считать только команды этому приложению (<see cref="AppNames"/>); null — все.
        /// </param>
        public static async Task<int> GetPendingCommandCountAsync(string? application = null)
        {
            const string sql = @"
SELECT COUNT(*) FROM remeLog_app_commands
WHERE ProcessedUtc IS NULL
  AND (@Application IS NULL OR TargetApplication = @Application);";

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Application", (object?)application ?? DBNull.Value);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        /// <param name="application">
        /// Вернуть только команды этому приложению (<see cref="AppNames"/>); null — все.
        /// </param>
        public static async Task<List<(Guid Id, string CommandType, string TargetApplication,
            string TargetMachine, string TargetUser, string Payload, DateTime CreatedUtc)>>
            GetPendingCommandsAsync(string? application = null)
        {
            const string sql = @"
SELECT Id, CommandType, TargetApplication, TargetMachine, TargetUser, Payload, CreatedUtc
FROM remeLog_app_commands
WHERE ProcessedUtc IS NULL
  AND (@Application IS NULL OR TargetApplication = @Application)
ORDER BY CreatedUtc;";

            var result = new List<(Guid, string, string, string, string, string, DateTime)>();

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Application", (object?)application ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? AppNames.RemeLog : reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                    reader.IsDBNull(5) ? "" : reader.GetString(5),
                    DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc).ToLocalTime()
                ));
            }

            return result;
        }

        public static async Task<bool> CancelPendingCommandAsync(Guid commandId)
        {
            const string sql = @"
UPDATE remeLog_app_commands
SET ProcessedUtc = SYSUTCDATETIME(),
    Result = 'Cancelled'
WHERE Id = @Id AND ProcessedUtc IS NULL;";

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", commandId);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        /// <summary>
        /// Пишет одно обращение к Windchill REST API в <c>remeLog_wnc_requests</c> — для
        /// диагностики нагрузки на сервере Windchill (сопоставить время жалоб пользователей на
        /// торможение/ошибки с фактическими запросами, см. <see cref="Models.WindchillClient"/>).
        /// "Кто" — Windows-логин/машина инициатора (<see cref="Environment.UserName"/>/
        /// <see cref="Environment.MachineName"/>), а не учётка Windchill: в Windchill все ходят
        /// под одним общим сервисным логином из <c>cnc_wnc_cfg</c>, там отдельных пользователей
        /// не различить.
        ///
        /// Ошибка записи лога не должна ронять сам поиск/скачивание — исключение гасится и
        /// уходит только в <see cref="Log.WriteError"/>.
        /// </summary>
        /// <param name="requests">
        /// Фактические HTTP-запросы к Windchill за это действие (по строке на запрос, см.
        /// <see cref="Models.WindchillClient"/>): одно действие пользователя — не всегда одно
        /// обращение к серверу. URL в них самодостаточны — вставив такой в браузер/Postman под
        /// сервисным логином из <c>cnc_wnc_cfg</c>, получаем ровно тот сырой ответ, который
        /// разбирал remeLog; это же можно передать техподдержке как воспроизводимый пример.
        /// </param>
        public static async Task LogWncRequestAsync(
            string requestType, string? paramsSummary, IEnumerable<string>? requests,
            int? resultCount, bool? truncated,
            bool success, string? errorMessage, long elapsedMs)
        {
            const string sql = @"
INSERT INTO remeLog_wnc_requests
    (MachineName, UserName, RequestType, Params, RequestUrls, ResultCount, Truncated, Success, ErrorMessage, ElapsedMs, CreatedUtc)
VALUES
    (@MachineName, @UserName, @RequestType, @Params, @RequestUrls, @ResultCount, @Truncated, @Success, @ErrorMessage, @ElapsedMs, SYSUTCDATETIME());";

            try
            {
                await using var connection = new SqlConnection(DomainSettings.ConnectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@MachineName", Environment.MachineName);
                command.Parameters.AddWithValue("@UserName", Environment.UserName);
                command.Parameters.AddWithValue("@RequestType", requestType);
                command.Parameters.AddWithValue("@Params", (object?)paramsSummary ?? DBNull.Value);
                var requestUrls = requests is null ? null : string.Join(Environment.NewLine, requests);
                command.Parameters.AddWithValue("@RequestUrls",
                    string.IsNullOrEmpty(requestUrls) ? DBNull.Value : requestUrls);
                command.Parameters.AddWithValue("@ResultCount", (object?)resultCount ?? DBNull.Value);
                command.Parameters.AddWithValue("@Truncated", (object?)truncated ?? DBNull.Value);
                command.Parameters.AddWithValue("@Success", success);
                command.Parameters.AddWithValue("@ErrorMessage", (object?)errorMessage ?? DBNull.Value);
                command.Parameters.AddWithValue("@ElapsedMs", elapsedMs);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.WriteError(ex, "Не удалось записать лог обращения к Windchill");
            }
        }
    }
}
