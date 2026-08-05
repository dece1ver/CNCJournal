using System.Collections.Concurrent;

namespace remeLog.Web.Infrastructure;

/// <summary>
/// Простой троттлинг попыток входа (по IP+логину), чтобы форма логина, выставленная наружу,
/// не была голым инструментом для перебора доменных паролей. Не замена политике блокировки в AD,
/// а дополнительный барьер на уровне приложения.
/// </summary>
public sealed class LoginAttemptLimiter
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    private sealed record Entry(int Count, DateTime FirstAttemptUtc);

    private readonly ConcurrentDictionary<string, Entry> _attempts = new();

    public bool IsLockedOut(string key)
    {
        if (!_attempts.TryGetValue(key, out var entry))
            return false;

        if (DateTime.UtcNow - entry.FirstAttemptUtc > LockoutWindow)
        {
            _attempts.TryRemove(key, out _);
            return false;
        }

        return entry.Count >= MaxAttempts;
    }

    public void RegisterFailure(string key)
    {
        _attempts.AddOrUpdate(
            key,
            _ => new Entry(1, DateTime.UtcNow),
            (_, existing) => DateTime.UtcNow - existing.FirstAttemptUtc > LockoutWindow
                ? new Entry(1, DateTime.UtcNow)
                : existing with { Count = existing.Count + 1 });
    }

    public void RegisterSuccess(string key) => _attempts.TryRemove(key, out _);
}
