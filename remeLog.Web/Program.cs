using remeLog.Core;
using remeLog.Infrastructure;
using remeLog.Web.Components;
using remeLog.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Под службой нет консоли — без файла разбирать сбои после старта будет нечем.
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(AppContext.BaseDirectory, "logs")));

// Позволяет тому же .exe работать и интерактивно (dotnet run), и как служба Windows —
// сама определяет режим запуска (через SCM или нет) и настраивает ContentRoot/Event Log.
builder.Host.UseWindowsService(options => options.ServiceName = "remeLog.Web");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHostedService<SettingsRefreshService>();

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

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
