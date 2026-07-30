using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace remeLog.Core.Db
{
    /// <summary>
    /// Статус результата обращения к БД. Копия <c>libeLog.Models.DbResult</c> —
    /// не переиспользуем оригинал напрямую, чтобы remeLog.Core не тянул libeLog (WPF).
    /// </summary>
    public enum DbResult
    {
        Ok, AuthError, Error, NoConnection, NotFound
    }

    /// <summary>Результат обращения к БД с типизированным значением.</summary>
    public readonly struct DbResult<T>
    {
        public DbResult Status { get; }
        public T? Value { get; }
        public string? Error { get; }

        public bool IsOk => Status == DbResult.Ok;
        public bool IsError => Status != DbResult.Ok;

        private DbResult(DbResult status, T? value, string? error)
        {
            Status = status;
            Value = value;
            Error = error;
        }

        public static DbResult<T> Ok(T value) => new(DbResult.Ok, value, null);
        public static DbResult<T> Fail(DbResult status, string error) => new(status, default, error);
        public static DbResult<T> NotFound(string error = "NOT FOUND") => new(DbResult.NotFound, default, error);
        public static DbResult<T> FailWithError(string error) => new(DbResult.Error, default, error);

        public void Deconstruct(out DbResult status, out T? value, out string? error)
        {
            status = Status;
            value = Value;
            error = Error;
        }
    }

    /// <summary>Тип отклонения от нормы — копия <c>libeLog.Models.DeviationReasonType</c>.</summary>
    public enum DeviationReasonType
    {
        Setup, Machining
    }

    /// <summary>Открытие SQL-соединения — минимальная копия нужных методов <c>libeLog.Infrastructure.Db.DbHelper</c>.</summary>
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
    }
}
