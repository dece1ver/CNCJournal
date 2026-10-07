using System;
using System.Collections.Generic;
using System.Linq;

namespace remeLog.Core.Services
{
    /// <summary>
    /// Операторы смены одной строкой для cnc_shifts.Operators и шапки суточного
    /// отчёта. Уникальные непустые значения в порядке первого появления (детали
    /// приходят отсортированными по StartSetupTime — то есть по времени работы).
    /// </summary>
    public static class ShiftOperators
    {
        public const string Separator = ", ";

        public static string Join(IEnumerable<string?> operators) =>
            string.Join(Separator, Distinct(operators));

        public static List<string> Distinct(IEnumerable<string?> operators) =>
            operators
                .Select(o => o?.Trim() ?? "")
                .Where(o => o.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Строка для шапки суточного отчёта: один оператор — полное ФИО,
        /// несколько — «Фамилия И.О.» (полные ФИО в одну строку не влезают).
        /// </summary>
        public static string Format(IEnumerable<string?> operators)
        {
            var list = Distinct(operators);
            return list.Count <= 1
                ? string.Join(Separator, list)
                : string.Join(Separator, list.Select(ShortName));
        }

        /// <summary> «Мартынов Дмитрий Юрьевич» → «Мартынов Д.Ю.»; одно слово не трогаем. </summary>
        public static string ShortName(string? fullName)
        {
            var tokens = (fullName ?? "").Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return "";
            if (tokens.Length == 1) return tokens[0];
            var initials = string.Concat(tokens.Skip(1).Select(t => t[0] + "."));
            return $"{tokens[0]} {initials}";
        }
    }
}
