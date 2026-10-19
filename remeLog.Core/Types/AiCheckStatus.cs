namespace remeLog.Infrastructure.Types
{
    /// <summary>
    /// Статус фоновой ИИ-проверки комментариев мастера для строки сутко-станка
    /// (фича AiMasterCheck). Только view-state: в БД не сохраняется, живёт до
    /// перезагрузки коллекции Parts.
    /// </summary>
    public enum AiCheckStatus
    {
        /// <summary> Проверка не требуется или не выполнялась. </summary>
        None,
        /// <summary> Запланирована/выполняется. </summary>
        Pending,
        /// <summary> Комментарии релевантны. </summary>
        Ok,
        /// <summary> Есть замечание (текст в AiCheckRemark). </summary>
        Remark,
        /// <summary> Проверка недоступна (сервер/таймаут/парсинг) — не показывать как замечание. </summary>
        Error,
    }
}
