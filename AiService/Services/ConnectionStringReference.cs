using Microsoft.Extensions.Configuration;

namespace AiService.Services;

/// <summary>
/// Строка подключения к БД lives in appsettings.json НЕ сырым текстом, а ссылкой
/// на переменную окружения вида ${CNCJ_RO_CONSTR} (сырые строки в репозитории
/// запрещены). Резолвится один раз при старте (см. Program): сырая строка деплоя
/// и пустое значение пропускаются как есть, ссылка на несуществующую переменную
/// даёт пусто (= служба работает без прямой БД, как раньше).
/// </summary>
public static class ConnectionStringReference
{
    public static string Resolve(IConfiguration config)
    {
        var configured = config.GetValue<string>("Database:ConnectionString", "") ?? "";
        var raw = configured.Trim();
        if (!raw.StartsWith("${", StringComparison.Ordinal) || !raw.EndsWith("}", StringComparison.Ordinal))
            return configured;
        var name = raw.Substring(2, raw.Length - 3).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return "";
        return Environment.GetEnvironmentVariable(name) ?? "";
    }
}
