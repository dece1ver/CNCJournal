using Newtonsoft.Json.Linq;
using remeLog.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.Models
{
    /// <summary>
    /// Клиент поиска чертежей/моделей в Windchill через <b>Windchill REST Services (WRS)</b> —
    /// официальный OData-эндпоинт Windchill.
    ///
    /// Каждый вызов <see cref="SearchAsync"/> — один синхронный HTTP GET-запрос, возвращающий
    /// готовый JSON. Поиск в Windchill выполняется на сервере сразу; опрашивать статус или
    /// повторять запрос не нужно.
    ///
    /// Авторизация — Basic-auth (логин/пароль из <c>cnc_wnc_cfg</c>), заголовок выставляется
    /// один раз в конструкторе и действует на все запросы клиента. WRS поддерживает и OAuth2,
    /// но это требует отдельной настройки на стороне Windchill (свой OAuth-клиент, возможно
    /// внешний IdP) — административная задача, а не правка кода; кроме того, сервер сейчас
    /// работает по обычному http:// (см. <c>Server</c> в конфиге), так что Basic и Bearer токен
    /// одинаково идут открытым текстом без TLS — переход на токены не даст выигрыша в защите,
    /// пока не включён HTTPS.
    ///
    /// <see cref="WncConfig.LocalType"/> здесь не используется: сущность <c>CADDocuments</c>
    /// (см. <see cref="SearchAsync"/>) сама по себе ограничивает выборку CAD-документами, без
    /// отдельного фильтра по типу. Поле в БД/конфиге оставлено на случай другой фильтрации.
    ///
    /// Документация (портал техподдержки PTC, требует логин):
    /// <see href="https://support.ptc.com/help/windchill_rest_services/r2.0/en/windchill_rest_services/WCCG_RESTAPIsWRS.html">
    /// Windchill REST Services — общий обзор</see>,
    /// <see href="https://support.ptc.com/help/windchill_rest_services/r2.4/en/windchill_rest_services/WCCG_RESTAccessExamplesFetchNONCE.html">
    /// Fetching a NONCE Token from a Service</see> (CSRF-токен через <c>GetCSRFToken()</c> — нужен
    /// только для create/update/delete, здесь не используется, т.к. клиент только читает; см. также
    /// диагностику в README при поиске, сломавшемся после обновления Windchill).
    /// </summary>
    public class WindchillClient : IDisposable
    {
        /// <summary>
        /// Максимум строк, забираемых за один поиск с каждого опрашиваемого entity set (см.
        /// <see cref="SearchAsync"/>). Если найденных объектов больше, <see cref="SearchAsync"/>
        /// возвращает первые <see cref="MaxResults"/> и <c>Truncated=true</c>.
        ///
        /// Признак усечения — количество полученных объектов относительно этого лимита, а не
        /// <c>@odata.count</c> из ответа сервера: на практике это поле не всегда совпадает с
        /// реальным числом строк в <c>value</c> (например, сервер возвращает <c>@odata.count=27</c>
        /// при 23 объектах в <c>value</c> и без <c>@odata.nextLink</c>), так что доверять ему как
        /// точному числу совпадений нельзя.
        /// </summary>
        public const int MaxResults = 100;

        private readonly HttpClient _client;
        private readonly string _serverUrl;

        /// <summary>
        /// Вызывается после каждого HTTP-запроса к Windchill (в т.ч. неудачного) — ровно один раз
        /// на запрос, со строкой вида "[статус] длительность URL". Нужен для диагностики нагрузки:
        /// сюда подписывается запись в <c>remeLog_wnc_requests</c> (см. <c>Util.SearchInWindchill</c>),
        /// чтобы в логе был воспроизводимый URL, а не только факт обращения.
        ///
        /// Одно действие пользователя — не обязательно один запрос: поиск с
        /// <c>cadDocumentsOnly=false</c> опрашивает два entity set, скачивание PDF делает запрос
        /// метаданных и отдельный запрос за файлом.
        /// </summary>
        private readonly Action<string>? _onRequestIssued;

        // Версия в пути — часть URL конкретного OData-сервиса Windchill, а не общая версия
        // REST API целиком: у разных сервисов на одном сервере версии в URL не совпадают
        // (проверено на боевом сервере — CADDocumentMgmt на v1, DocMgmt на v3). По документации
        // EDM (метаданные) домена всегда доступны по Windchill/servlet/odata/<Domain>/$metadata —
        // см. https://support.ptc.com/help/windchill_rest_services/r2.0/en/windchill_rest_services/WCCG_RESTAPIsWRS.html
        // (после апгрейда Windchill сверяться по этому эндпоинту, если поиск сломался).
        private const string CadDocumentMgmtBasePath = "/Windchill/servlet/odata/v1/CADDocumentMgmt";
        private const string DocMgmtBasePath = "/Windchill/servlet/odata/v3/DocMgmt";

        /// <param name="onRequestIssued"> Необязательный приёмник лога запросов, см. <see cref="_onRequestIssued"/>. </param>
        public WindchillClient(string serverUrl, string username, string password, Action<string>? onRequestIssued = null)
        {
            _client = new HttpClient();
            _serverUrl = serverUrl.TrimEnd('/');
            _onRequestIssued = onRequestIssued;

            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Ищет объекты в Windchill по трём независимым критериям, которые можно сочетать
        /// (пустые/whitespace-only игнорируются; если заполнено несколько — комбинируются
        /// через AND):
        /// <list type="bullet">
        /// <item><paramref name="keyword"/> — комбинированный поиск: значение разбивается на
        /// отдельные слова, каждое слово ищется подстрокой (<c>contains</c>) в <c>Number</c>
        /// или в <c>Name</c>, слова между собой объединяются через AND. Так устроено поле
        /// "Деталь" в PartsInfoWindow, откуда обычно приходит <paramref name="keyword"/>: там
        /// хранится связка "Наименование Обозначение" одной строкой (например,
        /// "Заглушка НМГ48-03-509"), а у CADDocument Name и Number — разные поля, ни одно не
        /// содержит строку целиком.</item>
        /// <item><paramref name="name"/> / <paramref name="number"/> — точный поиск только по
        /// наименованию (<c>Name</c>) или только по обозначению (<c>Number</c>), без разбиения
        /// на слова. По умолчанию — точное совпадение (<c>eq</c>). Для подстрочного поиска
        /// используется <c>*</c> — та же нотация, что и у фильтра "Деталь" в гриде (см.
        /// <see cref="Infrastructure.Types.SearchPattern"/>): <c>*текст*</c> — подстрока где
        /// угодно, <c>*текст</c> — оканчивается на, <c>текст*</c> — начинается с.</item>
        /// </list>
        ///
        /// <paramref name="cadDocumentsOnly"/> задаёт, какие entity set опрашивать:
        /// <c>true</c> (по умолчанию) — только <c>CADDocumentMgmt/CADDocuments</c> (3D-модели,
        /// детали, сборки, чертежи — то, что Windchill называет "CAD документами"); <c>false</c>
        /// — дополнительно ещё и <c>DocMgmt/Documents</c> (обычные документы Windchill — на
        /// боевом сервере это служебные бумаги вроде извещений о несоответствии, не чертежи).
        ///
        /// Ко всем критериям добавляется условие <c>Latest eq true</c>: оба entity set хранят
        /// каждую версию/ревизию документа отдельной строкой, а показывать нужно только
        /// актуальную — так же, как это по умолчанию делает поиск в самом Windchill.
        ///
        /// Документация по доменам и синтаксису фильтров (портал техподдержки PTC, требует логин):
        /// <see href="https://support.ptc.com/help/windchill_rest_services/r2.2/en/windchill_rest_services/CADdocumentmgmtdomain.html">
        /// PTC CAD Document Management Domain</see>,
        /// <see href="https://support.ptc.com/help/windchill_rest_services/r1.6/en/windchill_rest_services/wccg_restapiaccessexamples_CADDocumentMgmt_getaspecificCADdocument.html">
        /// Retrieving a Specific CAD Document</see> (пример <c>GET .../CADDocuments('OR:wt.epm.EPMDocument:...')</c>
        /// — тот же паттерн ID, что в <see cref="DownloadPdfAsync"/>),
        /// <see href="https://support.ptc.com/help/windchill_rest_services/r2.6/en/windchill_rest_services/docmgmtdomain.html">
        /// PTC Document Management Domain</see> (сущность <c>Documents</c>),
        /// <see href="https://support.ptc.com/help/windchill_rest_services/r1.6/en/windchill_rest_services/filteringoptions.html">
        /// Support for $filter on Navigation Properties</see> (синтаксис <c>contains</c>/<c>startswith</c>/<c>endswith</c>,
        /// использованный в <see cref="BuildFieldCondition"/> и <see cref="BuildKeywordCondition"/>).
        /// </summary>
        /// <returns>
        /// Список найденных объектов (не больше <see cref="MaxResults"/> суммарно) и
        /// <c>Truncated</c> — true, если список обрезан лимитом <see cref="MaxResults"/> и
        /// реальных совпадений может быть больше.
        /// </returns>
        public async Task<(List<WncObject> Objects, bool Truncated)> SearchAsync(
            string? keyword, string? name, string? number, bool cadDocumentsOnly, CancellationToken cancellationToken)
        {
            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(keyword)) conditions.Add(BuildKeywordCondition(keyword));
            if (!string.IsNullOrWhiteSpace(name)) conditions.Add(BuildFieldCondition("Name", name));
            if (!string.IsNullOrWhiteSpace(number)) conditions.Add(BuildFieldCondition("Number", number));

            if (conditions.Count == 0)
                throw new ArgumentException("Нужно заполнить хотя бы одно поле поиска (ключевое слово, наименование или обозначение)");

            // Показываем только актуальную версию каждого документа — иначе каждая
            // версия/ревизия приходит отдельной строкой.
            conditions.Add("Latest eq true");
            var filter = string.Join(" and ", conditions);

            var cadObjects = await QueryCadDocumentsAsync(filter, cancellationToken).ConfigureAwait(false);
            var cadTruncated = cadObjects.Count >= MaxResults;
            if (cadDocumentsOnly)
                return (cadObjects, cadTruncated);

            var docObjects = await QueryDocumentsAsync(filter, cancellationToken).ConfigureAwait(false);
            var docTruncated = docObjects.Count >= MaxResults;
            var combinedRawCount = cadObjects.Count + docObjects.Count;
            var merged = cadObjects.Concat(docObjects).Take(MaxResults).ToList();
            return (merged, cadTruncated || docTruncated || combinedRawCount > MaxResults);
        }

        /// <summary>
        /// Находит PDF-представление документа и скачивает его во временную папку.
        ///
        /// PDF в Windchill — не свойство самого документа, а файл в <c>AdditionalFiles</c>
        /// одного из его <c>Representations</c> (это же представление открывается кнопкой
        /// "Открыть в Creo View" в веб-интерфейсе Windchill). Не у каждого объекта есть такое
        /// представление — например, у 3D-модели без опубликованного чертежа его может не
        /// быть; тогда возвращается null.
        ///
        /// Документация (портал техподдержки PTC, требует логин):
        /// <see href="https://support.ptc.com/help/windchill_rest_services/r1.7/en/windchill_rest_services/visualizationdomain.html">
        /// PTC Visualization Domain</see> — описывает сущность <c>Representations</c> и вложенную
        /// <c>AdditionalFiles</c> (URL/MimeType/FileName для скачивания не-CreoView файлов,
        /// включая PDF), на которую опирается разбор ответа ниже.
        /// </summary>
        /// <returns> Путь к скачанному файлу во временной папке, либо null, если PDF-представление не найдено. </returns>
        public async Task<string?> DownloadPdfAsync(WncObject obj, CancellationToken cancellationToken)
        {
            var basePath = obj.IsCadDocument ? CadDocumentMgmtBasePath : DocMgmtBasePath;
            var entitySet = obj.IsCadDocument ? "CADDocuments" : "Documents";
            var url = $"{_serverUrl}{basePath}/{entitySet}('{obj.ObjectId}')?$expand=Representations";

            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var root = JObject.Parse(json);

            foreach (var representation in (root["Representations"] as JArray) ?? new JArray())
            {
                foreach (var file in (representation["AdditionalFiles"] as JArray) ?? new JArray())
                {
                    if (file["MimeType"]?.ToString() != "application/pdf") continue;

                    var fileUrl = file["URL"]?.ToString();
                    if (string.IsNullOrEmpty(fileUrl)) continue;

                    var fileName = file["FileName"]?.ToString();
                    if (string.IsNullOrEmpty(fileName)) fileName = $"{obj.Id}.pdf";

                    // Второе обращение к серверу в рамках одного скачивания (после запроса
                    // метаданных выше) — логируется отдельной строкой, см. ReportRequest.
                    var stopwatch = Stopwatch.StartNew();
                    using var response = await _client.GetAsync(fileUrl, cancellationToken).ConfigureAwait(false);
                    ReportRequest(fileUrl, response.StatusCode, stopwatch.ElapsedMilliseconds);
                    if (!response.IsSuccessStatusCode) continue;

                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    var localPath = Path.Combine(Path.GetTempPath(), fileName);
                    await File.WriteAllBytesAsync(localPath, bytes, cancellationToken).ConfigureAwait(false);
                    return localPath;
                }
            }

            return null;
        }

        /// <summary> Каждое слово должно найтись хоть в Number, хоть в Name (слова — AND, поля внутри слова — OR). </summary>
        private static string BuildKeywordCondition(string value) =>
            string.Join(" and ", Tokenize(value).Select(t =>
            {
                var e = EscapeODataStringLiteral(t);
                return $"(contains(Number,'{e}') or contains(Name,'{e}'))";
            }));

        /// <summary>
        /// Точное совпадение по умолчанию (<c>eq</c>), с той же вайлдкард-нотацией, что и у
        /// SearchPattern (фильтр "Деталь" в гриде): <c>*текст*</c> → contains, <c>*текст</c> →
        /// endswith, <c>текст*</c> → startswith. В отличие от <see cref="BuildKeywordCondition"/>
        /// значение НЕ разбивается на слова — это одно значение одного поля (обозначение почти
        /// всегда без пробелов; наименование, даже многословное, вводится как один точный
        /// вариант, а для частичного нужен <c>*</c>).
        /// </summary>
        private static string BuildFieldCondition(string field, string value)
        {
            var startsStar = value.StartsWith('*');
            var endsStar = value.EndsWith('*');
            var trimmed = EscapeODataStringLiteral(value.Trim('*'));

            return (startsStar, endsStar) switch
            {
                (true, true) => $"contains({field},'{trimmed}')",
                (true, false) => $"endswith({field},'{trimmed}')",
                (false, true) => $"startswith({field},'{trimmed}')",
                (false, false) => $"{field} eq '{trimmed}'",
            };
        }

        /// <summary> '*' — тот же вайлдкард-синтаксис, что и у фильтра "Деталь" в гриде (см. SearchPattern) — здесь убирается, т.к. contains() и так ищет подстроку где угодно. </summary>
        private static string[] Tokenize(string value) =>
            value.Replace("*", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        private async Task<List<WncObject>> QueryCadDocumentsAsync(string filter, CancellationToken cancellationToken)
        {
            var select = "Number,Name,Version,State,TypeIcon,LastModified,CreatedOn,ID,VersionID";
            var url = $"{_serverUrl}{CadDocumentMgmtBasePath}/CADDocuments" +
                      $"?$filter={Uri.EscapeDataString(filter)}" +
                      $"&$select={select}" +
                      $"&$expand={Uri.EscapeDataString("Context($select=ID,Name)")}" +
                      $"&$top={MaxResults}";

            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            return ParseSearchResponse(json, hasTypeIcon: true);
        }

        /// <summary> Опрашивается только когда cadDocumentsOnly=false — см. <see cref="SearchAsync"/>. </summary>
        private async Task<List<WncObject>> QueryDocumentsAsync(string filter, CancellationToken cancellationToken)
        {
            var select = "Number,Name,Version,State,LastModified,CreatedOn,ID,VersionID";
            var url = $"{_serverUrl}{DocMgmtBasePath}/Documents" +
                      $"?$filter={Uri.EscapeDataString(filter)}" +
                      $"&$select={select}" +
                      $"&$expand={Uri.EscapeDataString("Context($select=ID,Name)")}" +
                      $"&$top={MaxResults}";

            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            return ParseSearchResponse(json, hasTypeIcon: false);
        }

        private async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            System.Net.HttpStatusCode? status = null;
            try
            {
                using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new UnauthorizedAccessException("Не удалось авторизоваться в Windchill (проверьте логин/пароль в cnc_wnc_cfg)");

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Write($"Ошибка поиска в Windchill: {response.StatusCode}\n{json}");
                    throw new HttpRequestException($"Ошибка поиска в Windchill: {response.StatusCode}");
                }

                return json;
            }
            finally
            {
                // В finally, а не после запроса: запрос, оборвавшийся по таймауту или ошибке сети,
                // для диагностики нагрузки важнее удачного — сервер его всё равно отработал.
                ReportRequest(url, status, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary> Формат строки лога см. <see cref="_onRequestIssued"/>; URL идёт последним, чтобы его удобно было скопировать целиком. </summary>
        private void ReportRequest(string url, System.Net.HttpStatusCode? status, long elapsedMs) =>
            _onRequestIssued?.Invoke($"[{(status is null ? "нет ответа" : ((int)status).ToString())}] {elapsedMs} мс {url}");

        private List<WncObject> ParseSearchResponse(string json, bool hasTypeIcon)
        {
            var root = JObject.Parse(json);
            var objects = new List<WncObject>();

            foreach (var obj in (root["value"] as JArray) ?? new JArray())
            {
                var name = obj["Name"]?.ToString() ?? "";
                var number = obj["Number"]?.ToString() ?? "";
                var version = obj["Version"]?.ToString() ?? "";
                var state = obj["State"]?["Display"]?.ToString() ?? obj["State"]?["Value"]?.ToString() ?? "";
                var type = hasTypeIcon ? (obj["TypeIcon"]?["Tooltip"]?.ToString() ?? "") : "Документ";
                var containerName = obj["Context"]?["Name"]?.ToString() ?? "";
                var containerOid = obj["Context"]?["ID"]?.ToString() ?? "";
                var objectId = obj["ID"]?.ToString() ?? "";
                var versionOid = obj["VersionID"]?.ToString() ?? objectId;
                var modifyDate = FormatODataDate(obj["LastModified"]?.ToString());
                var createDate = FormatODataDate(obj["CreatedOn"]?.ToString());

                var link = $"{_serverUrl}/Windchill/app/#ptc1/tcomp/infoPage?ContainerOid={containerOid}&oid={versionOid}&u8=1";

                // hasTypeIcon совпадает с "объект из CADDocuments" — оба query-метода
                // передают его согласованно (см. QueryCadDocumentsAsync/QueryDocumentsAsync).
                objects.Add(new WncObject(name, number, link, version, state, containerName, type, modifyDate, createDate, objectId, isCadDocument: hasTypeIcon));
            }

            return objects;
        }

        /// <summary> UTC ISO-8601 от Windchill ("2022-08-24T13:23:24Z") → московское время в привычном формате. Россия не переходит на летнее время с 2014-го, поэтому фиксированный +3 достаточен. </summary>
        private static string FormatODataDate(string? isoUtc)
        {
            if (string.IsNullOrEmpty(isoUtc)) return "";
            if (!DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
                return isoUtc;
            return utc.AddHours(3).ToString(Constants.DateTimeFormat);
        }

        /// <summary> В строковых литералах OData одинарная кавычка экранируется удвоением. </summary>
        private static string EscapeODataStringLiteral(string value) => value.Replace("'", "''");

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
