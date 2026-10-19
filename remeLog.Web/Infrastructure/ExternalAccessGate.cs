namespace remeLog.Web.Infrastructure;

/// <summary>
/// Разделяет два входа в приложение по порту подключения:
/// ЛВС-порт (как раньше, из Kestrel:Endpoints:Http) остаётся полностью открытым — доверенная сеть.
/// Внешний порт (ExternalAccess:TunnelUrl, на который смотрит cloudflared) требует авторизации —
/// сюда попадают только запросы снаружи предприятия.
/// </summary>
public static class ExternalAccessGate
{
    public static IApplicationBuilder UseExternalAccessGate(this IApplicationBuilder app, int? tunnelPort)
    {
        if (tunnelPort is null)
            return app;

        return app.Use(async (context, next) =>
        {
            if (context.Connection.LocalPort == tunnelPort.Value
                && IsProtectedPath(context.Request.Path)
                && context.User.Identity?.IsAuthenticated != true)
            {
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/login?returnUrl={returnUrl}");
                return;
            }

            await next();
        });
    }

    /// <summary>
    /// Расширения файлов, которые отдаются без сессии: без них не отрисовать саму форму входа.
    /// Список закрытый — проверять «в пути есть точка» нельзя, иначе любой маршрут с точкой
    /// в параметре (например, дата 08.08.2026) молча оказался бы вне авторизации.
    /// </summary>
    private static readonly HashSet<string> PublicFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".mjs", ".map", ".json", ".webmanifest",
        ".ico", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp",
        ".woff", ".woff2", ".ttf", ".otf", ".eot"
    };

    // Логин-страница и статика должны быть доступны без сессии, иначе форму логина
    // будет некому показать. Всё остальное (сами страницы приложения и SignalR-хаб Blazor)
    // требует авторизации на внешнем порту.
    private static bool IsProtectedPath(PathString path)
    {
        if (path.StartsWithSegments("/login") || path.StartsWithSegments("/account"))
            return false;

        if (path.StartsWithSegments("/_framework") || path.StartsWithSegments("/_content")
            || path.StartsWithSegments("/Error") || path.StartsWithSegments("/not-found"))
            return false;

        var extension = Path.GetExtension(path.Value);
        if (!string.IsNullOrEmpty(extension) && PublicFileExtensions.Contains(extension))
            return false;

        return true;
    }
}
