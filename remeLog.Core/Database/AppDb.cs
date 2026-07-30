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
        public static async Task<List<AppPresence>> ReadActiveInstancesAsync()
        {
            const string sql = @"
    SELECT
        SessionId,
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
            MachineName,
            UserName,
            AppVersion,
            IpAddress,
            EnabledFeatures,
            StartedUtc,
            LastSeenUtc,
            ROW_NUMBER() OVER (
                PARTITION BY MachineName, UserName
                ORDER BY LastSeenUtc DESC
            ) AS rn
        FROM remeLog_app_presence
        WHERE LastSeenUtc >= DATEADD(DAY, -1, GETUTCDATE())
    ) t
    WHERE rn = 1
    ORDER BY LastSeenUtc DESC;";

            var result = new List<AppPresence>();

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new AppPresence
                {
                    SessionId = reader.GetGuid(0),
                    MachineName = reader.GetString(1),
                    UserName = reader.GetString(2),
                    AppVersion = reader.GetString(3),
                    IpAddress = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    EnabledFeatures = reader.GetInt32(5),
                    StartedLocal = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc).ToLocalTime(),
                    LastSeenLocal = DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc).ToLocalTime()
                });
            }

            return result;
        }

        public static async Task<Guid> SendAppCommandAsync(
            Guid? targetSessionId, string targetMachine, string? targetUser,
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
                command.Parameters.AddWithValue("@TargetApplication", "remeLog");
                command.Parameters.AddWithValue("@TargetMachine", targetMachine);
                command.Parameters.AddWithValue("@TargetUser", (object?)targetUser ?? DBNull.Value);
                command.Parameters.AddWithValue("@SenderMachine", Environment.MachineName);
                command.Parameters.AddWithValue("@SenderUser", Environment.UserName);
                command.Parameters.AddWithValue("@CommandType", commandType);
                command.Parameters.AddWithValue("@Payload", (object?)payload ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();

                Log.Write($"Отправлена команда '{commandType}' на {targetMachine}" +
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
                    command.Parameters.AddWithValue("@TargetApplication", "remeLog");
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

        public static async Task<int> GetPendingCommandCountAsync()
        {
            const string sql = "SELECT COUNT(*) FROM remeLog_app_commands WHERE ProcessedUtc IS NULL;";

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        public static async Task<List<(Guid Id, string CommandType, string TargetMachine,
            string TargetUser, string Payload, DateTime CreatedUtc)>> GetPendingCommandsAsync()
        {
            const string sql = @"
SELECT Id, CommandType, TargetMachine, TargetUser, Payload, CreatedUtc
FROM remeLog_app_commands
WHERE ProcessedUtc IS NULL
ORDER BY CreatedUtc;";

            var result = new List<(Guid, string, string, string, string, DateTime)>();

            await using var connection = new SqlConnection(DomainSettings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                    DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc).ToLocalTime()
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
    }
}
