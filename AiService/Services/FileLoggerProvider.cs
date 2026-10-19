using System.Collections.Concurrent;

namespace AiService.Services;

/// <summary>
/// Простой провайдер логов в файл (один файл на день, logs/yyyy-MM-dd.log). Раньше
/// логи шли только в консоль — при падении/недоступности консоли расследовать было
/// нечем (см. инцидент с 500 от Ollama 24.07.2026, разбирали по логам самой Ollama,
/// а не AiService). Уровни те же, что уже настроены в appsettings.json (Logging:LogLevel) —
/// фильтрация общая для всех провайдеров.
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
