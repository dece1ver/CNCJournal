using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using remeLog.Core;
using remeLog.Infrastructure;
using remeLog.Web.Components;
using remeLog.Web.Infrastructure;
using remeLog.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(AppContext.BaseDirectory, "logs")));

builder.Host.UseWindowsService(options => options.ServiceName = "remeLog.Web");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHostedService<SettingsRefreshService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name = "remeLog.Web.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<LoginAttemptLimiter>();

// Второй Kestrel-эндпоинт для внешнего доступа (например, через cloudflared tunnel на
// localhost). ЛВС-эндпоинт из appsettings (Kestrel:Http) не трогаем — он остаётся открытым.
var externalAccess = builder.Configuration.GetSection("ExternalAccess");
int? tunnelPort = null;
if (externalAccess.GetValue("Enabled", false))
{
    var tunnelUrl = externalAccess["TunnelUrl"] ?? "http://127.0.0.1:6970";
    if (Uri.TryCreate(tunnelUrl, UriKind.Absolute, out var tunnelUri) && IPAddress.TryParse(tunnelUri.Host, out var tunnelIp))
    {
        tunnelPort = tunnelUri.Port;
        builder.WebHost.ConfigureKestrel(options => options.Listen(tunnelIp, tunnelUri.Port));
    }
    else
    {
        Console.Error.WriteLine($"ExternalAccess:TunnelUrl некорректен (\"{tunnelUrl}\", хост должен быть IP-адресом) — внешний вход не поднят.");
    }
}

var app = builder.Build();

Log.Write = message => app.Logger.LogInformation("{Message}", message);
Log.WriteError = (ex, message) => app.Logger.LogError(ex, "{Message}", message ?? ex.Message);

LoadRemeLogConfig();
await Database.UpdateAppSettings();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseExternalAccessGate(tunnelPort);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/account/login", async (HttpContext context, LoginAttemptLimiter limiter) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var returnUrl = IsLocalUrl(form["returnUrl"].ToString()) ? form["returnUrl"].ToString() : "/";

    var key = $"{context.Connection.RemoteIpAddress}:{username.ToLowerInvariant()}";
    if (limiter.IsLockedOut(key))
    {
        context.Response.Redirect($"/login?error=lockout&returnUrl={Uri.EscapeDataString(returnUrl)}");
        return;
    }

    var domain = externalAccess["AdDomain"] ?? "";
    var allowedGroup = externalAccess["AllowedGroup"] ?? "";

    if (!OperatingSystem.IsWindows() || !AdAuthenticator.TryAuthenticate(domain, allowedGroup, username, password, out var displayName))
    {
        limiter.RegisterFailure(key);
        Log.Write($"remeLog.Web: неудачная попытка входа \"{username}\" с {context.Connection.RemoteIpAddress}");
        context.Response.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
        return;
    }

    limiter.RegisterSuccess(key);

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, displayName ?? username), new Claim(ClaimTypes.NameIdentifier, username)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) });

    Log.Write($"remeLog.Web: вход \"{username}\" с {context.Connection.RemoteIpAddress}");
    context.Response.Redirect(returnUrl);
});

app.MapPost("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Redirect("/login");
});

app.Run();

static bool IsLocalUrl(string? url) =>
    !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");

// Читает ConnectionString из того же config.json, что и WPF-клиент remeLog
// (C:\ProgramData\dece1ver\remeLog\config.json) — единый источник настроек БД.
static void LoadRemeLogConfig()
{
    const string configPath = @"C:\ProgramData\dece1ver\remeLog\config.json";
    if (!File.Exists(configPath))
    {
        Log.Write($"Файл конфигурации remeLog не найден: {configPath}");
        return;
    }

    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
    if (doc.RootElement.TryGetProperty("ConnectionString", out var cs))
        DomainSettings.ConnectionString = cs.GetString();
}
