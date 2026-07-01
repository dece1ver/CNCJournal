using libeLog.Infrastructure;
using Microsoft.Data.SqlClient;
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
        StartedUtc,
        LastSeenUtc
    FROM
    (
        SELECT
            SessionId,
            MachineName,
            UserName,
            AppVersion,
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

            await using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
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
                    StartedLocal = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc).ToLocalTime(),
                    LastSeenLocal = DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc).ToLocalTime()
                });
            }

            return result;
        }

        public static async Task SendAppCommandAsync(
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

            try
            {
                await using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Id", Guid.NewGuid());
                command.Parameters.AddWithValue("@TargetSessionId", (object?)targetSessionId ?? DBNull.Value);
                command.Parameters.AddWithValue("@TargetApplication", "remeLog");
                command.Parameters.AddWithValue("@TargetMachine", targetMachine);
                command.Parameters.AddWithValue("@TargetUser", (object?)targetUser ?? DBNull.Value);
                command.Parameters.AddWithValue("@SenderMachine", Environment.MachineName);
                command.Parameters.AddWithValue("@SenderUser", Environment.UserName);
                command.Parameters.AddWithValue("@CommandType", commandType);
                command.Parameters.AddWithValue("@Payload", (object?)payload ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();

                Util.WriteLog($"Отправлена команда '{commandType}' на {targetMachine}" +
                    (targetUser is not null ? $"\\{targetUser}" : "") +
                    (payload is not null ? $": {payload}" : ""));
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, $"Ошибка отправки команды '{commandType}' на {targetMachine}");
                throw;
            }
        }

        public static async Task SendAppCommandToAllAsync(
            List<AppPresence> targets, string commandType, string? payload)
        {
            const string sql = @"
INSERT INTO remeLog_app_commands
    (Id, TargetSessionId, TargetApplication, TargetMachine, TargetUser,
     SenderMachine, SenderUser, CommandType, Payload, CreatedUtc)
VALUES
    (@Id, @TargetSessionId, @TargetApplication, @TargetMachine, @TargetUser,
     @SenderMachine, @SenderUser, @CommandType, @Payload, SYSUTCDATETIME());";

            try
            {
                await using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
                await connection.OpenAsync();
                await using var transaction = connection.BeginTransaction();

                foreach (var target in targets)
                {
                    await using var command = new SqlCommand(sql, connection, transaction);
                    command.Parameters.AddWithValue("@Id", Guid.NewGuid());
                    command.Parameters.AddWithValue("@TargetSessionId", (object?)target.SessionId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@TargetApplication", "remeLog");
                    command.Parameters.AddWithValue("@TargetMachine", target.MachineName);
                    command.Parameters.AddWithValue("@TargetUser", target.UserName);
                    command.Parameters.AddWithValue("@SenderMachine", Environment.MachineName);
                    command.Parameters.AddWithValue("@SenderUser", Environment.UserName);
                    command.Parameters.AddWithValue("@CommandType", commandType);
                    command.Parameters.AddWithValue("@Payload", (object?)payload ?? DBNull.Value);
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                Util.WriteLog($"Отправлена команда '{commandType}' на {targets.Count} экземпляров" +
                    (payload is not null ? $": {payload}" : ""));
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, $"Ошибка массовой отправки команды '{commandType}'");
                throw;
            }
        }

        public static async Task<int> GetPendingCommandCountAsync()
        {
            const string sql = "SELECT COUNT(*) FROM remeLog_app_commands WHERE ProcessedUtc IS NULL;";

            await using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (int)(await command.ExecuteScalarAsync())!;
        }
    }
}
