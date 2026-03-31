using libeLog.Infrastructure;
using libeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QCTasks.Services;

/// <summary>
/// Почтовые уведомления, специфичные для QCTasks.
/// Знает о шаблонах ОТК и о том, откуда брать настройки SMTP.
/// Не содержит SMTP-механики — делегирует в <see cref="SmtpSender"/>.
/// </summary>
public class QcNotificationService
{
    private readonly AppSettings _settings;

    public QcNotificationService(AppSettings settings)
    {
        _settings = settings;
    }

    // ── IsAvailable ───────────────────────────────────────────────────────

    /// <summary>
    /// true — SMTP настроен и пароль присутствует в переменной окружения.
    /// Можно проверить перед отправкой, чтобы не показывать ошибку зря.
    /// </summary>
    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_settings.SmtpAddress) &&
        !string.IsNullOrWhiteSpace(_settings.SmtpUsername) &&
        SmtpSender.GetPasswordFromEnv(_settings.SmtpPasswordEnvVar) is { Length: > 0 };

    // ── Публичные методы ──────────────────────────────────────────────────

    /// <summary>
    /// Отправляет уведомление об отклонении детали.
    /// </summary>
    /// <param name="task">Задача ОТК.</param>
    /// <param name="comment">Обязательный комментарий контролёра.</param>
    /// <param name="recipients">Список адресатов.</param>
    /// <exception cref="InvalidOperationException">SMTP-пароль не найден в env.</exception>
    public void SendRejectionNotice(
        ProductionTaskData task,
        string comment,
        IReadOnlyList<string> recipients)
    {
        var html = BuildRejectionHtml(task, comment);
        SendViaSmtp("Контроль ОТК: деталь отклонена", html, recipients);
    }

    /// <summary>
    /// Отправляет уведомление о принятии детали (опционально, если настроено).
    /// </summary>
    public void SendAcceptanceNotice(
        ProductionTaskData task,
        string? comment,
        IReadOnlyList<string> recipients)
    {
        var html = BuildAcceptanceHtml(task, comment);
        SendViaSmtp("Контроль ОТК: деталь принята", html, recipients);
    }

    // ── Шаблоны ───────────────────────────────────────────────────────────

    private static string BuildRejectionHtml(ProductionTaskData task, string comment)
    {
        var sb = new StringBuilder();
        sb.Append(SmtpSender.Title("Деталь отклонена при контроле ОТК", color: "#d9534f"));
        sb.Append(SmtpSender.Hr);
        sb.Append(SmtpSender.Field("Деталь", task.PartName));
        sb.Append(SmtpSender.Field("М/Л", task.Order));
        sb.Append(SmtpSender.Field("Количество", task.PartsCount));
        AppendDateIfPresent(sb, task.Date);
        sb.Append(SmtpSender.Hr);
        sb.Append(SmtpSender.Field("Комментарий ОТК", comment));
        sb.Append(SmtpSender.Field("Время", DateTime.Now.ToString("HH:mm  dd.MM.yyyy")));
        return SmtpSender.BuildHtml(sb.ToString());
    }

    private static string BuildAcceptanceHtml(ProductionTaskData task, string? comment)
    {
        var sb = new StringBuilder();
        sb.Append(SmtpSender.Title("Деталь принята при контроле ОТК", color: "#5cb85c"));
        sb.Append(SmtpSender.Hr);
        sb.Append(SmtpSender.Field("Деталь", task.PartName));
        sb.Append(SmtpSender.Field("М/Л", task.Order));
        sb.Append(SmtpSender.Field("Количество", task.PartsCount));
        AppendDateIfPresent(sb, task.Date);
        if (!string.IsNullOrWhiteSpace(comment))
        {
            sb.Append(SmtpSender.Hr);
            sb.Append(SmtpSender.Field("Комментарий ОТК", comment));
        }
        sb.Append(SmtpSender.Field("Время", DateTime.Now.ToString("HH:mm  dd.MM.yyyy")));
        return SmtpSender.BuildHtml(sb.ToString());
    }

    private static void AppendDateIfPresent(StringBuilder sb, string? date)
    {
        if (!string.IsNullOrWhiteSpace(date) && date != "-")
            sb.Append(SmtpSender.Field("Дата задачи", date));
    }

    // ── Отправка ──────────────────────────────────────────────────────────

    private void SendViaSmtp(string subject, string html, IReadOnlyList<string> recipients)
    {
        var cfg = BuildConfig();
        SmtpSender.Send(cfg, subject, html, recipients,
            logError: msg => Console.Error.WriteLine($"[QcNotificationService] {msg}"));
    }

    private SmtpConfig BuildConfig()
    {
        var pwd = SmtpSender.GetPasswordFromEnv(_settings.SmtpPasswordEnvVar);
        if (string.IsNullOrEmpty(pwd))
            throw new InvalidOperationException(
                $"SMTP-пароль не установлен. Ожидается переменная окружения «{_settings.SmtpPasswordEnvVar}».");

        return new SmtpConfig(
            _settings.SmtpAddress,
            _settings.SmtpPort,
            Ssl: true,
            _settings.SmtpUsername,
            pwd);
    }
}
