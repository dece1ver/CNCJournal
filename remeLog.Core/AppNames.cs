namespace remeLog.Core
{
    /// <summary>
    /// Идентификаторы приложений в таблицах присутствия и команд
    /// (remeLog_app_presence.Application, remeLog_app_commands.TargetApplication).
    /// Значения зашиты и в eLog (eLog.Infrastructure.Services.AppPresenceService), который
    /// не ссылается на remeLog.Core — менять их можно только синхронно в обоих местах.
    /// </summary>
    public static class AppNames
    {
        public const string RemeLog = "remeLog";
        public const string ELog = "eLog";
    }
}
