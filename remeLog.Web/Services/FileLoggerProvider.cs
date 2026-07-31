using System.Collections.Concurrent;

namespace remeLog.Web.Services;

/// <summary>
/// Простой провайдер логов в файл (один файл на день, logs/yyyy-MM-dd.log) — как в
/// AiService: под службой консоли нет, без файла разбирать сбои будет нечем.
/// </summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal void Write(string line)
    {
        lock (_writeLock)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Логирование не должно ронять сервис — молча пропускаем сбой записи.
            }
        }
    }

    public void Dispose() => _loggers.Clear();

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var line = $"{DateTime.Now:HH:mm:ss.fff} [{LevelLabel(logLevel)}] {category}: {formatter(state, exception)}";
            if (exception != null)
                line += Environment.NewLine + exception;

            provider.Write(line);
        }

        private static string LevelLabel(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => "????",
        };
    }
}
