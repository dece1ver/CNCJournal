using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Winnum
{
    /// <summary>
    /// Описание источника данных для сводной таблицы.
    /// Содержит делегат получения данных и правила извлечения времени и значения из ответа.
    /// </summary>
    public class TimelineSource
    {
        /// <summary>Название колонки в итоговой таблице</summary>
        public string DisplayName { get; }

        /// <summary>Метод получения данных (возвращает XML-строку)</summary>
        public Func<Task<string>> FetchAsync { get; }

        /// <summary>Ключ атрибута времени события в XML-элементе</summary>
        public string TimeKey { get; }

        /// <summary>Ключ атрибута значения в XML-элементе</summary>
        public string ValueKey { get; }

        /// <summary>
        /// Форматы разбора времени. Если не задан — используется набор по умолчанию.
        /// </summary>
        public string[]? TimeFormats { get; }

        /// <summary>
        /// Опциональный преобразователь сырого значения в отображаемую строку.
        /// Если null — значение выводится как есть.
        /// </summary>
        public Func<string, string>? ValueMapper { get; }

        public TimelineSource(
            string displayName,
            Func<Task<string>> fetchAsync,
            string timeKey,
            string valueKey,
            string[]? timeFormats = null,
        Func<string, string>? valueMapper = null) 
        {
            DisplayName = displayName;
            FetchAsync = fetchAsync;
            TimeKey = timeKey;
            ValueKey = valueKey;
            TimeFormats = timeFormats;
            ValueMapper = valueMapper;
        }

        /// <summary>
        /// Источник на основе GetSignalAsync. Типичные ключи: event_time / value.
        /// </summary>
        public static TimelineSource FromSignal(
            string displayName,
            Func<Task<string>> fetchAsync,
            string timeKey = "event_time",
            string valueKey = "value",
            Func<string, string>? valueMapper = null) =>
            new(displayName, fetchAsync, timeKey, valueKey, valueMapper: valueMapper);

        /// <summary>
        /// Источник на основе GetTagIntervalCalculationAsync / GetSimpleTagIntervalCalculationAsync.
        /// Каждый элемент порождает два события: начало (значение "▶") и конец (значение "■").
        /// </summary>
        public static IntervalTimelineSource FromTagInterval(
            string displayName,
            Func<Task<string>> fetchAsync,
            string startKey = "start",
            string endKey = "end",
            string[]? timeFormats = null) =>
            new(displayName, fetchAsync, startKey, endKey, null, timeFormats);

        /// <summary>
        /// Источник на основе GetPriorityTagDurationAsync.
        /// </summary>
        public static IntervalTimelineSource FromPriorityTag(
            string displayName,
            Func<Task<string>> fetchAsync,
            DateTime? filterStart = null,
            DateTime? filterEnd = null) =>
                new(displayName, fetchAsync,
                startKey: "START",
                endKey: "END",
                valueKey: "TAG",
                timeFormats: new[] { "dd.MM.yyyy HH:mm:ss.fff", "dd.MM.yyyy HH:mm:ss" },
                filterStart: filterStart,
                filterEnd: filterEnd);
    }

    /// <summary>
    /// Интервальный источник — каждый XML-элемент описывает промежуток [start, end],
    /// а не одиночное событие. Значение берётся из отдельного атрибута (например TAG).
    /// </summary>
    public class IntervalTimelineSource : TimelineSource
    {
        public string StartKey { get; }
        public string EndKey { get; }
        public string? IntervalValueKey { get; }
        public DateTime? FilterStart { get; }
        public DateTime? FilterEnd { get; }

        public IntervalTimelineSource(
            string displayName,
            Func<Task<string>> fetchAsync,
            string startKey,
            string endKey,
            string? valueKey = null,
            string[]? timeFormats = null,
            DateTime? filterStart = null,
            DateTime? filterEnd = null)
            : base(displayName, fetchAsync, startKey, valueKey ?? "▶", timeFormats)
        {
            StartKey = startKey;
            EndKey = endKey;
            IntervalValueKey = valueKey;
            FilterStart = filterStart;
            FilterEnd = filterEnd;
        }
    }
}
