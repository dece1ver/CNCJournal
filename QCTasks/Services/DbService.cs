using Dapper;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QCTasks.Services;

/// <summary>
/// Сервис записи статистики в SQL Server.
/// Все методы безопасны при пустом ConnectionString — просто возвращают дефолт и не падают.
/// </summary>
public class DbService
{
    private readonly string? _connectionString;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionString);

    public DbService(string? connectionString)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? null
            : connectionString;
    }

    /// <summary>
    /// Вставляет запись о начале проверки.
    /// </summary>
    /// <returns>ID вставленной строки, или null если БД недоступна.</returns>
    public async Task<int?> StartInspectionAsync(
        string partName, string orderNumber, string? partsCount)
    {
        if (!IsAvailable) return null;

        const string sql = @"
INSERT INTO qc_inspections (part_name, order_number, parts_count, started_at, operator)
VALUES (@PartName, @OrderNumber, @PartsCount, @StartedAt, @UserName);
SELECT CAST(SCOPE_IDENTITY() AS INT);
";

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            return await conn.ExecuteScalarAsync<int>(sql, new
            {
                PartName = partName,
                OrderNumber = orderNumber,
                PartsCount = partsCount,
                StartedAt = DateTime.Now,
                Environment.UserName,
            });
        }
        catch (Exception ex)
        {
            LogError("StartInspectionAsync", ex);
            return null;
        }
    }

    /// <summary>
    /// Обновляет запись — фиксирует итог и время завершения.
    /// </summary>
    public async Task CompleteInspectionAsync(int id, bool accepted, ProductionTaskData inspection)
    {
        if (!IsAvailable) return;

        const string sql = @"
UPDATE qc_inspections
SET completed_at = @CompletedAt,
    result = @Result,
    comment = @Comment 
WHERE id = @Id;
";

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, new
            {
                CompletedAt = DateTime.Now,
                Result = accepted ? "Принято" : "Отклонено",
                Id = id,
                Comment = inspection.QcComment
            });
        }
        catch (Exception ex)
        {
            LogError("CompleteInspectionAsync", ex);
        }
    }

    /// <summary>
    /// Ищет незакрытую запись после перезапуска приложения.
    /// Возвращает ID или null если не найдено.
    /// </summary>
    public async Task<int?> FindActiveInspectionAsync(string partName, string orderNumber)
    {
        if (!IsAvailable) return null;

        const string sql = @"
SELECT TOP 1 id FROM qc_inspections
WHERE part_name = @PartName
    AND order_number = @OrderNumber
    AND completed_at IS NULL
ORDER BY started_at DESC;
";

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            var id = await conn.ExecuteScalarAsync<int?>(sql, new
            {
                PartName = partName,
                OrderNumber = orderNumber
            });
            return id;
        }
        catch (Exception ex)
        {
            LogError("FindActiveInspectionAsync", ex);
            return null;
        }
    }

    /// <summary>
    /// Закрывает зависшую строку без результата — статус "Отменено".
    /// Вызывается когда оператор убрал "В работе" вручную в таблице,
    /// а потом снова нажал "В работу".
    /// </summary>
    public async Task CancelInspectionAsync(int id)
    {
        if (!IsAvailable) return;

        const string sql = @"
UPDATE qc_inspections
SET completed_at = @CompletedAt,
    result = 'Отменено'
WHERE id = @Id
    AND completed_at IS NULL;
";
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, new { CompletedAt = DateTime.Now, Id = id });
        }
        catch (Exception ex) { LogError("CancelInspectionAsync", ex); }
    }

    /// <summary>
    /// Обновляет статус последней совпадающей по названию и заказу записи.
    /// </summary>
    public async Task UpdateInspectionAsync(ProductionTaskData inspection)
    {
        if (!IsAvailable) return;

        const string sql = @"
UPDATE qc_inspections
SET completed_at = @CompletedAt,
    result = @Result,
    comment = @Comment
WHERE id = (
    SELECT TOP (1) id
    FROM qc_inspections
    WHERE part_name = @Name 
      AND order_number = @OrderNumber
    ORDER BY started_at DESC
);
";
        try
        {
            await using var conn = new SqlConnection(_connectionString);

            await conn.ExecuteAsync(sql, new
            {
                CompletedAt = DateTime.Now,
                Name = inspection.PartName,
                OrderNumber = inspection.Order,
                Result = inspection.EngeneersComment,
                Comment = inspection.QcComment
            });
        }
        catch (Exception ex) { LogError("UpdateInspectionAsync", ex); }
    }

    private static void LogError(string method, Exception ex) =>
        Console.Error.WriteLine($"[DbService.{method}] {ex.Message}");
}
