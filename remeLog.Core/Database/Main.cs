using System.Text.Json;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
