using libeLog.Infrastructure.Enums;
using libeLog.WinApi.AD;
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using static Google.Apis.Sheets.v4.SpreadsheetsResource.DeveloperMetadataResource;

namespace libeLog.Infrastructure;

public static class Utils
{
    public static string ConvertColumnIndexToLetters(int columnIndex)
    {
        if (columnIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(columnIndex), "Column index cannot be negative.");

        string letters = "";
        while (columnIndex >= 0)
        {
            int remainder = columnIndex % 26;
            letters = (char)('A' + remainder) + letters;
            columnIndex = (columnIndex / 26) - 1;
        }
        return letters;
    }

    [SupportedOSPlatform("windows")]
    public static UserInfo GetUserInfo()
    {
        string userName = Environment.UserName;
        string domainName = Environment.UserDomainName;
        bool isLocal = domainName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);

        if (isLocal)
        {
            try
            {
                string query = $"SELECT FullName FROM Win32_UserAccount WHERE Name='{userName.Replace("'", "''")}' AND LocalAccount=True";
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
                {
                    var fullName = obj["FullName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(fullName))
                        return CreateEmpty(userName, fullName, isLocal: true);
                }
            }
            catch { }
            return CreateEmpty(userName, userName, isLocal: true);
        }

        SearchResult? result = null;
        try
        {
            using var entry = new DirectoryEntry($"LDAP://{domainName}");
            using var adSearcher = new DirectorySearcher(entry)
            {
                Filter = $"(&(objectClass=user)(sAMAccountName={userName}))"
            };
            adSearcher.PropertiesToLoad.AddRange(new string[] {
                "displayName", "givenName", "sn", "middleName", "initials",
        "title", "department", "company", "division", "employeeID", "employeeType",
        "mail", "telephoneNumber", "mobile", "facsimileTelephoneNumber", "ipPhone", "wWWHomePage",
        "physicalDeliveryOfficeName", "streetAddress", "l", "st", "postalCode", "co",
        "manager",
        "sAMAccountName", "userPrincipalName", "distinguishedName",
        "accountExpires", "lastLogon", "pwdLastSet", "whenCreated", "whenChanged",
        "userAccountControl",
        "memberOf",
        "thumbnailPhoto"
            });
            result = adSearcher.FindOne();
        }
        catch { }

        if (result == null)
            return CreateEmpty(userName, userName, isLocal: false);

        // Каждый геттер изолирован — падение одного не роняет остальные
        string Get(string key)
        {
            try { return result.Properties[key]?[0]?.ToString() ?? ""; }
            catch { return ""; }
        }

        DateTime? GetDate(string key)
        {
            try
            {
                if (result.Properties[key]?[0] is not { } raw) return null;
                return raw switch
                {
                    DateTime dt => dt,
                    long ticks when ticks > 0 && ticks != long.MaxValue => DateTime.FromFileTime(ticks),
                    _ => null
                };
            }
            catch { return null; }
        }

        bool isEnabled = false;
        try { isEnabled = result.Properties["userAccountControl"]?[0] is int uac && (uac & 2) == 0; }
        catch { }

        var groups = Array.Empty<string>() as IReadOnlyList<string>;
        try
        {
            groups = result.Properties["memberOf"]
                .Cast<string>()
                .Select(dn => dn.Split(',')[0].Replace("CN=", "").Trim())
                .ToList()
                .AsReadOnly();
        }
        catch { }

        byte[]? photo = null;
        try
        {
            if (result.Properties["thumbnailPhoto"]?.Count > 0)
                photo = (byte[])result.Properties["thumbnailPhoto"][0];
        }
        catch { }

        string manager = "";
        try
        {
            var raw = result.Properties["manager"]?[0]?.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                manager = raw.Split(',')[0].Replace("CN=", "").Trim();
        }
        catch { }

        string displayName = "";
        try { displayName = Get("displayName") is { Length: > 0 } dn ? dn : userName; }
        catch { displayName = userName; }

        return new UserInfo(
            UserName: userName,
            DisplayName: displayName,
            FirstName: Get("givenName"),
            LastName: Get("sn"),
            MiddleName: Get("middleName"),
            Initials: Get("initials"),
            Title: Get("title"),
            Department: Get("department"),
            Company: Get("company"),
            Division: Get("division"),
            EmployeeId: Get("employeeID"),
            EmployeeType: Get("employeeType"),
            Mail: Get("mail"),
            Phone: Get("telephoneNumber"),
            Mobile: Get("mobile"),
            Fax: Get("facsimileTelephoneNumber"),
            IPPhone: Get("ipPhone"),
            HomePage: Get("wWWHomePage"),
            Office: Get("physicalDeliveryOfficeName"),
            StreetAddress: Get("streetAddress"),
            City: Get("l"),
            State: Get("st"),
            PostalCode: Get("postalCode"),
            Country: Get("co"),
            Manager: manager,
            SamAccountName: Get("sAMAccountName"),
            UserPrincipalName: Get("userPrincipalName"),
            DistinguishedName: Get("distinguishedName"),
            AccountExpires: GetDate("accountExpires"),
            LastLogon: GetDate("lastLogon"),
            PasswordLastSet: GetDate("pwdLastSet"),
            Created: GetDate("whenCreated"),
            Modified: GetDate("whenChanged"),
            IsLocal: false,
            IsEnabled: isEnabled,
            Groups: groups,
            Photo: photo);
    }

    private static UserInfo CreateEmpty(string userName, string displayName, bool isLocal) => new(
    UserName: userName,
    DisplayName: displayName,
    FirstName: "",
    LastName: "",
    MiddleName: "",
    Initials: "",
    Title: "",
    Department: "",
    Company: "",
    Division: "",
    EmployeeId: "",
    EmployeeType: "",
    Mail: "",
    Phone: "",
    Mobile: "",
    Fax: "",
    IPPhone: "",
    HomePage: "",
    Office: "",
    StreetAddress: "",
    City: "",
    State: "",
    PostalCode: "",
    Country: "",
    Manager: "",
    SamAccountName: userName,
    UserPrincipalName: "",
    DistinguishedName: "",
    AccountExpires: null,
    LastLogon: null,
    PasswordLastSet: null,
    Created: null,
    Modified: null,
    IsLocal: isLocal,
    IsEnabled: true,
    Groups: Array.Empty<string>(),
    Photo: null);


    public static List<string> ReadReceiversFromFile(ReceiversType receiversType, string path)
    {
        if (!File.Exists(path))
            return new List<string>();

        var receivers = new List<string>();
        ReceiversType? currentSection = null;

        foreach (var line in File.ReadLines(path))
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            if (IsSection(trimmedLine))
            {
                currentSection = ParseSection(trimmedLine);
                continue;
            }

            if (currentSection == receiversType)
            {
                receivers.Add(trimmedLine);
            }
        }

        return receivers;
    }
    private static bool IsSection(string line)
    {
        return line.StartsWith('[') && line.EndsWith(']') && line.Length > 2;
    }

    private static ReceiversType? ParseSection(string line)
    {
        var sectionName = line.Replace(" ", "")[1..^1];
        return Enum.TryParse<ReceiversType>(sectionName, true, out var section)
            ? section
            : null;
    }
}
