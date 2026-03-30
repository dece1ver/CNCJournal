using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace libeLog.Infrastructure.Sql
{
    public class TableBuilder
    {
        private readonly TableDefinition _table;
        public TableBuilder(string tableName)
        {
            SqlValidationHelper.ValidateName(tableName);
            _table = new TableDefinition { Name = tableName };
        }

        public static string MapToSqlServerType(string genericType)
        {
            if (string.IsNullOrEmpty(genericType))
                throw new ArgumentException("Тип SQL не может быть пустым");

            if (genericType.Contains('('))
            {
                if (!genericType.EndsWith(")"))
                    throw new ArgumentException($"Некорректный формат типа SQL: {genericType}");

                int openBracket = genericType.IndexOf('(');
                string typeName = genericType.Substring(0, openBracket).ToUpper();
                string parameters = genericType.Substring(openBracket);

                if (typeName == "DECIMAL" || typeName == "NUMERIC")
                {
                    if (!Regex.IsMatch(parameters, @"^\(\d+,\d+\)$") && !Regex.IsMatch(parameters, @"^\(\d+\)$"))
                        throw new ArgumentException($"Некорректный формат параметров для типа {typeName}: {parameters}");
                }
                else if (typeName == "VARCHAR" || typeName == "NVARCHAR" || typeName == "CHAR" || typeName == "NCHAR")
                {
                    if (!Regex.IsMatch(parameters, @"^\(\d+\)$") && parameters.ToUpper() != "(MAX)")
                        throw new ArgumentException($"Некорректный формат параметров для типа {typeName}: {parameters}");
                }

                return typeName switch
                {
                    "VARCHAR" => $"VARCHAR{parameters}",
                    "NVARCHAR" => $"NVARCHAR{parameters}",
                    "CHAR" => $"CHAR{parameters}",
                    "NCHAR" => $"NCHAR{parameters}",
                    "DECIMAL" => $"DECIMAL{parameters}",
                    "NUMERIC" => $"DECIMAL{parameters}",
                    _ => genericType
                };
            }

            string baseType = genericType.ToUpper();
            return baseType switch
            {
                "INT" => "INT",
                "INTEGER" => "INT",
                "BIGINT" => "BIGINT",
                "SMALLINT" => "SMALLINT",
                "TINYINT" => "TINYINT",
                "VARCHAR" => "VARCHAR(255)",
                "NVARCHAR" => "NVARCHAR(255)",
                "TEXT" => "NVARCHAR(MAX)",
                "BLOB" => "VARBINARY(MAX)",
                "BOOLEAN" => "BIT",
                "DATETIME" => "DATETIME2",
                "DATE" => "DATE",
                "TIME" => "TIME",
                "DECIMAL" => "DECIMAL(18,2)",
                "FLOAT" => "FLOAT",
                "DOUBLE" => "FLOAT",
                "UUID" or "GUID" => "UNIQUEIDENTIFIER",
                _ => genericType
            };
        }

        /// <summary>
        /// Добавляет в таблицу столбец с произвольным SQL-типом, при этом преобразует тип к формату SQL Server, если необходимо.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="sqlType">Строковое представление SQL-типа (например, "INT", "VARCHAR(50)").</param>
        /// <param name="configure">Опциональная конфигурация столбца (PK, FK, Nullable и т.п.).</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddSqlServerColumn(string name, string sqlType, Action<ColumnOptions>? configure = null)
        {
            return AddColumn(name, MapToSqlServerType(sqlType), configure);
        }

        /// <summary>
        /// Добавляет столбец с заданным именем и типом в таблицу.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="sqlType">Тип данных столбца в SQL (например, "INT", "NVARCHAR(100)").</param>
        /// <param name="configure">Делегат настройки дополнительных свойств (nullable, PK, FK и т.п.).</param>
        /// <exception cref="ArgumentException">Если имя столбца недопустимо или уже существует в таблице.</exception>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddColumn(string name, string sqlType, Action<ColumnOptions>? configure = null)
        {
            SqlValidationHelper.ValidateName(name);

            if (_table.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Дублирующееся имя столбца: {name}");

            var column = new ColumnDefinition
            {
                Name = name,
                SqlType = sqlType,
            };

            configure?.Invoke(new ColumnOptions(column));
            _table.Columns.Add(column);
            return this;
        }

        /// <summary>
        /// Добавляет стандартный целочисленный первичный ключ с именем "Id".
        /// </summary>
        /// <param name="name">Имя столбца (по умолчанию "Id").</param>
        /// <param name="identity">Указывает, должен ли столбец быть автоинкрементируемым (IDENTITY).</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddIdColumn(string name = "Id", bool identity = true)
        {
            return AddColumn(name, "INT", opt => {
                opt.PrimaryKey().Nullable(false);
                if (identity) opt.Identity();
            });
        }

        /// <summary>
        /// Добавляет колонку типа GUID (UNIQUEIDENTIFIER).
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="isPrimaryKey">Делает столбец первичным ключом, если true.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddGuidColumn(string name, bool isPrimaryKey = false, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, "UNIQUEIDENTIFIER", opt => {
                opt.Nullable(nullable);
                if (isPrimaryKey) opt.PrimaryKey().Nullable(false);
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет строковую колонку NVARCHAR(N) или NVARCHAR(MAX).
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="length">Максимальная длина. Если -1, используется NVARCHAR(MAX).</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddStringColumn(string name, int length = 255, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, $"NVARCHAR({(length == -1 ? "MAX" : length.ToString())})", opt =>
            {
                opt.Nullable(nullable);
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет целочисленный столбец типа TINYINT.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddByteColumn(string name, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, "TINYINT", opt => {
                opt.Nullable(nullable);
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет целочисленный столбец типа INT.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddIntColumn(string name, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, "INT", opt => { 
                opt.Nullable(nullable);
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет столбец типа FLOAT.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddDoubleColumn(string name, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, "FLOAT", opt => { 
                opt.Nullable(nullable); 
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }



        /// <summary>
        /// Добавляет булев столбец типа BIT.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddBoolColumn(string name, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, "BIT", opt => { 
                opt.Nullable(nullable); 
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет столбец даты и времени типа SMALLDATETIME.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddSmallDateTimeColumn(string name, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, "SMALLDATETIME", opt => { 
                opt.Nullable(nullable); 
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет столбец даты и времени типа DATETIME2.
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddDateTimeColumn(string name, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, "DATETIME2", opt =>
            {
                opt.Nullable(nullable);
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет десятичный столбец типа DECIMAL(P, S).
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="precision">Общая точность (по умолчанию 18).</param>
        /// <param name="scale">Число знаков после запятой (по умолчанию 2).</param>
        /// <param name="nullable">Указывает, допускается ли NULL.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddDecimalColumn(string name, int precision = 18, int scale = 2, bool nullable = true, object? defaultValue = null)
        {
            return AddColumn(name, $"DECIMAL({precision},{scale})", opt => { 
                opt.Nullable(nullable); 
                if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
            });
        }

        /// <summary>
        /// Добавляет фиксированную юникодную строку NCHAR(N).
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="length">Точ­ная длина (N).</param>
        /// <param name="nullable">Допускается ли NULL (по умолчанию true).</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddNCharColumn(string name, int length, bool nullable = true, object? defaultValue = null)
        {
            if (length <= 0)
                throw new ArgumentException("Длина NCHAR должна быть больше нуля.", nameof(length));

            return AddSqlServerColumn(
                name,
                $"NCHAR({length})",
                opt => {
                    opt.Nullable(nullable);
                    if (defaultValue is not null) opt.DefaultSql(ToSqlLiteral(defaultValue));
                }
            );
        }

        /// <summary>
        /// Добавляет вычисляемый столбец (Computed Column).
        /// </summary>
        /// <param name="name">Имя столбца.</param>
        /// <param name="expression">SQL-выражение для вычисления.</param>
        /// <param name="persisted">Флаг: хранить ли как PERSISTED (по умолчанию true).</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddComputedColumn(string name, string expression, bool persisted = true)
        {
            SqlValidationHelper.ValidateName(name);

            if (_table.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Дублирующееся имя столбца: {name}");

            var column = new ColumnDefinition
            {
                Name = name,
                ComputedExpression = expression,
                IsPersisted = persisted
            };

            _table.Columns.Add(column);
            return this;
        }

        /// <summary>
        /// Устанавливает составной первичный ключ по указанным именам столбцов.
        /// </summary>
        /// <param name="columns">Список имён колонок, входящих в составной PK.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        /// <exception cref="ArgumentException">Если хотя бы одна из указанных колонок не определена в таблице.</exception>
        public TableBuilder AddCompositePrimaryKey(params string[] columns)
        {
            SqlValidationHelper.ValidateColumnsExist(columns, _table.Columns);
            _table.CompositePrimaryKey = columns.ToList();
            return this;
        }

        /// <summary>
        /// Добавляет составное уникальное ограничение по набору колонок.
        /// </summary>
        /// <param name="columns">Имена колонок, входящих в составной UNIQUE.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        /// <exception cref="ArgumentException">Если хотя бы одна колонка не существует в таблице.</exception>
        public TableBuilder AddCompositeUnique(params string[] columns)
        {
            SqlValidationHelper.ValidateColumnsExist(columns, _table.Columns);
            _table.CompositeUniques.Add(columns.ToList());
            return this;
        }

        /// <summary>
        /// Добавляет внешний ключ по одной колонке.
        /// </summary>
        /// <param name="column">Имя столбца, на который накладывается FK.</param>
        /// <param name="referencedTable">Имя таблицы, на которую ссылается FK.</param>
        /// <param name="referencedColumn">Имя столбца в ссылочной таблице.</param>
        /// <param name="onDelete">Действие при удалении строки.</param>
        /// <param name="onUpdate">Действие при обновлении строки.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        /// <exception cref="ArgumentException">Если столбец не существует в таблице или имя недопустимо.</exception>
        public TableBuilder AddForeignKey(string column, string referencedTable, string referencedColumn,
            ForeignKeyAction onDelete = ForeignKeyAction.NoAction, ForeignKeyAction onUpdate = ForeignKeyAction.NoAction)
        {
            SqlValidationHelper.ValidateName(column);
            SqlValidationHelper.ValidateName(referencedTable);
            SqlValidationHelper.ValidateName(referencedColumn);

            if (!_table.Columns.Any(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Столбец '{column}' не существует в таблице '{_table.Name}'");

            return AddCompositeForeignKey(new[] { column }, referencedTable, new[] { referencedColumn }, onDelete, onUpdate);
        }

        /// <summary>
        /// Добавляет составной внешний ключ.
        /// </summary>
        /// <param name="columns">Список локальных колонок, входящих в FK.</param>
        /// <param name="referencedTable">Имя таблицы, на которую ссылается FK.</param>
        /// <param name="referencedColumns">Список ссылочных колонок.</param>
        /// <param name="onDelete">Действие при удалении строки.</param>
        /// <param name="onUpdate">Действие при обновлении строки.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        /// <exception cref="ArgumentException">
        /// Если количество колонок не совпадает, имена недопустимы, или FK уже был задан для одной из колонок.
        /// </exception>
        public TableBuilder AddCompositeForeignKey(IEnumerable<string> columns, string referencedTable, IEnumerable<string> referencedColumns,
            ForeignKeyAction onDelete = ForeignKeyAction.NoAction, ForeignKeyAction onUpdate = ForeignKeyAction.NoAction)
        {
            var colList = columns.ToList();
            var refList = referencedColumns.ToList();

            if (colList.Count != refList.Count)
                throw new ArgumentException("Число столбцов FK и ссылочных столбцов должно совпадать");

            SqlValidationHelper.ValidateColumnsExist(colList, _table.Columns);
            SqlValidationHelper.ValidateName(referencedTable);
            foreach (var refCol in refList)
                SqlValidationHelper.ValidateName(refCol);

            bool hasDuplicateFk = false;
            foreach (var col in _table.Columns)
            {
                if (col.ForeignKey != null && colList.Count == 1 && colList[0].Equals(col.Name, StringComparison.OrdinalIgnoreCase))
                {
                    col.ForeignKey = null;
                    hasDuplicateFk = true;
                }
            }

            if (hasDuplicateFk)
            {
                Console.WriteLine($"Предупреждение: Внешний ключ для {string.Join(", ", colList)} был определен дважды и перезаписан.");
            }

            _table.ForeignKeys.Add(new ForeignKeyDefinition
            {
                Columns = colList,
                ReferencedTable = referencedTable,
                ReferencedColumns = refList,
                OnDelete = onDelete,
                OnUpdate = onUpdate
            });

            return this;
        }



        /// <summary>
        /// Добавляет стандартные поля аудита: CreatedAt, CreatedBy, ModifiedAt, ModifiedBy.
        /// </summary>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddAuditColumns()
        {
            return this
                .AddDateTimeColumn("CreatedAt", false)
                .AddStringColumn("CreatedBy", 100, false)
                .AddDateTimeColumn("ModifiedAt", true)
                .AddStringColumn("ModifiedBy", 100, true);
        }

        /// <summary>
        /// Добавляет описание индекса к таблице.
        /// Индекс создаётся при деплое через SqlSchemaBootstrapper.ApplyAsync.
        /// </summary>
        /// <param name="columns">Столбцы индекса.</param>
        /// <param name="filter">
        /// Необязательное WHERE-условие для фильтрованного индекса,
        /// например: <c>"completed_at IS NULL"</c>.
        /// </param>
        /// <param name="name">
        /// Явное имя индекса. Если null — формируется как
        /// <c>IX_{таблица}_{столбец1}_{столбец2}</c>.
        /// </param>
        /// <param name="unique">Создать уникальный индекс.</param>
        /// <param name="include">INCLUDE-столбцы для покрывающего индекса.</param>
        /// <returns>Текущий экземпляр <see cref="TableBuilder"/>.</returns>
        public TableBuilder AddIndex(
            string[] columns,
            string? filter = null,
            string? name = null,
            bool unique = false,
            string[]? include = null)
        {
            if (columns == null || columns.Length == 0)
                throw new ArgumentException("Индекс должен содержать хотя бы один столбец.", nameof(columns));

            foreach (var col in columns)
                SqlValidationHelper.ValidateName(col);

            if (include != null)
                foreach (var col in include)
                    SqlValidationHelper.ValidateName(col);

            _table.Indexes.Add(new IndexDefinition
            {
                Name = name,
                Columns = new List<string>(columns),
                IncludeColumns = include != null ? new List<string>(include) : new List<string>(),
                Filter = filter,
                IsUnique = unique,
            });

            return this;
        }

        /// <summary>
        /// Финализирует построение таблицы и возвращает <see cref="TableDefinition"/>.
        /// </summary>
        /// <returns>Описание таблицы.</returns>
        public TableDefinition Build() => _table;

        private static string ToSqlLiteral(object value)
        {
            return value switch
            {
                string s => s,
                bool b => b ? "1" : "0",
                DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
                double d => d.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString(CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                Enum e => Convert.ToInt32(e).ToString(),
                _ => value.ToString() ?? "NULL"
            };
        }

        public class ColumnOptions
        {
            private readonly ColumnDefinition _column;

            public ColumnOptions(ColumnDefinition column)
            {
                _column = column;
            }

            public ColumnOptions Nullable(bool value = true)
            {
                _column.IsNullable = value;
                return this;
            }

            public ColumnOptions PrimaryKey(bool value = true)
            {
                _column.IsPrimaryKey = value;
                return this;
            }

            public ColumnOptions Unique(bool value = true)
            {
                _column.IsUnique = value;
                return this;
            }

            public ColumnOptions Identity(bool value = true)
            {
                _column.AutoIncrement = value;
                return this;
            }

            public ColumnOptions Persisted(bool value = true)
            {
                _column.IsPersisted = value;
                return this;
            }

            public ColumnOptions DefaultSql(string sqlExpression)
            {
                _column.DefaultValueSql = sqlExpression;
                return this;
            }

            public ColumnOptions ForeignKey(string referencedTable, string referencedColumn,
                ForeignKeyAction onDelete = ForeignKeyAction.NoAction,
                ForeignKeyAction onUpdate = ForeignKeyAction.NoAction)
            {
                _column.ForeignKey = new ForeignKeyDefinition
                {
                    Columns = new() { _column.Name },
                    ReferencedTable = referencedTable,
                    ReferencedColumns = new() { referencedColumn },
                    OnDelete = onDelete,
                    OnUpdate = onUpdate
                };
                return this;
            }
        }
    }
}