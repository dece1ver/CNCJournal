using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace remeLog.Infrastructure.Winnum
{
    public class ApiClient
    {
        protected readonly HttpClient _httpClient;
        protected readonly string _baseUrl;
        protected readonly string _usr;
        protected readonly string _pwd;

        public ApiClient(string baseUrl, string usr, string pwd)
        {
            _baseUrl = baseUrl;
            _usr = usr;
            _pwd = pwd;
            _httpClient = new HttpClient();
        }

        public string BaseUrl => _baseUrl;
        public string Usr => _usr;
        public string Pwd => _pwd;

        internal async Task<string> ExecuteRequestAsync(Dictionary<string, string> parameters)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["usr"] = _usr;
            query["pwd"] = _pwd;

            foreach (var param in parameters)
                query[param.Key] = param.Value;

            var url = $"{_baseUrl}?{query}&mode=yes";
            Debug.WriteLine(url);
            url = Uri.UnescapeDataString(url);
            url = Uri.UnescapeDataString(url);
            url = Uri.UnescapeDataString(url);
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
    }
}
