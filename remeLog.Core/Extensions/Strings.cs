using System;
using System.Text.RegularExpressions;

namespace remeLog.Core.Extensions
{
    /// <summary>
    /// Копии нужных методов <c>libeLog.Extensions.Strings</c> — используются в Part.cs,
    /// который теперь собирается без ссылки на libeLog (WPF).
    /// </summary>
    public static class Strings
    {
        public static bool EqualsOrdinalIgnoreCase(this string source, string other) =>
            string.Equals(source, other, StringComparison.OrdinalIgnoreCase);

        public static string NormalizedPartNameWithoutComments(this string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var cleaned = Regex.Replace(name, @"\([^)]*\)", string.Empty);
            cleaned = cleaned.Replace("\"", "");
            return Regex.Replace(cleaned, @"\s{2,}", " ").ToLower().Trim();
        }
    }
}
