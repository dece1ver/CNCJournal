using eLog.Infrastructure.Extensions;
using eLog.Models;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace eLog.Infrastructure;

/// <summary>
/// Уведомления, специфичные для eLog: формирует шаблоны писем
/// и делегирует отправку в <see cref="SmtpSender"/>.
/// Не содержит SMTP-механики — только доменную логику.
/// </summary>
public static class Email
{
    public static readonly IReadOnlyDictionary<string, ReceiversType[]> RecieversGroups =
        new Dictionary<string, ReceiversType[]>
        {
            ["Технологи"] = new[] { ReceiversType.ProcessEngineeringDepartment },
            ["Руководители цеха"] = new[] { ReceiversType.ProductionSupervisors },
            ["Инструментальный склад"] = new[] { ReceiversType.ToolStorage },
            ["Технологи и руководители цеха"] = new[]
            {
                ReceiversType.ProcessEngineeringDepartment,
                ReceiversType.ProductionSupervisors,
            },
        };

    /// <summary>Уведомление о длительной наладке.</summary>
    public static bool SendLongSetupNotify(Part part, int limitMinutes)
    {
        try
        {
            var html = BuildLongSetupHtml(part, limitMinutes);
            SendViaSmtp("Уведомление о длительной наладке", html, AppSettings.LongSetupsMailRecievers);
            return true;
        }
        catch (Exception ex)
        {
            Util.WriteLog(ex);
            return false;
        }
    }

    /// <summary>Уведомление о нехватке инструмента.</summary>
    public static bool SendToolSearchComment(Part part, string comment)
    {
        try
        {
            var html = BuildToolSearchHtml(part, comment);
            SendViaSmtp("Уведомление об инструменте", html,
                AppSettings.ToolSearchMailRecievers);
            return true;
        }
        catch (Exception ex)
        {
            Util.WriteLog(ex);
            return false;
        }
    }

    /// <summary>Произвольное сообщение от оператора.</summary>
    public static void SendMessage(Part part, string message, List<string> receivers)
    {
        var html = BuildOperatorMessageHtml(part, message);
        SendViaSmtp("Сообщение от оператора", html, receivers);
    }

    private static string BuildLongSetupHtml(Part part, int limitMinutes)
    {
        var sb = new StringBuilder();
        sb.Append(SmtpSender.Title(
            $"Внимание: процесс наладки превысил {limitMinutes.FormattedMinutes()}!"));
        sb.Append(SmtpSender.Hr);
        sb.Append(SmtpSender.Field("Станок", AppSettings.Instance.Machine?.Name));
        sb.Append(SmtpSender.Field("Оператор", part.Operator.FullName));
        sb.Append(SmtpSender.Field("Деталь", part.FullName.TrimLen(70)));
        sb.Append(SmtpSender.Field("Установка", part.Setup.ToString()));
        sb.Append(SmtpSender.Field("М/Л", part.Order));
        sb.Append(SmtpSender.Field("Начало наладки", part.StartSetupTime.ToString()));
        sb.Append(SmtpSender.Field("Норматив наладки", part.SetupTimePlan.ToString()));
        sb.Append(SmtpSender.Field("Лимит наладки", limitMinutes.ToString()));

        if (part.DownTimes.Any())
        {
            sb.Append(SmtpSender.Hr);
            sb.Append(@"<p style=""margin:0 0 6px;font-size:16px;font-weight:bold;color:#120085"">Простои:</p>");
            foreach (var dt in part.DownTimes)
            {
                var end = dt.EndTime == DateTime.MinValue ? "..." : $"{dt.EndTime:t}";
                sb.Append(SmtpSender.Field(dt.Name, $"{dt.StartTime:t} — {end}"));
            }
        }

        return SmtpSender.BuildHtml(sb.ToString());
    }

    private static string BuildToolSearchHtml(Part part, string comment)
    {
        var sb = new StringBuilder();
        sb.Append(SmtpSender.Title("Внимание: оператору не хватает инструмента!"));
        sb.Append(SmtpSender.Hr);
        sb.Append(SmtpSender.Field("Станок", AppSettings.Instance.Machine?.Name));
        sb.Append(SmtpSender.Field("Оператор", part.Operator.FullName));
        sb.Append(SmtpSender.Field("Деталь", part.FullName.TrimLen(70)));
        sb.Append(SmtpSender.Field("Что нужно", comment));
        return SmtpSender.BuildHtml(sb.ToString());
    }

    private static string BuildOperatorMessageHtml(Part part, string message)
    {
        var sb = new StringBuilder();
        sb.Append(SmtpSender.Title("Внимание: оператор отправляет сообщение!"));
        sb.Append(SmtpSender.Hr);
        sb.Append(SmtpSender.Field("Станок", AppSettings.Instance.Machine?.Name));
        sb.Append(SmtpSender.Field("Оператор", part.Operator.FullName));
        sb.Append(SmtpSender.Field("Деталь", part.FullName.TrimLen(70)));
        sb.Append(SmtpSender.Field("Установка", part.Setup.ToString()));
        sb.Append(SmtpSender.Field("М/Л", part.Order));
        sb.Append(SmtpSender.Field("Сообщение", message));
        return SmtpSender.BuildHtml(sb.ToString());
    }

    private static void SendViaSmtp(string subject, string html, List<string> recipients)
    {
        var cfg = BuildSmtpConfig();
        SmtpSender.Send(cfg, subject, html, recipients, logError: WriteLog);
    }

    private static SmtpConfig BuildSmtpConfig()
    {
        var cfg = AppSettings.Instance;
        var pwd = SmtpSender.GetPasswordFromEnv(AppSettings.SmtpPasswordEnvVar);
        if (string.IsNullOrEmpty(pwd))
            throw new InvalidOperationException("SMTP-пароль не установлен в переменной окружения.");
        return new SmtpConfig(cfg.SmtpAddress, cfg.SmtpPort, Ssl: true,
            cfg.SmtpUsername, pwd);
    }

    private static void WriteLog(string message) =>
        Util.WriteLog(new Exception(message));
}