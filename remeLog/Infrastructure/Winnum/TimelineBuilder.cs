using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Winnum
{
    public static class TimelineBuilder
    {
        private static TimeSpan _bucketStep;

        private static readonly string[] DefaultTimeFormats =
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.ff",
            "yyyy-MM-dd HH:mm:ss.f",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.ff",
            "yyyy-MM-ddTHH:mm:ss",
            "dd.MM.yyyy HH:mm:ss.fff",
            "dd.MM.yyyy HH:mm:ss"
        };

        private enum EventKind
        {
            End = 0,    // сначала очищаем
            Start = 1   // потом устанавливаем
        }

        private readonly record struct TimelineEvent(
            DateTime Time,
            string Signal,
            string Value,
            EventKind Kind);

        public static async Task<DataTable> BuildAsync(
            IEnumerable<TimelineSource> sources,
            TimeSpan? step = null,
            IProgress<string>? progress = null)
        {
            _bucketStep = step ?? TimeSpan.FromSeconds(5);

            var sourceList = sources.ToList();

            progress?.Report("Получение данных...");

            var fetchTasks = sourceList.Select(async source =>
            {
                try
                {
                    var xml = await source.FetchAsync();
                    return (source, xml, error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (source, xml: string.Empty, error: ex.Message);
                }
            });

            var fetched = await Task.WhenAll(fetchTasks);

            progress?.Report("Разбор XML...");

            var events = new List<TimelineEvent>();

            foreach (var (source, xml, error) in fetched)
            {
                if (error != null || string.IsNullOrWhiteSpace(xml))
                    continue;

                if (!Parser.TryParseXmlItems(xml, out var items))
                    continue;

                var formats = source.TimeFormats ?? DefaultTimeFormats;

                if (source is IntervalTimelineSource intervalSource)
                    ExtractIntervalEvents(events, items, intervalSource, formats);
                else
                    ExtractPointEvents(events, items, source, formats);
            }

            progress?.Report("Построение таблицы...");

            return BuildTable(sourceList, events);
        }

        private static void ExtractPointEvents(
            List<TimelineEvent> events,
            List<Dictionary<string, string>> items,
            TimelineSource source,
            string[] formats)
        {
            foreach (var item in items)
            {
                if (!item.TryGetValue(source.TimeKey, out var timeStr)) continue;
                if (!item.TryGetValue(source.ValueKey, out var value)) continue;
                if (!TryParseDateTime(timeStr, formats, out var time)) continue;

                events.Add(new TimelineEvent(
                    time,
                    source.DisplayName,
                    value,
                    EventKind.Start));
            }
        }

        private static void ExtractIntervalEvents(
            List<TimelineEvent> events,
            List<Dictionary<string, string>> items,
            IntervalTimelineSource source,
            string[] formats)
        {
            foreach (var item in items)
            {
                if (!item.TryGetValue(source.StartKey, out var startStr)) continue;
                if (!item.TryGetValue(source.EndKey, out var endStr)) continue;

                if (!TryParseDateTime(startStr, formats, out var start)) continue;
                if (!TryParseDateTime(endStr, formats, out var end)) continue;

                string value = source.IntervalValueKey != null &&
                               item.TryGetValue(source.IntervalValueKey, out var v)
                               ? v
                               : source.DisplayName;

                events.Add(new TimelineEvent(
                    start,
                    source.DisplayName,
                    value,
                    EventKind.Start));

                events.Add(new TimelineEvent(
                    end,
                    source.DisplayName,
                    "",
                    EventKind.End));
            }
        }

        private static DataTable BuildTable(
            List<TimelineSource> sources,
            List<TimelineEvent> events)
        {
            var table = new DataTable();
            table.Columns.Add("Время", typeof(string));

            var signals = sources
                .Select(s => s.DisplayName)
                .Distinct()
                .ToList();

            foreach (var s in signals)
                table.Columns.Add(s, typeof(string));

            if (events.Count == 0)
                return table;

            // КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ:
            // сортировка по времени + приоритету события
            events.Sort((a, b) =>
            {
                int t = a.Time.CompareTo(b.Time);
                if (t != 0) return t;
                return a.Kind.CompareTo(b.Kind);
            });

            var firstTime = FloorToBucket(events.First().Time);
            var lastTime = FloorToBucket(events.Last().Time);

            var state = signals.ToDictionary(s => s, _ => "");

            int index = 0;

            for (var t = firstTime; t <= lastTime; t += _bucketStep)
            {
                while (index < events.Count && events[index].Time <= t)
                {
                    var ev = events[index];

                    if (state.ContainsKey(ev.Signal))
                        state[ev.Signal] = ev.Value;

                    index++;
                }

                if (state.Values.All(string.IsNullOrEmpty))
                    continue;

                var row = table.NewRow();

                row["Время"] = t.ToString("HH:mm:ss");

                foreach (var s in signals)
                    row[s] = state[s];

                table.Rows.Add(row);
            }

            return table;
        }

        private static DateTime FloorToBucket(DateTime dt)
        {
            var ticks = _bucketStep.Ticks;
            return new DateTime(dt.Ticks - dt.Ticks % ticks, dt.Kind);
        }

        private static bool TryParseDateTime(
            string s,
            string[] formats,
            out DateTime result)
        {
            return DateTime.TryParseExact(
                       s,
                       formats,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out result)
                   || DateTime.TryParse(
                       s,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out result);
        }
    }
}