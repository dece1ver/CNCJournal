using System.Collections.Generic;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Пользовательский профиль видимости колонок PartsInfoWindow — набор
    /// идентификаторов колонок (см. ColumnId/PartColumnMeta.Map), которые
    /// должны отображаться при выборе этого профиля.
    /// </summary>
    public class ColumnProfile
    {
        public string Name { get; set; } = "";
        public List<string> ColumnIds { get; set; } = new();
    }
}
