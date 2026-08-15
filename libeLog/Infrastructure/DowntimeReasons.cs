namespace libeLog.Infrastructure
{
    /// <summary>
    /// Причины простоя целой смены из справочника cnc_downtime_reasons. Значения задаёт
    /// внедренец, но отчёты считают по ним отдельные колонки, поэтому набор фиксирован:
    /// этими же строками справочник заполняется при развёртывании
    /// (SqlSchemaBootstrapper, seed-if-empty), и по ним же ShiftInfo.DowntimesComment
    /// разбирается в сводных отчётах. Переименование причины в БД без правки здесь
    /// обнулит соответствующую колонку отчёта.
    /// </summary>
    public static class DowntimeReasons
    {
        /// <summary>Причина не указана — пустой пункт в начале списка.</summary>
        public const string None = "";

        public const string NoOperator = "Отсутствие оператора";
        public const string HardwareRepair = "Ремонт оборудования";
        public const string NoPower = "Отсутствие электричества";
        public const string ProcessRelatedLoss = "Организационные потери";
        public const string Other = "Другое";
    }
}
