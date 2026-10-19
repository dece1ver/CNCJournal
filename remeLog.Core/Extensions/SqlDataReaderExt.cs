using Microsoft.Data.SqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.Core.Extensions
{
    /// <summary>
    /// Копия нужных методов <c>libeLog.Extensions.SqlDataReaderExt</c> — не используется
    /// нигде за пределами remeLog, поэтому дублируется здесь без сохранения оригинала.
    /// </summary>
    public static class SqlDataReaderExt
    {
        /// <summary>Значение колонки или <c>null</c>, если в ней DBNull.</summary>
        public static bool? GetNullableBoolean(this SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);

        /// <summary>Значение колонки или <c>null</c>, если в ней DBNull.</summary>
        public static double? GetNullableDouble(this SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

        /// <summary>Асинхронно получает значение из БД с обработкой DBNull.</summary>
        public static async Task<T> GetValueOrDefaultAsync<T>(
            this SqlDataReader reader,
            int index,
            T defaultValue = default!,
            CancellationToken cancellationToken = default)
        {
            if (await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false))
            {
                return defaultValue;
            }

            Type type = typeof(T);
            Type? underlyingType = Nullable.GetUnderlyingType(type);

            try
            {
                if (type == typeof(string))
                    return (T)(object)await reader.GetFieldValueAsync<string>(index, cancellationToken).ConfigureAwait(false);
                else if (type == typeof(int) || (underlyingType == typeof(int)))
                    return (T)(object)await reader.GetFieldValueAsync<int>(index, cancellationToken).ConfigureAwait(false);
                else if (type == typeof(long) || (underlyingType == typeof(long)))
                    return (T)(object)await reader.GetFieldValueAsync<long>(index, cancellationToken).ConfigureAwait(false);
                else if (type == typeof(double) || (underlyingType == typeof(double)))
                    return (T)(object)await reader.GetFieldValueAsync<double>(index, cancellationToken).ConfigureAwait(false);
                else if (type == typeof(decimal) || (underlyingType == typeof(decimal)))
                    return (T)(object)await reader.GetFieldValueAsync<decimal>(index, cancellationToken).ConfigureAwait(false);
                else if (type == typeof(DateTime) || (underlyingType == typeof(DateTime)))
                    return (T)(object)await reader.GetFieldValueAsync<DateTime>(index, cancellationToken).ConfigureAwait(false);
                else if (type == typeof(bool) || (underlyingType == typeof(bool)))
                    return (T)(object)await reader.GetFieldValueAsync<bool>(index, cancellationToken).ConfigureAwait(false);
                else if (type == typeof(Guid) || (underlyingType == typeof(Guid)))
                    return (T)(object)await reader.GetFieldValueAsync<Guid>(index, cancellationToken).ConfigureAwait(false);

                try
                {
                    return await reader.GetFieldValueAsync<T>(index, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    object value = await reader.GetFieldValueAsync<object>(index, cancellationToken).ConfigureAwait(false);
                    return (T)Convert.ChangeType(value, underlyingType ?? type);
                }
            }
            catch (InvalidCastException ex)
            {
                throw new InvalidCastException(
                    $"Не удалось преобразовать значение из столбца {index} в тип {type.Name}",
                    ex);
            }
        }

        /// <summary>Перегрузка по имени столбца.</summary>
        public static Task<T> GetValueOrDefaultAsync<T>(
            this SqlDataReader reader,
            string columnName,
            T defaultValue = default!,
            CancellationToken cancellationToken = default)
        {
            int index = reader.GetOrdinal(columnName);
            return reader.GetValueOrDefaultAsync(index, defaultValue, cancellationToken);
        }

        /// <summary>Перегрузка только с CancellationToken.</summary>
        public static Task<T> GetValueOrDefaultAsync<T>(
            this SqlDataReader reader,
            int index,
            CancellationToken cancellationToken) =>
            reader.GetValueOrDefaultAsync<T>(index, default!, cancellationToken);
    }
}
