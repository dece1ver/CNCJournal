using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    /// <summary>
    /// Активный фильтр, добавленный через контекстное меню таблицы.
    /// </summary>
    public sealed class FilterChip
    {
        /// <summary>Имя SQL-столбца. Пустое для InMemory-фильтров.</summary>
        public string SqlColumn { get; init; } = "";

        /// <summary>Отображаемое название — уникальный ключ для поиска в PartColumnMeta.Map.</summary>
        public string DisplayName { get; init; } = "";

        /// <summary>Значение (одно или несколько через ';').</summary>
        public string Value { get; init; } = "";

        /// <summary>True — фильтр применяется в памяти после загрузки, не в SQL.</summary>
        public bool IsInMemory { get; init; }

        public string Label => $"{DisplayName}: {Value}";
    }

}
