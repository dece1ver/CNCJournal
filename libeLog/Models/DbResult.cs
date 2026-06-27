using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libeLog.Models
{
    public enum DbResult
    {
        Ok, AuthError, Error, NoConnection, NotFound
    }

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
}
