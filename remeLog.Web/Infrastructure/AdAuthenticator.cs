using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using remeLog.Core;

namespace remeLog.Web.Infrastructure;

/// <summary>
/// Проверка логина/пароля по AD того домена, в котором находится хост сервиса remeLog.Web,
/// и (опционально) членства в группе, которой разрешён внешний доступ. Бинд к контроллеру
/// домена выполняется изнутри ЛВС — сам AD наружу не открывается, наружу торчит только
/// HTTP(S) до этого процесса (через cloudflared/reverse-proxy).
/// </summary>
[SupportedOSPlatform("windows")]
public static class AdAuthenticator
{
    /// <param name="domain">Имя домена или контроллера, например "corp.local".</param>
    /// <param name="allowedGroup">
    /// SAM-имя группы, членам которой разрешён вход. Пустая строка/null — проверка группы пропускается
    /// (достаточно валидных доменных учётных данных).
    /// </param>
    public static bool TryAuthenticate(string domain, string allowedGroup, string userName, string password, out string? displayName)
    {
        displayName = null;

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return false;

        try
        {
            using var context = new PrincipalContext(ContextType.Domain, domain);

            if (!context.ValidateCredentials(userName, password))
                return false;

            using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName);
            if (user is null)
                return false;

            if (!string.IsNullOrWhiteSpace(allowedGroup))
            {
                using var group = GroupPrincipal.FindByIdentity(context, allowedGroup);
                if (group is null || !user.IsMemberOf(group))
                    return false;
            }

            displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? userName : user.DisplayName;
            return true;
        }
        catch (Exception ex)
        {
            Log.WriteError(ex, $"Ошибка проверки AD-учётных данных для \"{userName}\"");
            return false;
        }
    }
}
