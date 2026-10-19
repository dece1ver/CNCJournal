using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Services
{
    /// <summary>
    /// Держит конфигурацию подключения к Windchill (из <c>cnc_wnc_cfg</c>, см.
    /// <see cref="Util.SearchInWindchill"/>) и создаёт <see cref="WindchillClient"/> на каждый
    /// вызов <see cref="SearchDocumentsAsync"/>. Вся логика запроса — внутри
    /// <see cref="WindchillClient.SearchAsync"/> (REST/OData, см. документацию там).
    /// </summary>
    public class WindchillService
    {
        private readonly string _serverUrl;
        private readonly string _username;
        private readonly string _password;

        public WindchillService(string serverUrl, string username, string password)
        {
            _serverUrl = serverUrl;
            _username = username;
            _password = password;
        }

        /// <summary> Ищет объекты по ключевому слову/наименованию/обозначению. См. <see cref="WindchillClient.SearchAsync"/>. </summary>
        /// <param name="onRequestIssued"> Приёмник лога фактических HTTP-запросов, см. <see cref="WindchillClient"/>. </param>
        public async Task<(List<WncObject> Objects, bool Truncated)> SearchDocumentsAsync(
            string? keyword, string? name, string? number, bool cadDocumentsOnly, CancellationToken cancellationToken,
            Action<string>? onRequestIssued = null)
        {
            using var client = new WindchillClient(_serverUrl, _username, _password, onRequestIssued);
            return await client.SearchAsync(keyword, name, number, cadDocumentsOnly, cancellationToken);
        }

        /// <summary> Скачивает PDF-представление объекта во временную папку. См. <see cref="WindchillClient.DownloadPdfAsync"/>. </summary>
        /// <param name="onRequestIssued"> Приёмник лога фактических HTTP-запросов, см. <see cref="WindchillClient"/>. </param>
        public async Task<string?> DownloadPdfAsync(WncObject obj, CancellationToken cancellationToken,
            Action<string>? onRequestIssued = null)
        {
            using var client = new WindchillClient(_serverUrl, _username, _password, onRequestIssued);
            return await client.DownloadPdfAsync(obj, cancellationToken);
        }
    }
}
