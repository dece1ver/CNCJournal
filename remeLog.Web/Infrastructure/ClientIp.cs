namespace remeLog.Web.Infrastructure;

/// <summary>
/// Определяет адрес клиента с учётом того, что внешние запросы приходят через cloudflared
/// с локального адреса: без разбора заголовков прокси у всех внешних посетителей был бы
/// один и тот же IP 127.0.0.1.
/// </summary>
public static class ClientIp
{
    private const string CloudflareHeader = "CF-Connecting-IP";
    private const string ForwardedForHeader = "X-Forwarded-For";

    /// <summary>
    /// Адрес клиента строкой — для журнала входов и ключа <see cref="LoginAttemptLimiter"/>.
    /// </summary>
    /// <param name="tunnelPort">
    /// Порт внешнего входа. Заголовки прокси учитываются только для запросов, пришедших на
    /// него: их ставит наш же cloudflared. На открытом ЛВС-порту заголовки подделываются
    /// кем угодно, и доверие к ним позволило бы обойти ограничение попыток входа.
    /// </param>
    public static string Resolve(HttpContext context, int? tunnelPort)
    {
        if (tunnelPort is not null && context.Connection.LocalPort == tunnelPort.Value)
        {
            var cloudflare = context.Request.Headers[CloudflareHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cloudflare))
                return cloudflare.Trim();

            // X-Forwarded-For — цепочка «клиент, прокси1, прокси2…», клиент первый.
            var forwarded = context.Request.Headers[ForwardedForHeader].FirstOrDefault();
            var first = forwarded?.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "неизвестен";
    }
}
