using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using remeLog.Infrastructure.Winnum.Data;
using static remeLog.Infrastructure.Winnum.Types;

namespace remeLog.Infrastructure.Winnum
{
    internal class Parser
    {
        private static readonly string[] WinnumDateFormats = new[]
        {
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.f",
            "yyyy-MM-dd HH:mm:ss.ff",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.f",
            "yyyy-MM-ddTHH:mm:ss.ff",
            "yyyy-MM-ddTHH:mm:ss.fff"
        };

        /// <summary>
        /// Преобразует строку в DateTime, если она соответствует одному из разрешённых ISO-форматов:
        /// - yyyy-MM-dd
        /// - yyyy-MM-ddTHH:mm:ss
        /// - yyyy-MM-ddTHH:mm:ss.fff
        /// </summary>
        /// <param name="input">Строка в одном из поддерживаемых форматов</param>
        /// <returns>Распарсенный объект DateTime</returns>
        /// <exception cref="FormatException">Если строка не соответствует ни одному из форматов</exception>
        public static DateTime FromWinnumDateTime(string input)
        {
            return DateTime.ParseExact(
                input,
                WinnumDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None
            );
        }

        public static List<Dictionary<string, string>> ParseXmlItems(string xml)
        {
            var xDoc = XDocument.Parse(xml);
            var items = xDoc.Root?.Elements("item");
            var result = new List<Dictionary<string, string>>();

            if (items == null)
                return result;

            foreach (var item in items)
            {
                var dict = item.Attributes()
                               .ToDictionary(
                                   a => Uri.UnescapeDataString(a.Name.LocalName), 
                                    a => Uri.UnescapeDataString(a.Value));
                result.Add(dict);
            }

            return result;
        }

        public static bool TryParseXmlItems(string xml, out List<Dictionary<string, string>> result)
        {
            result = new List<Dictionary<string, string>>();

            try
            {
                var xDoc = XDocument.Parse(xml);
                var items = xDoc.Root?.Elements("item");

                if (items == null)
                    return true;

                foreach (var item in items)
                {
                    var dict = item.Attributes()
                                   .ToDictionary(
                                       a => Uri.UnescapeDataString(a.Name.LocalName),
                                       a => Uri.UnescapeDataString(a.Value)
                                   );
                    result.Add(dict);
                }

                return true;
            }
            catch
            {
                result = null!;
                return false;
            }
        }




        public static List<PriorityTagDuration> ParsePriorityTagDurations(string xml, DateTime start, DateTime end)
        {
            var xDoc = XDocument.Parse(xml);
            var items = xDoc.Root?.Elements("item") ?? Enumerable.Empty<XElement>();
            var result = new List<PriorityTagDuration>();

            foreach (var el in items)
            {
                string UnescapeAttr(string name) =>
                    Uri.UnescapeDataString(el.Attribute(name)?.Value ?? "");

                var dto = new PriorityTagDuration
                {
                    SerialNumber = UnescapeAttr("SERIAL_NUMBER"),
                    Name = UnescapeAttr("NAME"),
                    Model = UnescapeAttr("MODEL"),
                    Tag = UnescapeAttr("TAG"),
                    Program = el.Attribute("PROGRAM") is { } p ? Uri.UnescapeDataString(p.Value) : null,
                    TimeDataRaw = UnescapeAttr("timeData"),
                    Start = DateTime.ParseExact(
                        UnescapeAttr("START"),
                        "dd.MM.yyyy HH:mm:ss.fff",
                        CultureInfo.InvariantCulture),
                    End = DateTime.ParseExact(
                        UnescapeAttr("END"),
                        "dd.MM.yyyy HH:mm:ss.fff",
                        CultureInfo.InvariantCulture),
                    Duration = double.TryParse(UnescapeAttr("DURATION"), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0
                };

                if (dto.Start < end && dto.End > start)
                {
                    result.Add(dto);
                }
            }

            return result;
        }

        /// <summary>
        /// Извлекает пары временных интервалов из словарей, где значения по ключу timeData — строки с миллисекундными Unix-метками времени через запятую.
        /// </summary>
        /// <param name="dicts">Коллекция словарей со строками меток времени</param>
        /// <param name="asLocalTime">Если true — преобразует в локальное время, иначе — в UTC</param>
        /// <returns>Перечисление пар DateTime (начало, конец)</returns>
        public static IEnumerable<(DateTime Start, DateTime End)> ParseTimeIntervals(
            IEnumerable<Dictionary<string, string>> dicts,
            bool asLocalTime = false)
        {
            foreach (var dict in dicts)
            {
                if (!dict.TryGetValue("timeData", out var raw)) continue;

                var timestamps = raw.Split(',')
                    .Select(s => long.TryParse(s, out var ms) ? ms : (long?)null)
                    .Where(ms => ms.HasValue)
                    .Select(ms =>
                    {
                        var dto = DateTimeOffset.FromUnixTimeMilliseconds(ms.Value);
                        return asLocalTime ? dto.LocalDateTime : dto.UtcDateTime;
                    })
                    .ToList();

                for (int i = 0; i + 1 < timestamps.Count; i += 2)
                {
                    yield return (timestamps[i], timestamps[i + 1]);
                }
            }
        }

        /// <summary>
        /// Возвращает общую длительность всех интервалов времени из словарей.
        /// </summary>
        /// <param name="dicts">Коллекция словарей со строками меток времени</param>
        /// <param name="asLocalTime">Если true — учитываются локальные DateTime, иначе UTC</param>
        /// <returns>Суммарная длительность всех интервалов</returns>
        public static TimeSpan SumTotalDuration(
            IEnumerable<Dictionary<string, string>> dicts,
            bool asLocalTime = false)
        {
            return ParseTimeIntervals(dicts, asLocalTime)
                .Aggregate(TimeSpan.Zero, (acc, pair) => acc + (pair.End - pair.Start));
        }

        /// <summary>
        /// Возвращает элементы, у которых интервалы между ключами startKey и endKey
        /// хотя бы частично пересекаются с указанным диапазоном времени.
        /// </summary>
        /// <param name="items">Исходная коллекция словарей.</param>
        /// <param name="startKey">Ключ начала интервала в словаре.</param>
        /// <param name="start">Начало интересующего диапазона.</param>
        /// <param name="endKey">Ключ конца интервала в словаре.</param>
        /// <param name="end">Конец интересующего диапазона.</param>
        /// <param name="format">Формат дат в словаре.</param>
        /// <returns>Список словарей, чьи интервалы пересекаются с указанным диапазоном.</returns>
        public static List<Dictionary<string, string>> FilterByDateRange(
            List<Dictionary<string, string>> items,
            string startKey,
            DateTime start,
            string endKey,
            DateTime end,
            string format = "dd.MM.yyyy HH:mm:ss.fff")
        {
            var filtered = new List<Dictionary<string, string>>();

            foreach (var item in items)
            {
                if (!item.TryGetValue(startKey, out var startStr) ||
                    !item.TryGetValue(endKey, out var endStr))
                    continue;

                if (!DateTime.TryParseExact(startStr, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var itemStart) ||
                    !DateTime.TryParseExact(endStr, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var itemEnd))
                    continue;

                if (itemStart < end && itemEnd > start)
                    filtered.Add(item);
            }

            return filtered;
        }
    }
}
