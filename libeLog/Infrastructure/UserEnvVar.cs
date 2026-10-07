using libeLog.WinApi.pInvoke;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text.RegularExpressions;

namespace libeLog.Infrastructure;

/// <summary>
/// Команда SetUserEnvVar (окно экземпляров remeLog → eLog):
/// создание или обновление переменной окружения на уровне пользователя
/// на том ПК, где работает экземпляр-получатель.
/// </summary>
/// <remarks>
/// Общий код для обеих сторон, чтобы маска имени и формат payload не разъехались:
/// <list type="bullet">
/// <item>отправитель — вкладки окна экземпляров remeLog (собирает payload);</item>
/// <item>получатель — eLog AppPresenceService (разбирает payload и применяет).</item>
/// </list>
/// Формат payload — JSON: {"name":"ИМЯ","value":"значение"}.
/// Значение произвольное (может содержать «=», пробелы, кавычки), поэтому
/// одним текстом «ИМЯ=значение» его не передаём — там был бы неоднозначный разбор.
/// </remarks>
public static class UserEnvVar
{
    /// <summary>Тип команды в таблице remeLog_app_commands.</summary>
    public const string CommandType = "SetUserEnvVar";

    /// <summary>
    /// Маска имени: только латиница, цифры и _, первый символ — не цифра, длина 1–255.
    /// Проверяют обе стороны: отправитель — до постановки в очередь,
    /// получатель — перед применением.
    /// </summary>
    private static readonly Regex NameMask = new("^[A-Za-z_][A-Za-z0-9_]{0,254}$", RegexOptions.Compiled);

    /// <summary>Потолок длины значения — предел блока окружения Windows.</summary>
    private const int MaxValueLength = 32767;

    /// <summary>Проверяет имя переменной по маске.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && NameMask.IsMatch(name);

    /// <summary>Собирает payload команды из имени и значения (сторона remeLog).</summary>
    public static string BuildPayload(string name, string value) =>
        JsonConvert.SerializeObject(new { name, value });

    /// <summary>
    /// Разбирает payload команды (сторона eLog).
    /// Возвращает false с текстом причины в <paramref name="error"/>,
    /// если данные некорректны и применять нечего.
    /// </summary>
    public static bool TryParsePayload(string? payload, out string name, out string value, out string error)
    {
        name = string.Empty;
        value = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "пустой payload";
            return false;
        }

        // Отдельный try только вокруг разбора JSON: дальше идут обычные проверки,
        // исключения которых ловить не нужно.
        JObject parsed;
        try
        {
            parsed = JObject.Parse(payload);
        }
        catch (Exception ex)
        {
            error = $"не JSON: {ex.Message}";
            return false;
        }

        // Имя обязано быть строкой по маске. Не-строку (число, null, отсутствие поля)
        // считаем некорректными данными, а не приводим к строке молча.
        var rawName = parsed["name"]?.Type == JTokenType.String ? (string?)parsed["name"] : null;
        if (!IsValidName(rawName))
        {
            error = "имя не по маске (латиница, цифры, _; первый символ — не цифра)";
            return false;
        }

        // Значение — любая строка, в том числе пустая. Отсутствие значения (null)
        // или слишком длинное значение — некорректные данные.
        var rawValue = parsed["value"]?.Type == JTokenType.String ? (string?)parsed["value"] : null;
        if (rawValue is null)
        {
            error = "нет значения";
            return false;
        }
        if (rawValue.Length > MaxValueLength)
        {
            error = $"значение длиннее {MaxValueLength} символов";
            return false;
        }

        name = rawName!;
        value = rawValue;
        return true;
    }

    /// <summary>
    /// Применяет payload: пишет переменную на уровень пользователя и в окружение
    /// текущего процесса, затем оповещает систему (WM_SETTINGCHANGE), чтобы
    /// переменную увидели и процессы, запущенные после команды.
    /// </summary>
    /// <returns>
    /// Итог для колонки Result: «Применено: ИМЯ» либо «Данные некорректны: причина».
    /// Технические сбои записи (реестр недоступен и т.п.) возвращают «Ошибка: ...».
    /// </returns>
    public static string ApplyPayload(string? payload)
    {
        // Клиент валидирует прежде чем применять: сначала разбор, потом запись.
        if (!TryParsePayload(payload, out var name, out var value, out var error))
            return $"Данные некорректны: {error}";

        try
        {
            // User — постоянство (реестр HKCU\Environment, переживает перезапуск).
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
            // Process — чтобы текущий процесс eLog подхватил сразу, без перезапуска.
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            BroadcastEnvironmentChanged();
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }

        return $"Применено: {name}";
    }

    /// <summary>
    /// Рассылка WM_SETTINGCHANGE всем окнам: система и Проводник перечитывают окружение,
    /// новые процессы стартуют уже с новой переменной. Best effort — сама переменная
    /// к этому моменту уже записана, поэтому сбой рассылки глушим.
    /// </summary>
    private static void BroadcastEnvironmentChanged()
    {
        try
        {
            User32.SendMessageTimeout(
                User32.HWND_BROADCAST, WM.SETTINGCHANGE, IntPtr.Zero, "Environment",
                User32.SMTO_ABORTIFHUNG, 5000, out _);
        }
        catch
        {
            // Переменная уже записана — новые процессы её увидят и без рассылки.
        }
    }
}
