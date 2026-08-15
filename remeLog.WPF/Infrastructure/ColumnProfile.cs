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

        /// <summary>
        /// Переопределения ширины столбцов (ColumnId → px). Содержит только те
        /// колонки, для которых пользователь задал ширину, отличную от дефолтной
        /// из разметки (см. ColumnWidthDefaults) — остальные наследуют дефолт.
        /// </summary>
        public Dictionary<string, double> ColumnWidths { get; set; } = new();
    }
}
