using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace remeLog.Core.Services
{
    /// <summary>
    /// Критерии фильтрации PartsInfoWindow, вынесенные из VM-свойств в плоский DTO — тот же
    /// набор критериев сможет собрать любой другой хост (например remeLog.Web) из своих
    /// элементов управления, не завися от WPF-биндингов.
    /// </summary>
    public record PartsFilterCriteria(
        DateTime FromDate,
        DateTime ToDate,
        Shift ShiftFilter,
        string OperatorFilter,
        string PartNameFilter,
        string OrderFilter,
        string EngineerConclusionFilter,
        string EngineerCommentFilter,
        (string Op, int Value)? FinishedCountFilter,
        (string Op, int Value)? TotalCountFilter,
        int? SetupFilter,
        bool OnlySerialPartsFilter,
        IReadOnlyCollection<string> SerialPartNormalizedNames,
        IReadOnlyCollection<string> SelectedMachines,
        IReadOnlyCollection<FilterChip> ChipFilters);

    /// <summary>
    /// Построение SQL-условий из <see cref="PartsFilterCriteria"/> и фильтрация в памяти
    /// по InMemory-колонкам (<see cref="ColumnMeta.Predicate"/>) — вынесено из
    /// PartsInfoWindowViewModel.BuildConditions/ApplyInMemoryFilters без изменения логики.
    /// </summary>
    public static class PartsFilterService
    {
        public static string BuildConditions(PartsFilterCriteria c)
        {
            var sb = new StringBuilder();

            sb.AppendFormat("ShiftDate BETWEEN '{0}' AND '{1}' ", c.FromDate, c.ToDate);

            if (c.ShiftFilter is not { Type: ShiftType.All })
                sb.AppendFormat("AND Shift = '{0}' ", c.ShiftFilter.FilterText);

            AppendMultiValueCondition(sb, "Operator", c.OperatorFilter);
            AppendCondition(sb, "PartName", c.PartNameFilter);
            AppendMultiValueCondition(sb, "[Order]", c.OrderFilter);
            AppendCondition(sb, "EngineerConclusion", c.EngineerConclusionFilter);
            AppendCondition(sb, "EngineerComment", c.EngineerCommentFilter);

            if (c.FinishedCountFilter is { } finished)
                sb.AppendFormat("AND FinishedCount {0} {1} ", finished.Op, finished.Value);

            if (c.TotalCountFilter is { } total)
                sb.AppendFormat("AND totalCount {0} {1} ", total.Op, total.Value);

            if (c.SetupFilter != null)
                sb.AppendFormat("AND Setup = {0} ", c.SetupFilter);

            if (c.OnlySerialPartsFilter)
            {
                var serialNames = string.Join(", ", c.SerialPartNormalizedNames.Select(n => $"'{n}'"));
                sb.AppendFormat("AND NormalizedPartName IN ({0}) ", serialNames);
            }

            var machines = string.Join(", ", c.SelectedMachines.Distinct().Select(m => $"'{m}'"));
            sb.AppendFormat("AND Machine IN ({0}) ", machines);

            foreach (var chip in c.ChipFilters)
            {
                if (chip.IsInMemory) continue;
                var meta = PartColumnMeta.Map.Values.FirstOrDefault(m => m.SqlColumn == chip.SqlColumn);

                if (meta is null || meta.Kind == FilterKind.None)
                    continue;

                var values = chip.Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                if (values.Count == 0) continue;

                if (values.Count == 1)
                {
                    AppendChipCondition(sb, chip.SqlColumn, values[0], meta.Kind);
                }
                else if (meta.Kind == FilterKind.Text)
                {
                    var inList = string.Join(", ", values.Select(v => $"'{v.Replace("'", "''")}'"));
                    sb.AppendFormat("AND {0} IN ({1}) ", chip.SqlColumn, inList);
                }
                else
                {
                    sb.Append("AND (");
                    for (int i = 0; i < values.Count; i++)
                    {
                        if (i > 0) sb.Append(" OR ");
                        AppendChipConditionRaw(sb, chip.SqlColumn, values[i], meta.Kind);
                    }
                    sb.Append(") ");
                }
            }

            return sb.ToString();
        }

        /// <summary>Условие для получения истории изготовлений детали на всех станках за всё время.</summary>
        public static string BuildConditionsForPartForAllTime(string partName)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("ShiftDate BETWEEN '{0}' AND '{1}' ", new DateTime(2023, 1, 1), DateTime.Today);
            AppendCondition(sb, "PartName", partName);
            return sb.ToString();
        }

        /// <summary>Фильтрация уже загруженных деталей по InMemory-колонкам (не выражаются в SQL).</summary>
        public static List<Part> ApplyInMemoryFilters(List<Part> parts, IEnumerable<FilterChip> chipFilters)
        {
            var inMemoryChips = chipFilters.Where(c => c.IsInMemory).ToList();
            if (inMemoryChips.Count == 0) return parts;

            return parts.Where(p => inMemoryChips.All(chip =>
            {
                var meta = PartColumnMeta.Map.Values.FirstOrDefault(m => m.DisplayName == chip.DisplayName);
                if (meta?.Predicate is null) return true;

                var values = chip.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return values.Length == 0 || values.Any(v => meta.Predicate(p, v));
            })).ToList();
        }

        private static void AppendCondition(StringBuilder sb, string column, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var pattern = new SearchPattern(value);
                sb.AppendFormat("AND {0} {1} ", column, pattern);
            }
        }

        private static void AppendMultiValueCondition(StringBuilder sb, string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            if (value.Contains(';'))
            {
                var values = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(v => v.Trim())
                                  .Where(v => !string.IsNullOrWhiteSpace(v))
                                  .ToList();

                if (values.Count > 0)
                {
                    var patterns = values.Select(v => new SearchPattern(v)).ToList();

                    if (patterns.All(p => p.IsExactMatch))
                    {
                        var quotedValues = string.Join(", ", values.Select(v => $"'{v}'"));
                        sb.AppendFormat("AND {0} IN ({1}) ", column, quotedValues);
                    }
                    else
                    {
                        sb.Append("AND (");
                        for (int i = 0; i < patterns.Count; i++)
                        {
                            if (i > 0) sb.Append(" OR ");
                            sb.AppendFormat("{0} {1}", column, patterns[i]);
                        }
                        sb.Append(") ");
                    }
                }
            }
            else
            {
                AppendCondition(sb, column, value);
            }
        }

        private static void AppendChipCondition(StringBuilder sb, string column, string value, FilterKind kind)
        {
            sb.Append("AND ");
            AppendChipConditionRaw(sb, column, value, kind);
            sb.Append(' ');
        }

        private static void AppendChipConditionRaw(StringBuilder sb, string column, string value, FilterKind kind)
        {
            switch (kind)
            {
                case FilterKind.Text:
                    sb.AppendFormat("{0} = '{1}'", column, value.Replace("'", "''"));
                    break;

                case FilterKind.Number:
                    var numeric = value.TrimEnd('%', ' ');
                    if (double.TryParse(numeric,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out _))
                    {
                        sb.AppendFormat("{0} = {1}", column, numeric);
                    }
                    break;

                case FilterKind.Bool:
                    var boolVal = value is "True" or "1" or "true" or "✓" ? "1" : "0";
                    sb.AppendFormat("{0} = {1}", column, boolVal);
                    break;
            }
        }
    }
}
