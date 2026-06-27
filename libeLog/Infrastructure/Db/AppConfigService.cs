using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace libeLog.Infrastructure.Db
{
    public static class AppConfigService
    {
        public static async Task<Dictionary<string, object?>> GetAllConfigAsync(string connectionString, string tableName)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            var row = await conn.QueryFirstOrDefaultAsync<IDictionary<string, object>>($"SELECT * FROM [{tableName}]");
            if (row is null)
                return new Dictionary<string, object?>();
            var result = new Dictionary<string, object?>(row.Count);
            foreach (var kvp in row)
                result[kvp.Key] = kvp.Value is DBNull ? null : kvp.Value;
            return result;
        }

        public static async Task<T?> GetConfigValueAsync<T>(string connectionString, string tableName, string column)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            return await conn.ExecuteScalarAsync<T>($"SELECT TOP 1 [{column}] FROM [{tableName}]");
        }
    }
}
