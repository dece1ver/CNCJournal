using System;
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
        /// <summary>Статус операции.</summary>
        public DbResult Status { get; }
        /// <summary>Значение при успехе, иначе <c>default</c>.</summary>
        public T? Value { get; }
        /// <summary>Текст ошибки при неуспехе.</summary>
        public string? Error { get; }

        public bool IsOk => Status == DbResult.Ok;
        public bool IsError => Status != DbResult.Ok;

        private DbResult(DbResult status, T? value, string? error)
        {
            Status = status;
            Value = value;
            Error = error;
        }

        /// <summary>Успешный результат.</summary>
        public static DbResult<T> Ok(T value) => new(DbResult.Ok, value, null);
        /// <summary>Результат с ошибкой заданного статуса.</summary>
        public static DbResult<T> Fail(DbResult status, string error) => new(status, default, error);
        /// <summary>Запись не найдена.</summary>
        public static DbResult<T> NotFound(string error = "NOT FOUND") => new(DbResult.NotFound, default, error);
        /// <summary>Результат с ошибкой статуса <see cref="DbResult.Error"/>.</summary>
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
        /// <summary>Открывает соединение синхронно.</summary>
        public static SqlConnection OpenConnection(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Строка подключения не задана.");
            var conn = new SqlConnection(connectionString);
            conn.Open();
            return conn;
        }

        /// <summary>Открывает соединение асинхронно.</summary>
        public static async Task<SqlConnection> OpenConnectionAsync(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Строка подключения не задана.");
            var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            return conn;
        }
    }
}
