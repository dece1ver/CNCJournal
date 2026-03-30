using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace libeLog.Infrastructure.Sql
{
    /// <summary>
    /// Класс для генерации скриптов ALTER TABLE
    /// </summary>
    public class SqlSchemaAlterHelper
    {
        /// <summary>
        /// Генерирует скрипт для добавления новых столбцов в существующую таблицу
        /// </summary>
        /// <param name="tableName">Имя таблицы</param>
        /// <param name="columnsToAdd">Список столбцов для добавления</param>
        /// <returns>SQL-скрипт для добавления столбцов</returns>
        public static string GenerateAddColumnsScript(string tableName, IEnumerable<ColumnDefinition> columnsToAdd)
        {
            SqlValidationHelper.ValidateName(tableName);
            var sb = new StringBuilder();
            var columns = columnsToAdd.ToList();
            if (columns.Count == 0)
                return string.Empty;

            foreach (var col in columns)
            {
                SqlValidationHelper.ValidateName(col.Name);

                // столбец, если его нет
                sb.AppendLine($@"
                    IF NOT EXISTS (
                        SELECT * 
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = 'dbo' 
                          AND TABLE_NAME   = N'{tableName}' 
                          AND COLUMN_NAME  = N'{col.Name}'
                    )
                    BEGIN
                        ALTER TABLE [dbo].[{tableName}] 
                            ADD [{col.Name}] 
                            {(col.IsComputed
                                ? $"AS {col.ComputedExpression} {(col.IsPersisted ? "PERSISTED" : "")}"
                                : $@"{col.SqlType}
                                    {(col.AutoIncrement ? "IDENTITY(1,1)" : "")}
                                    {(col.DefaultValueSql is not null ? $" DEFAULT {col.DefaultValueSql}" : "")}
                                    {(col.IsNullable ? "NULL" : "NOT NULL")}"
                            )};
                    END
                ");

                // PK на одиночный столбец
                if (col.IsPrimaryKey)
                {
                    sb.AppendLine($@"
                    IF NOT EXISTS (
                        SELECT * FROM sys.key_constraints
                        WHERE name = N'PK_{tableName}_{col.Name}' 
                          AND parent_object_id = OBJECT_ID(N'dbo.{tableName}')
                    )
                    BEGIN
                        ALTER TABLE [dbo].[{tableName}] 
                            ADD CONSTRAINT [PK_{tableName}_{col.Name}] 
                            PRIMARY KEY ([{col.Name}]);
                    END
                    ");
                }

                // UNIQUE на одиночный столбец
                if (col.IsUnique)
                {
                    sb.AppendLine($@"
                    IF NOT EXISTS (
                        SELECT * FROM sys.indexes
                        WHERE name = N'UQ_{tableName}_{col.Name}' 
                          AND object_id = OBJECT_ID(N'dbo.{tableName}')
                    )
                    BEGIN
                        ALTER TABLE [dbo].[{tableName}] 
                            ADD CONSTRAINT [UQ_{tableName}_{col.Name}] 
                            UNIQUE ([{col.Name}]);
                    END
                    ");
                }

                // Одиночный FK
                if (col.ForeignKey is ForeignKeyDefinition fk)
                {
                    var refCol = fk.ReferencedColumns[0];
                    sb.AppendLine($@"
                    IF NOT EXISTS (
                        SELECT * FROM sys.foreign_keys
                        WHERE name = N'FK_{tableName}_{fk.ReferencedTable}_{col.Name}'
                          AND parent_object_id = OBJECT_ID(N'dbo.{tableName}')
                    )
                    BEGIN
                        ALTER TABLE [dbo].[{tableName}] 
                            ADD CONSTRAINT [FK_{tableName}_{fk.ReferencedTable}_{col.Name}]
                            FOREIGN KEY ([{col.Name}]) 
                            REFERENCES [dbo].[{fk.ReferencedTable}]([{refCol}])
                            {(fk.OnDelete != ForeignKeyAction.NoAction ? $"ON DELETE {SqlSchemaHelper.ToSqlAction(fk.OnDelete)}" : "")}
                            {(fk.OnUpdate != ForeignKeyAction.NoAction ? $"ON UPDATE {SqlSchemaHelper.ToSqlAction(fk.OnUpdate)}" : "")};
                    END
                    ");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Генерирует скрипт для добавления составных ключей и ограничений в существующую таблицу
        /// </summary>
        /// <param name="tableName">Имя таблицы</param>
        /// <param name="tableDefinition">Определение таблицы с ограничениями</param>
        /// <returns>SQL-скрипт для добавления ограничений</returns>
        public static string GenerateAddConstraintsScript(string tableName, TableDefinition tableDefinition)
        {
            SqlValidationHelper.ValidateName(tableName);

            var sb = new StringBuilder();

            if (tableDefinition.CompositePrimaryKey.Count > 0)
            {
                string columns = string.Join(",", tableDefinition.CompositePrimaryKey.Select(c => $"[{c}]"));
                sb.AppendLine($@"
                IF NOT EXISTS (
                    SELECT * FROM sys.key_constraints 
                    WHERE name = N'PK_{tableName}' AND parent_object_id = OBJECT_ID(N'dbo.{tableName}')
                )
                BEGIN
                    ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT [PK_{tableName}] PRIMARY KEY ({columns});
                END
                ");
            }

            for (int i = 0; i < tableDefinition.CompositeUniques.Count; i++)
            {
                var unique = tableDefinition.CompositeUniques[i];
                string columns = string.Join(",", unique.Select(c => $"[{c}]"));
                sb.AppendLine($@"
                IF NOT EXISTS (
                    SELECT * FROM sys.indexes 
                    WHERE name = N'UQ_{tableName}_{i}' AND object_id = OBJECT_ID(N'dbo.{tableName}')
                )
                BEGIN
                    ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT [UQ_{tableName}_{i}] UNIQUE ({columns});
                END
                ");
            }

            for (int i = 0; i < tableDefinition.ForeignKeys.Count; i++)
            {
                var fk = tableDefinition.ForeignKeys[i];

                string columns = string.Join(",", fk.Columns.Select(c => $"[{c}]"));
                string refColumns = string.Join(",", fk.ReferencedColumns.Select(c => $"[{c}]"));

                string constraintName = fk.Columns.Count == 1
                    ? $"FK_{tableName}_{fk.Columns[0]}_{fk.ReferencedTable}_{fk.ReferencedColumns[0]}"
                    : $"FK_{tableName}_{fk.ReferencedTable}_{i}";

                sb.AppendLine($@"
                IF NOT EXISTS (
                    SELECT * FROM sys.foreign_keys 
                    WHERE name = N'{constraintName}' AND parent_object_id = OBJECT_ID(N'dbo.{tableName}')
                )
                BEGIN
                    ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT [{constraintName}]
                    FOREIGN KEY ({columns}) REFERENCES [dbo].[{fk.ReferencedTable}] ({refColumns})
                    {(fk.OnDelete != ForeignKeyAction.NoAction ? $"ON DELETE {SqlSchemaHelper.ToSqlAction(fk.OnDelete)}" : "")}
                    {(fk.OnUpdate != ForeignKeyAction.NoAction ? $"ON UPDATE {SqlSchemaHelper.ToSqlAction(fk.OnUpdate)}" : "")};
                END
                ");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Генерирует скрипт CREATE INDEX для всех индексов таблицы.
        /// Каждый индекс оборачивается в проверку sys.indexes, поэтому скрипт
        /// безопасно запускать повторно — существующие индексы пропускаются.
        /// </summary>
        /// <param name="tableName">Имя таблицы.</param>
        /// <param name="indexes">Список описаний индексов.</param>
        /// <returns>SQL-скрипт; пустая строка если индексов нет.</returns>
        public static string GenerateAddIndexesScript(string tableName, IEnumerable<IndexDefinition> indexes)
        {
            SqlValidationHelper.ValidateName(tableName);

            var sb = new StringBuilder();

            foreach (var idx in indexes)
            {
                if (idx.Columns == null || idx.Columns.Count == 0)
                    throw new ArgumentException($"Индекс в таблице '{tableName}' не содержит столбцов.");

                string indexName = idx.ResolveName(tableName);
                SqlValidationHelper.ValidateName(indexName);

                string cols = string.Join(", ", idx.Columns.Select(c => $"[{c}]"));
                string unique = idx.IsUnique ? "UNIQUE " : string.Empty;
                string include = idx.IncludeColumns.Count > 0
                    ? $"\n    INCLUDE ({string.Join(", ", idx.IncludeColumns.Select(c => $"[{c}]"))})"
                    : string.Empty;
                string filter = !string.IsNullOrWhiteSpace(idx.Filter)
                    ? $"\n    WHERE {idx.Filter}"
                    : string.Empty;

                sb.AppendLine($@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'{indexName}'
      AND object_id = OBJECT_ID(N'dbo.{tableName}')
)
    CREATE {unique}INDEX [{indexName}]
        ON [dbo].[{tableName}] ({cols}){include}{filter};
");
            }

            return sb.ToString();
        }
    }
}