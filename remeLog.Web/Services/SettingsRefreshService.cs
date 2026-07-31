using remeLog.Core;
using remeLog.Infrastructure;

namespace remeLog.Web.Services;

/// <summary>
/// Периодически обновляет DomainSettings (нормативы, праздники и т.п.) из БД. WPF-клиент
/// делает это на каждую загрузку окна (Util.UpdateAppSettingsAsync в LoadPartsAsync) —
/// у веб-хоста, который живёт неделями как служба, а не перезапускается на каждый показ
/// страницы, без этого настройки замёрзли бы на значениях с момента старта.
/// </summary>
public sealed class SettingsRefreshService(ILogger<SettingsRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await Database.UpdateAppSettings();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось обновить настройки из БД");
            }
        }
    }
}
