using libeLog.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows;

namespace libeLog.Extensions
{
    public static class Strings
    {
        /// <summary>
        /// Капитализация строки (для имен, фамилий и тд)
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static string Capitalize(this string source)
        {
            if (string.IsNullOrEmpty(source))
                return string.Empty;
            var letters = source.ToCharArray();
            for (int i = 0; i < letters.Length; i++)
            {
                if (i == 0)
                {
                    letters[i] = char.ToUpper(letters[i]);
                }
                else
                {
                    letters[i] = char.ToLower(letters[i]);
                }
            }
            letters[0] = char.ToUpper(letters[0]);
            return new string(letters);
        }

        /// <summary>
        /// Парсит строку в TimeSpan
        /// </summary>
        /// <param name="input">Строка с вводом</param>
        /// <param name="time">Спарсеный промежуток времени</param>
        /// <returns></returns>
        public static bool TimeParse(this string input, out TimeSpan time)
        {
            if (string.IsNullOrEmpty(input))
            {
                time = TimeSpan.Zero;
                return false;
            }
            input = input.Replace(",", ".");
            if (input.Length > 0)
            {
                if (input.Count(x => x == ':') == 1)
                {
                    var sTime = input.Split(':');
                    if (double.TryParse(sTime[0], out var minutes) &&
                        double.TryParse(sTime[1], out var seconds))
                    {
                        time = TimeSpan.FromSeconds(minutes * 60 + seconds);
                        return true;
                    }
                }
                else if (input.Count(x => x == ':') == 2)
                {
                    var sTime = input.Split(':');
                    if (double.TryParse(sTime[0], out var hours) &&
                        double.TryParse(sTime[1], out var minutes) &&
                        double.TryParse(sTime[2], out var seconds))
                    {
                        time = TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
                        return true;
                    }
                }
                else
                {
                    if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var minutes))
                    {
                        time = TimeSpan.FromMinutes(minutes);
                        return true;
                    }
                    time = TimeSpan.Zero;
                    return false;
                }
            }
            time = TimeSpan.Zero;
            return false;
        }

        /// <summary>
        /// Получает число из строки
        /// </summary>
        /// <param name="stringNumber">Строка для получения</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <param name="numberOption">Возвращаемое значение: Any для любого, OnlyPositive для положительных</param>
        /// <returns>Значение Double, при неудаче возвращает значение по умолчанию</returns>
        public static double GetDouble(this string stringNumber, double defaultValue = 0, GetNumberOption numberOption = GetNumberOption.OnlyPositive)
        {
            //if (stringNumber is "-") return double.NegativeInfinity;
            NumberFormatInfo numberFormat = new() { NumberDecimalSeparator = "," };
            if (!double.TryParse(stringNumber, NumberStyles.Any, numberFormat, out var result)) return defaultValue;
            return numberOption switch
            {
                GetNumberOption.OnlyPositive when result >= 0 => result,
                GetNumberOption.Any => result,
                _ => defaultValue
            };
        }

        /// <summary>
        /// Получает число из строки
        /// </summary>
        /// <param name="stringNumber">Строка для получения</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <param name="numberOption">Возвращаемое значение: только положительное или любое</param>
        /// <returns>Значение Int32, при неудаче возвращает значение по умолчанию</returns>
        public static int GetInt(this string stringNumber, int defaultValue = 0, GetNumberOption numberOption = GetNumberOption.OnlyPositive)
        {
            NumberFormatInfo numberFormat = new() { NumberDecimalSeparator = "," };
            if (!int.TryParse(stringNumber, NumberStyles.Any, numberFormat, out var result)) return defaultValue;
            if (numberOption == GetNumberOption.OnlyPositive && result > 0)
            {
                return result;
            }
            return defaultValue;
        }

        /// <summary>
        /// Назначает значение если строка пуста, если нет оставляет оригинальное, либо опционально применяет функцию для преобразования.
        /// </summary>
        /// <param name="checkString">Исходная строка.</param>
        /// <param name="value">Значение, возвращаемое если исходная строка пустая или null.</param>
        /// <param name="transform">Функция для преобразования оригинального значения, если строка не пустая.</param>
        /// <returns>
        /// Возвращает значение параметра <paramref name="value"/>, если исходная строка пустая или null;
        /// оригинальное значение строки, если не задано преобразование;
        /// либо результат применения функции <paramref name="transform"/>, если строка не пустая.
        /// </returns>
        public static string IfEmpty(this string checkString, string value, Func<string, string> transform = null!) 
        {
            if (string.IsNullOrEmpty(checkString)) return value;
            return transform != null ? transform(checkString) : checkString;
        }

        /// <summary>
        /// Обрезает слишком длинную строку по количеству символов
        /// </summary>
        /// <param name="str">Исходная строка</param>
        /// <param name="length">Желаемая длина строки (без учета "...")</param>
        /// <param name="ending">Добавлять ли многоточие (дает +3 к length)</param>
        /// <returns>Обрезанная строка</returns>
        public static string TrimLen(this string str, int length, bool ending = true)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Длина не может быть отрицательной.");

            if (str.Length <= length) return str;

            var trimmedLength = ending ? length - 3 : length;
            if (trimmedLength < 0) return "...";

            var result = str[..trimmedLength];
            return ending ? result + "..." : result;
        }

        /// <summary>
        /// Сравнивает текущую строку с другой строкой без учёта регистра, используя StringComparison.OrdinalIgnoreCase.
        /// </summary>
        /// <param name="source">Исходная строка.</param>
        /// <param name="other">Строка для сравнения.</param>
        /// <returns>true, если строки равны без учёта регистра, иначе false.</returns>
        public static bool EqualsOrdinalIgnoreCase(this string source, string other)
        {
            return string.Equals(source, other, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Проверяет наличие директории и права доступа к ней
        /// </summary>
        /// <param name="directoryPath"></param>
        /// <param name="requiredRights"></param>
        /// <returns>CheckDirectoryRightsResult</returns>
        public static CheckDirectoryRightsResult CheckDirectoryRights(this string directoryPath, FileSystemRights requiredRights)
        {
            if (string.IsNullOrEmpty(directoryPath)) return CheckDirectoryRightsResult.NotExists;
            DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
            if (directoryInfo.Exists)
            {
                try
                {
                    WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
                    WindowsPrincipal currentPrincipal = new WindowsPrincipal(currentIdentity);
                    DirectorySecurity directorySecurity = directoryInfo.GetAccessControl();
                    AuthorizationRuleCollection accessRules = directorySecurity.GetAccessRules(true, true, typeof(NTAccount));

                    foreach (FileSystemAccessRule rule in accessRules)
                    {
                        if (rule.AccessControlType == AccessControlType.Allow &&
                            (rule.FileSystemRights & requiredRights) != 0
                            && (currentPrincipal.IsInRole(rule.IdentityReference.Value)
                            || rule.IdentityReference.Value == currentIdentity.Name))
                        {
                            return CheckDirectoryRightsResult.HasAccess;
                        }
                    }

                    return CheckDirectoryRightsResult.NoAccess;
                }
                catch
                {
                    return CheckDirectoryRightsResult.Error;
                }
            }

            return CheckDirectoryRightsResult.NotExists;
        }

        public static (DbResult result, string message) CheckDbConnection(this string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return (DbResult.Error, "Строка подключения не инициализирована");

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    return (DbResult.Ok, "Ok");
                }
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    -1 => (DbResult.Error, Constants.StatusTips.NoConnectionToDb),
                    18456 => (DbResult.AuthError, Constants.StatusTips.AuthFailedToDb),
                    _ => (DbResult.Error, $"Ошибка БД №{sqlEx.Number}"),
                };
            }
            catch (Exception ex)
            {
                return (DbResult.Error, $"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Определяет, является ли исходный файл новее целевого файла.
        /// </summary>
        /// <param name="sourceFilePath">Путь к исходному файлу (source).</param>
        /// <param name="targetFilePath">Путь к целевому файлу (target).</param>
        /// <returns>Возвращает <c>true</c>, если исходный файл новее целевого файла; иначе <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Возникает, если <paramref name="sourceFilePath"/> или <paramref name="targetFilePath"/> равны <c>null</c> или содержат только пробельные символы.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Возникает, если файл, указанный в <paramref name="sourceFilePath"/> или <paramref name="targetFilePath"/>, не существует.
        /// </exception>
        public static bool IsFileNewerThan(this string sourceFilePath, string targetFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentNullException(nameof(sourceFilePath), "Путь к файлу не может быть null или пустым.");

            if (string.IsNullOrWhiteSpace(targetFilePath))
                throw new ArgumentNullException(nameof(targetFilePath), "Путь к файлу не может быть null или пустым.");

            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("Файл не существует.", sourceFilePath);

            if (!File.Exists(targetFilePath))
                throw new FileNotFoundException("Файл не существует.", targetFilePath);

            FileInfo sourceFileInfo = new(sourceFilePath);
            FileInfo targetFileInfo = new(targetFilePath);

            return sourceFileInfo.LastWriteTime > targetFileInfo.LastWriteTime;
        }

        /// <summary>
        /// Нормализованное имя детали
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string NormalizedPartName(this string name) =>
            name.ToLower().Replace("\"", "");

        /// <summary>
        /// Нормализованное имя детали без круглых скобок, кавычек и лишних пробелов
        /// </summary>
        /// <param name="name">Исходное имя детали</param>
        /// <returns>Очщенное и нормализованное имя</returns>
        public static string NormalizedPartNameWithoutComments(this string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Удаляем всё в круглых скобках вместе со скобками
            var cleaned = Regex.Replace(name, @"\([^)]*\)", string.Empty);

            // Удаляем кавычки
            cleaned = cleaned.Replace("\"", "");

            // Убираем множественные пробелы и приводим к нижнему регистру
            return Regex.Replace(cleaned, @"\s{2,}", " ").ToLower().Trim();
        }

        /// <summary>
        /// Проверяет возможность чтения указанного файла
        /// </summary>
        /// <param name="filePath">Полный путь к файлу</param>
        /// <returns>Результат проверки в виде значения FileCheckResult</returns>
        public static FileCheckResult CheckFileAccess(this string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return FileCheckResult.InvalidPath;

                using var stream = File.OpenRead(filePath);
                return FileCheckResult.Success;
            }
            catch (FileNotFoundException)
            {
                return FileCheckResult.FileNotFound;
            }
            catch (DirectoryNotFoundException)
            {
                return FileCheckResult.FileNotFound;
            }
            catch (UnauthorizedAccessException)
            {
                return FileCheckResult.AccessDenied;
            }
            catch (IOException ex) when ((ex.HResult & 0x0000FFFF) == 32)
            {
                return FileCheckResult.FileInUse;
            }
            catch (IOException)
            {
                return FileCheckResult.GeneralError;
            }
            catch (ArgumentException)
            {
                return FileCheckResult.InvalidPath;
            }
            catch
            {
                return FileCheckResult.GeneralError;
            }
        }

        /// <summary>
        /// Проверяет возможность записи в указанный файл
        /// </summary>
        /// <param name="filePath">Полный путь к файлу</param>
        /// <returns>Результат проверки в виде значения FileCheckResult</returns>
        public static FileCheckResult CheckFileWriteAccess(this string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return FileCheckResult.InvalidPath;

                using var stream = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                return FileCheckResult.Success;
            }
            catch (FileNotFoundException)
            {
                return FileCheckResult.FileNotFound;
            }
            catch (DirectoryNotFoundException)
            {
                return FileCheckResult.FileNotFound;
            }
            catch (UnauthorizedAccessException)
            {
                return FileCheckResult.AccessDenied;
            }
            catch (IOException ex) when ((ex.HResult & 0x0000FFFF) == 32)
            {
                return FileCheckResult.FileInUse;
            }
            catch (IOException)
            {
                return FileCheckResult.GeneralError;
            }
            catch (ArgumentException)
            {
                return FileCheckResult.InvalidPath;
            }
            catch
            {
                return FileCheckResult.GeneralError;
            }
        }

        /// <summary>
        /// Преобразует строку полного ФИО в формат "Фамилия И.О.".
        /// Ожидается строка из трёх слов: Имя Отчество Фамилия.
        /// </summary>
        /// <param name="fullName">Строка с полным ФИО.</param>
        /// <returns>
        /// ФИО в формате "Фамилия И.О.".
        /// Если строка некорректна — возвращается исходная строка.
        /// </returns>
        public static string ToShortFio(this string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return fullName;

            var parts = fullName
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 3)
                return fullName;

            string firstName = parts[1];
            string patronymic = parts[2];
            string lastName = parts[0];

            if (firstName.Length == 0 || patronymic.Length == 0 || lastName.Length == 0)
                return fullName;

            if (!char.IsLetter(firstName[0]) ||
                !char.IsLetter(patronymic[0]) ||
                !char.IsLetter(lastName[0]))
                return fullName;

            return $"{lastName} {firstName[0]}.{patronymic[0]}.";
        }
    }
}