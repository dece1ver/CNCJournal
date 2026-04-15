using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libeLog.WinApi.AD
{
    public sealed record UserInfo(
    // Основное
    string UserName,
    string DisplayName,
    string FirstName,
    string LastName,
    string MiddleName,
    string Initials,
    // Должность и отдел
    string Title,
    string Department,
    string Company,
    string Division,
    string EmployeeId,
    string EmployeeType,
    // Контакты
    string Mail,
    string Phone,
    string Mobile,
    string Fax,
    string IPPhone,
    string HomePage,
    // Расположение
    string Office,
    string StreetAddress,
    string City,
    string State,
    string PostalCode,
    string Country,
    // Руководитель и подчинение
    string Manager,
    // Аккаунт
    string SamAccountName,
    string UserPrincipalName,
    string DistinguishedName,
    DateTime? AccountExpires,
    DateTime? LastLogon,
    DateTime? PasswordLastSet,
    DateTime? Created,
    DateTime? Modified,
    bool IsLocal,
    bool IsEnabled,
    // Группы
    IReadOnlyList<string> Groups,
    // Фото
    byte[]? Photo);
}