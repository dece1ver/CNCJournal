-- Расширение колонки Status в stanki.dbo.cnc_machine_activity с NVARCHAR(20) до NVARCHAR(100).
--
-- Зачем: eLog пишет в Status название простоя вместе с фазой ("Обращение в другие службы [Н]"),
-- поэтому MERGE статуса падал с "Произойдет усечение строковых или двоичных данных в столбце Status",
-- запись статуса станка не проходила ни разу, а в лог каждые 12 секунд падал одинаковый стектрейс.
--
-- ОСНОВНОЙ ПУТЬ — remeLog «Обновление БД»: тот же ALTER генерируется из определения таблицы
-- (libeLog/Infrastructure/Sql/SqlSchemaBootstrapper.cs → SqlSchemaDiffHelper.GetColumnsToWidenAsync →
-- SqlSchemaAlterHelper.GenerateWidenColumnsScript), потому что сравнение схемы само видит,
-- что в базе уже, чем объявлено. Этот файл нужен только если обновляться через remeLog нельзя.
--
-- Идемпотентен и только расширяет: данные не теряются, выполнять можно повторно.
-- NOT NULL — как в определении таблицы (TableBuilder.AddStringColumn("Status", 100, false)).
-- Версия схемы (cnc_remelog_config.SchemaVersion) не меняется: расширение обратно совместимо.

USE [stanki];
GO

IF EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME = 'cnc_machine_activity'
      AND COLUMN_NAME = 'Status'
      AND DATA_TYPE = 'nvarchar'
      AND CHARACTER_MAXIMUM_LENGTH < 100
)
BEGIN
    -- ALTER COLUMN не выполняется, пока на столбец ссылается CHECK-ограничение: снимаем и вернём.
    DECLARE @createChecks NVARCHAR(MAX) = N'';
    DECLARE @dropChecks NVARCHAR(MAX) = N'';
    SELECT @createChecks = @createChecks + N'
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                       WHERE name = N''{cc.name}'' AND parent_object_id = OBJECT_ID(N''dbo.cnc_machine_activity''))
            ALTER TABLE [dbo].[cnc_machine_activity] ADD ' + OBJECT_DEFINITION(cc.object_id) + N';' + CHAR(13)
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = OBJECT_ID(N'dbo.cnc_machine_activity')
      AND EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
                  WHERE d.referenced_id = cc.object_id
                    AND d.referenced_minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.cnc_machine_activity'), N'Status', 'ColumnId'));
    SELECT @dropChecks = @dropChecks + N'ALTER TABLE [dbo].[cnc_machine_activity] DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(13)
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = OBJECT_ID(N'dbo.cnc_machine_activity')
      AND EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
                  WHERE d.referenced_id = cc.object_id
                    AND d.referenced_minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.cnc_machine_activity'), N'Status', 'ColumnId'));
    IF LEN(@dropChecks) > 0 EXEC sp_executesql @dropChecks;

    ALTER TABLE [dbo].[cnc_machine_activity] ALTER COLUMN [Status] NVARCHAR(100) NOT NULL;

    IF LEN(@createChecks) > 0 EXEC sp_executesql @createChecks;

    PRINT 'cnc_machine_activity.Status расширена до NVARCHAR(100).';
END
ELSE
    PRINT 'cnc_machine_activity.Status уже NVARCHAR(100) или шире — изменений не требуется.';
GO

-- Проверка
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'cnc_machine_activity' AND COLUMN_NAME = 'Status';
GO