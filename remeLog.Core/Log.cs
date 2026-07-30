using System;

namespace remeLog.Core
{
    /// <summary>
    /// Логирующий seam — замена прямых вызовов <c>libeLog</c>-специфичного
    /// <c>remeLog.Infrastructure.Util.WriteLog</c> (завязан на System.Windows.Forms).
    /// remeLog при старте подключает сюда существующий <c>Util.WriteLog</c>
    /// (см. App.xaml.cs), остальные хосты (remeLog.Web) — свой обработчик.
    /// По умолчанию, если никто не подписался, запись уходит в никуда.
    /// </summary>
    public static class Log
    {
        public static Action<string> Write { get; set; } = _ => { };
        public static Action<Exception, string?> WriteError { get; set; } = (_, _) => { };
    }

    /// <summary>
    /// Seam для сохранения настроек — remeLog при старте подключает сюда
    /// <c>AppSettings.Save</c>, т.к. UpdateAppSettings живёт в remeLog.Core.
    /// </summary>
    public static class Persistence
    {
        public static Action Save { get; set; } = () => { };
    }
}
