namespace remeLog.Infrastructure.Types
{
    /// <summary>Фильтрация деталей по признаку серийности.</summary>
    public enum PartsFilterType
    {
        /// <summary>Все детали, без фильтрации.</summary>
        All,

        /// <summary>Только серийные детали.</summary>
        Serial,

        /// <summary>Только несерийные детали.</summary>
        NonSerial
    }
}
