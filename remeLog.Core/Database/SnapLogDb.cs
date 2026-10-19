using Microsoft.Data.SqlClient;
using remeLog.Core;
using remeLog.Core.Db;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        /// <summary>
        /// История слепок времён (аудит кнопки «Слепить детали»).
        /// В демо никуда не пишется.
        /// </summary>
        public static async Task<DbResult<bool>> SavePartSnapLogsAsync(IReadOnlyList<PartSnapLog> logs)
        {
            if (DomainSettings.DemoMode) return await Task.FromResult(DbResult<bool>.Ok(true));
            const string sql = @"
                INSERT INTO part_snap_log
                    (Machine, ShiftDate, Shift, PrevPartGuid, NextPartGuid, NextPartName,
                     OldStartSetupTime, NewStartSetupTime, GapMinutes, SnappedBy, SnappedAt)
                VALUES
                    (@Machine, @ShiftDate, @Shift, @PrevPartGuid, @NextPartGuid, @NextPartName,
                     @OldStartSetupTime, @NewStartSetupTime, @GapMinutes, @SnappedBy, @SnappedAt)";
            try
            {
                await using var conn = new SqlConnection(DomainSettings.ConnectionString);
                await conn.OpenAsync();
                foreach (var log in logs)
                {
                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Machine", log.Machine);
                    cmd.Parameters.AddWithValue("@ShiftDate", log.ShiftDate.Date);
                    cmd.Parameters.AddWithValue("@Shift", log.Shift);
                    cmd.Parameters.AddWithValue("@PrevPartGuid", log.PrevPartGuid);
                    cmd.Parameters.AddWithValue("@NextPartGuid", log.NextPartGuid);
                    cmd.Parameters.AddWithValue("@NextPartName",
                        string.IsNullOrEmpty(log.NextPartName) ? DBNull.Value : log.NextPartName);
                    cmd.Parameters.AddWithValue("@OldStartSetupTime", log.OldStartSetupTime);
                    cmd.Parameters.AddWithValue("@NewStartSetupTime", log.NewStartSetupTime);
                    cmd.Parameters.AddWithValue("@GapMinutes", log.GapMinutes);
                    cmd.Parameters.AddWithValue("@SnappedBy", log.SnappedBy);
                    cmd.Parameters.AddWithValue("@SnappedAt", log.SnappedAt);
                    await cmd.ExecuteNonQueryAsync();
                }
                return DbResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Log.WriteError(ex, "SavePartSnapLogsAsync");
                return DbResult<bool>.FailWithError(ex.Message);
            }
        }
    }
}
