using Dapper;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace libeLog.Infrastructure.Db
{
    public static class DbHelper
    {
        public static SqlConnection OpenConnection(string connectionString)
        {
            var conn = new SqlConnection(connectionString);
            conn.Open();
            return conn;
        }

        public static async Task<SqlConnection> OpenConnectionAsync(string connectionString)
        {
            var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            return conn;
        }

        public static DbResult<T> QueryFirst<T>(string connectionString, string sql, object? parameters = null)
        {
            try
            {
                using var conn = OpenConnection(connectionString);
                var result = conn.QueryFirstOrDefault<T>(sql, parameters);
                return result is null
                    ? DbResult<T>.NotFound()
                    : DbResult<T>.Ok(result);
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    -1 => DbResult<T>.Fail(DbResult.NoConnection, sqlEx.Message),
                    18456 => DbResult<T>.Fail(DbResult.AuthError, sqlEx.Message),
                    _ => DbResult<T>.Fail(DbResult.Error, sqlEx.Message)
                };
            }
            catch (Exception ex)
            {
                return DbResult<T>.FailWithError(ex.Message);
            }
        }

        public static async Task<DbResult<T>> QueryFirstAsync<T>(string connectionString, string sql, object? parameters = null)
        {
            try
            {
                await using var conn = await OpenConnectionAsync(connectionString);
                var result = await conn.QueryFirstOrDefaultAsync<T>(sql, parameters);
                return result is null
                    ? DbResult<T>.NotFound()
                    : DbResult<T>.Ok(result);
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    -1 => DbResult<T>.Fail(DbResult.NoConnection, sqlEx.Message),
                    18456 => DbResult<T>.Fail(DbResult.AuthError, sqlEx.Message),
                    _ => DbResult<T>.Fail(DbResult.Error, sqlEx.Message)
                };
            }
            catch (Exception ex)
            {
                return DbResult<T>.FailWithError(ex.Message);
            }
        }

        public static DbResult<T> QuerySingle<T>(string connectionString, string sql, object? parameters = null)
        {
            try
            {
                using var conn = OpenConnection(connectionString);
                var result = conn.QuerySingleOrDefault<T>(sql, parameters);
                return result is null
                    ? DbResult<T>.NotFound()
                    : DbResult<T>.Ok(result);
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    -1 => DbResult<T>.Fail(DbResult.NoConnection, sqlEx.Message),
                    18456 => DbResult<T>.Fail(DbResult.AuthError, sqlEx.Message),
                    _ => DbResult<T>.Fail(DbResult.Error, sqlEx.Message)
                };
            }
            catch (Exception ex)
            {
                return DbResult<T>.FailWithError(ex.Message);
            }
        }

        public static async Task<DbResult<int>> ExecuteAsync(string connectionString, string sql, object? parameters = null)
        {
            try
            {
                await using var conn = await OpenConnectionAsync(connectionString);
                var rows = await conn.ExecuteAsync(sql, parameters);
                return DbResult<int>.Ok(rows);
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    -1 => DbResult<int>.Fail(DbResult.NoConnection, sqlEx.Message),
                    18456 => DbResult<int>.Fail(DbResult.AuthError, sqlEx.Message),
                    _ => DbResult<int>.Fail(DbResult.Error, sqlEx.Message)
                };
            }
            catch (Exception ex)
            {
                return DbResult<int>.FailWithError(ex.Message);
            }
        }

        public static async Task<DbResult<T>> ExecuteScalarAsync<T>(string connectionString, string sql, object? parameters = null)
        {
            try
            {
                await using var conn = await OpenConnectionAsync(connectionString);
                var result = await conn.ExecuteScalarAsync<T>(sql, parameters);
                return DbResult<T>.Ok(result);
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    -1 => DbResult<T>.Fail(DbResult.NoConnection, sqlEx.Message),
                    18456 => DbResult<T>.Fail(DbResult.AuthError, sqlEx.Message),
                    _ => DbResult<T>.Fail(DbResult.Error, sqlEx.Message)
                };
            }
            catch (Exception ex)
            {
                return DbResult<T>.FailWithError(ex.Message);
            }
        }

        public static DbResult<SqlConnection> TryOpenConnection(string connectionString, out string? error)
        {
            error = null;
            try
            {
                var conn = OpenConnection(connectionString);
                return DbResult<SqlConnection>.Ok(conn);
            }
            catch (SqlException sqlEx)
            {
                error = sqlEx.Message;
                return sqlEx.Number switch
                {
                    -1 => DbResult<SqlConnection>.Fail(DbResult.NoConnection, sqlEx.Message),
                    18456 => DbResult<SqlConnection>.Fail(DbResult.AuthError, sqlEx.Message),
                    _ => DbResult<SqlConnection>.Fail(DbResult.Error, sqlEx.Message)
                };
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return DbResult<SqlConnection>.FailWithError(ex.Message);
            }
        }
    }
}
