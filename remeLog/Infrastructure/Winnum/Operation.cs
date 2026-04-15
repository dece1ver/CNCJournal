using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Presentation;
using remeLog.Models;
using static remeLog.Infrastructure.Winnum.Types;

namespace remeLog.Infrastructure.Winnum
{
    public class Operation
    {
        private readonly ApiClient _client;
        private readonly Machine _machine;

        public Operation(ApiClient client, Machine machine)
        {
            _client = client;
            _machine = machine;
        }

        private static string FormatAppId(AppId id) =>
            $"winnum.org.app.WNApplicationInstance:{(int)id}";
        private static string FormatTagId(TagId id) =>
            $"winnum.org.tag.WNTag:{(int)id}";
        private string FormatWnId() =>
            $"winnum.org.product.WNProduct:{_machine.WnId}";

        public async Task<DateTime> GetPlatformDateTimeAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Получение времени Platform");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNCalendarHelper" },
                { "men", "getPlatformDateTime" }
            };
            var result = Parser.FromWinnumDateTime(Parser.ParseXmlItems(await _client.ExecuteRequestAsync(parameters)).First()["datetime"]);
            progress?.Report("Получено время Platform");
            return result;
        }

        public async Task<DateTime> GetCloudDateTimeAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Получение времени Cloud");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNCalendarHelper" },
                { "men", "getCloudDateTime" }
            };
            var result = Parser.FromWinnumDateTime(Parser.ParseXmlItems(await _client.ExecuteRequestAsync(parameters)).First()["datetime"]);
            progress?.Report("Получено время Cloud");
            return result;
        }

        /// <summary>
        /// Получение количества выполненных операций
        /// </summary>
        /// <param name="appId">Идентификатор приложения</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="tillDate">Конечная дата</param>
        /// <returns>Результат запроса в виде строки</returns>
        public async Task<string> GetOperationSummaryAsync(AppId appId, DateTime fromDate, DateTime tillDate, IProgress<string>? progress = null)
        {
            progress?.Report("Получение информации о выполненных операциях");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNCNCApplicationCompletedQtyHelper" },
                { "men", "getOperationSummary" },
                { "appid", FormatAppId(appId) },
                { "pid", FormatWnId() },
                { "from", fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "till", tillDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            };
            var result = await _client.ExecuteRequestAsync(parameters);
            progress?.Report("Получена информация о выполненных операциях");
            return result;
        }

        /// <summary>
        /// Получение количества завершенных и не завершенных операций
        /// </summary>
        /// <param name="appId">Идентификатор приложения</param>
        /// <param name="fromDate">Начальная дата</param>
        /// <param name="tillDate">Конечная дата</param>
        /// <returns>Результат запроса в виде строки</returns>
        public async Task<string> GetCompletedQtyAsync(AppId appId, DateTime fromDate, DateTime tillDate, IProgress<string>? progress = null)
        {
            progress?.Report("Получение информации о выполненном количестве");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNCNCApplicationCompletedQtyHelper" },
                { "men", "getCompletedQty" },
                { "appid", FormatAppId(appId) },
                { "pid", FormatWnId() },
                { "from", fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "till", tillDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            };
            var result = await _client.ExecuteRequestAsync(parameters);
            progress?.Report("Получена информация о выполненном количестве");
            return result;
        }

        public async Task<string> GetPriorityTagDurationAsync(AppId appId, DateTime fromDate, DateTime tillDate, IProgress<string>? progress = null)
        {
            progress?.Report("Получение информации о продолжительности выполнения тэгов");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNApplicationTagHelper" },
                { "men", "getPriorityTagDuration" },
                { "appid", FormatAppId(appId) },
                { "pid", FormatWnId() },
                { "from", fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "till", tillDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            };
            var result = await _client.ExecuteRequestAsync(parameters);
            progress?.Report("Получена информация о продолжительности выполнения тэгов");
            return result;
        }

        public async Task<string> GetTagCalculationValueAsync(AppId appId, TagId tagId, DateTime fromDate, DateTime tillDate, IProgress<string>? progress = null)
        {
            progress?.Report("Получение информации о значениях тэгов");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNApplicationTagHelper" },
                { "men", "getTagCalculationValue" },
                { "appid", FormatAppId(appId) },
                { "pid", FormatWnId() },
                { "tid", FormatTagId(tagId) },
                { "from", fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "till", tillDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            };
            var result = await _client.ExecuteRequestAsync(parameters);
            progress?.Report("Получена информация о значениях тэгов");
            return result;
        }

        public async Task<string> GetSimpleTagIntervalCalculationAsync(AppId appId, TagId tagId, DateTime fromDate, DateTime tillDate, IProgress<string>? progress = null, int? timeout = null)
        {
            progress?.Report("Получение информации об интервалах выполнения тэгов");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNApplicationTagHelper" },
                { "men", "getSimpleTagIntervalCalculation" },
                { "appid", FormatAppId(appId) },
                { "pid", FormatWnId() },
                { "tid", FormatTagId(tagId) },
                { "from", fromDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                { "till", tillDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }
            };
            if (timeout.HasValue) parameters.Add("timeout", timeout.Value.ToString());
            var resilt = await _client.ExecuteRequestAsync(parameters);
            progress?.Report("Получена информация об интервалах выполнения тэгов");
            return resilt;
        }

        public async Task<string> GetTagIntervalCalculationAsync(AppId appId, TagId tagId, DateTime fromDate, DateTime tillDate, IProgress<string>? progress = null, int? timeout = null, bool? check_success = null, bool? base_shift = null)
        {
            progress?.Report("Получение расширенной информации об интервалах выполнения тэгов");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNApplicationTagHelper" },
                { "men", "getTagIntervalCalculation" },
                { "appid", FormatAppId(appId) },
                { "pid", FormatWnId() },
                { "tid", FormatTagId(tagId) },
                { "from", fromDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                { "till", tillDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }
            };
            if (timeout.HasValue) parameters.Add("timeout", timeout.Value.ToString());
            if (check_success.HasValue) parameters.Add("check_success", check_success.Value.ToString());
            if (base_shift.HasValue) parameters.Add("base_shift", base_shift.Value.ToString());
            var result = await _client.ExecuteRequestAsync(parameters);
            progress?.Report("Получена расширенная информация об интервалах выполнения тэгов");
            return result;
        }

        public async Task<string> GetMachineInfo(AppId appId, IProgress<string>? progress = null)
        {
            progress?.Report("Получение информации о станке");
            var parameters = new Dictionary<string, string>
            {
                { "rpc", "winnum.views.url.WNApplicationHelper" },
                { "men", "isInUse" },
                { "appid", FormatAppId(appId) },
                { "pid", FormatWnId() }
            };
            var result = await _client.ExecuteRequestAsync(parameters);
            progress?.Report("Получена информация о станке");
            return result;
        }
    }
}