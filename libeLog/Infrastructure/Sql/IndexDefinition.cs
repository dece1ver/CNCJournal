using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libeLog.Infrastructure.Sql
{
    public class IndexDefinition
    {
        /// <summary>Имя индекса. Если не задано — генерируется автоматически.</summary>
        public string? Name { get; set; }

        /// <summary>Столбцы индекса в порядке включения.</summary>
        public List<string> Columns { get; set; } = new List<string>();

        /// <summary>INCLUDE-столбцы (некластерный покрывающий индекс).</summary>
        public List<string> IncludeColumns { get; set; } = new List<string>();

        /// <summary>Выражение WHERE для фильтрованного индекса. null — обычный индекс.</summary>
        public string? Filter { get; set; }

        /// <summary>Уникальный индекс.</summary>
        public bool IsUnique { get; set; }

        /// <summary>Возвращает имя индекса: явное или сгенерированное по имени таблицы и столбцам.</summary>
        public string ResolveName(string tableName)
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name!;

            string prefix = IsUnique ? "UQ" : "IX";
            string cols = string.Join("_", Columns);
            return $"{prefix}_{tableName}_{cols}";
        }
    }
}
