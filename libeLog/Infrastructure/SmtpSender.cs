using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace libeLog.Infrastructure;

/// <summary>Конфигурация SMTP-подключения.</summary>
public record SmtpConfig(
    string Address,
    int Port,
    bool Ssl,
    string Username,
    string Password);

/// <summary>
/// Низкоуровневый SMTP-отправщик.
/// Не знает ни об AppSettings, ни о шаблонах, ни о доменной логике.
/// </summary>
public static class SmtpSender
{
    /// <summary>
    /// Отправляет HTML-письмо. Сначала пробует с <see cref="SmtpConfig.Ssl"/> = true,
    /// при ошибке повторяет с SSL = false.
    /// Бросает исключение с кратким user-friendly сообщением;
    /// детали пишутся через <paramref name="logError"/>.
    /// </summary>
    public static void Send(
        SmtpConfig config,
        string subject,
        string htmlBody,
        IReadOnlyList<string> recipients,
        Action<string>? logError = null)
    {
        try
        {
            SendInternal(config, subject, htmlBody, recipients);
        }
        catch
        {
            var fallback = config with { Ssl = !config.Ssl };
            SendInternal(fallback, subject, htmlBody, recipients, logError);
        }
    }

    /// <summary>
    /// Читает SMTP-пароль из пользовательской переменной окружения.
    /// Имя переменной задаётся в AppSettings каждого приложения.
    /// </summary>
    public static string? GetPasswordFromEnv(string variableName) =>
        Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User);

    /// <summary>
    /// Оборачивает <paramref name="innerHtml"/> в стандартную карточку:
    /// белый блок, тень, ограниченная ширина, стандартный подвал.
    /// </summary>
    /// <param name="innerHtml">Содержимое внутри карточки (строки &lt;p&gt;, заголовки и т.п.).</param>
    /// <param name="footerNote">Дополнительный текст перед системной строкой подвала (необязательно).</param>
    public static string BuildHtml(string innerHtml, string? footerNote = null)
    {
        var footer = new StringBuilder();
        footer.Append(@"<hr style=""border:none;border-top:1px solid #ddd;margin:15px 0"">");

        if (!string.IsNullOrWhiteSpace(footerNote))
        {
            footer.Append($@"<p style=""font-size:12px;color:#555;text-align:center;margin:6px 0"">{footerNote}</p>");
        }

        footer.Append($@"<p style=""font-size:11px;text-align:center;color:#777;margin-top:8px"">
            [{Environment.UserDomainName}/{Environment.UserName}@{Environment.MachineName}]:
            Это сообщение сформировано автоматически, не отвечайте на него.
        </p>");

        return $@"
<html>
<body style=""font-family:Calibri,sans-serif;color:#333"">
  <div style=""max-width:380px;margin:0 auto;padding:18px;border:1px solid #ddd;
               border-radius:8px;box-shadow:0 2px 8px rgba(0,0,0,.1)"">
    {innerHtml}
    {footer}
  </div>
</body>
</html>";
    }

    /// <summary>Стандартная строка-разделитель между секциями карточки.</summary>
    public static string Hr =>
        @"<hr style=""border:none;border-top:1px solid #ddd;margin:15px 0"">";

    /// <summary>Строка-поле: <c>&lt;p&gt;&lt;strong&gt;Label:&lt;/strong&gt; Value&lt;/p&gt;</c>.</summary>
    public static string Field(string label, string? value) =>
        $@"<p style=""margin:6px 0""><strong>{label}:</strong> {value}</p>";

    /// <summary>Крупный цветной заголовок карточки.</summary>
    public static string Title(string text, string color = "#d9534f") =>
        $@"<p style=""font-size:18px;font-weight:bold;color:{color};text-align:center"">{text}</p>";

    private static void SendInternal(
        SmtpConfig config,
        string subject,
        string htmlBody,
        IReadOnlyList<string> recipients,
        Action<string>? logError = null)
    {
        using var mail = new MailMessage();
        mail.From = new MailAddress(config.Username, "Уведомлятель");
        mail.Subject = subject;
        mail.Body = htmlBody;
        mail.IsBodyHtml = true;

        foreach (var r in recipients) mail.To.Add(r);

        using var smtp = new SmtpClient(config.Address, config.Port)
        {
            Credentials = new NetworkCredential(config.Username, config.Password),
            EnableSsl = config.Ssl
        };

        string? certLog = null;

        bool CertCallback(
            object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
        {
            if (chain is { ChainStatus.Length: > 0 } ch)
            {
                var sb = new StringBuilder("Ошибка проверки SSL-сертификата:\n");
                var c2 = cert as X509Certificate2 ?? new X509Certificate2(cert!);
                sb.AppendLine($"→ Subject: {c2.Subject}");
                sb.AppendLine($"→ Действителен до: {c2.NotAfter:yyyy-MM-dd}");
                foreach (var s in ch.ChainStatus)
                    sb.AppendLine($"   - {s.Status}: {s.StatusInformation.Trim()}");
                certLog = sb.ToString();
            }
            return false;
        }

        var oldCb = ServicePointManager.ServerCertificateValidationCallback;
        ServicePointManager.ServerCertificateValidationCallback = CertCallback;

        try
        {
            smtp.Send(mail);
        }
        catch (AuthenticationException ex)
        {
            logError?.Invoke(
                $"AuthenticationException → {config.Address}:{config.Port}\n{ex.Message}\n{certLog}");
            throw new AuthenticationException(
                "Не удалось установить защищённое соединение с почтовым сервером.", ex);
        }
        catch (SmtpException ex)
        {
            logError?.Invoke(
                $"SmtpException → {config.Address}:{config.Port}\n{ex.Message}");
            throw new SmtpException("Ошибка при отправке письма.", ex);
        }
        catch (Exception ex)
        {
            logError?.Invoke(
                $"{ex.GetType().Name} → {config.Address}:{config.Port}\n{ex.Message}");
            throw new Exception("Сбой при работе с почтой.", ex);
        }
        finally
        {
            ServicePointManager.ServerCertificateValidationCallback = oldCb;
        }
    }
}
