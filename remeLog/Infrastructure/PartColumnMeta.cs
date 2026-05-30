using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace remeLog.Infrastructure
{
    public enum FilterKind
    {
        /// <summary>AND Column = 'value'</summary>
        Text,
        /// <summary>AND Column = value (без кавычек)</summary>
        Number,
        /// <summary>AND Column = 1 / = 0</summary>
        Bool,
        /// <summary>Фильтрация в памяти через предикат после загрузки из БД.</summary>
        InMemory,
        /// <summary>Вычисляемое, не фильтруется совсем.</summary>
        None,
    }

    /// <summary>
    /// Метаданные колонки. Predicate обязателен когда Kind == InMemory.
    /// </summary>
    public sealed class ColumnMeta
    {
        public string SqlColumn { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public FilterKind Kind { get; init; } = FilterKind.Text;

        /// <summary>
        /// Предикат для фильтрации в памяти: (деталь, значение_из_ячейки) → bool.
        /// Используется когда Kind == InMemory.
        /// </summary>
        public Func<Part, string, bool>? Predicate { get; init; }
    }

    public static class PartColumnMeta
    {
        public const string H_Machine = "Станок";
        public const string H_ShiftDate = "Дата";
        public const string H_Shift = "Смена";
        public const string H_Operator = "Оператор";
        public const string H_PartName = "Деталь";
        public const string H_Order = "М/Л";
        public const string H_TotalCount = "Всего по М/Л";
        public const string H_FinishedCount = "Выполнено";
        public const string H_DefectiveCount = "Брак";
        public const string H_Setup = "Установка";
        public const string H_StartSetupTime = "Начало наладки";
        public const string H_StartMachiningTime = "Начало изготовления";
        public const string H_EndMachiningTime = "Конец изготовления";
        public const string H_SetupTimePlan = "Норматив наладки";
        public const string H_SetupTimeFact = "Фактическая наладка";
        public const string H_SingleProductionTimePlan = "Норматив штучный";
        public const string H_MachiningTime = "Машинное время";
        public const string H_PartReplacementTime = "Время замены";
        public const string H_SingleProductionTime = "Штучное фактическое";
        public const string H_ProductionTimeFact = "Фактическое изготовление";
        public const string H_PlanForBatch = "Норматив на партию";
        public const string H_OperatorComment = "Комментарий оператора";
        public const string H_Problems = "Проблемы";
        public const string H_SetupDowntimes = "Простои в наладке";
        public const string H_MachiningDowntimes = "Простои в изготовлении";
        public const string H_PartialSetupTime = "Частичная наладка";
        public const string H_CreateNcProgramTime = "Написание УП";
        public const string H_MaintenanceTime = "Обслуживание";
        public const string H_ToolSearchingTime = "Поиск инструмента";
        public const string H_ToolChangingTime = "Замена инструмента";
        public const string H_MentoringTime = "Обучение";
        public const string H_ContactingDepartmentsTime = "Другие службы";
        public const string H_FixtureMakingTime = "Изготовление оснастки";
        public const string H_HardwareFailureTime = "Отказ оборудования";
        public const string H_SpecialDowntimeTime = "Специальный";
        public const string H_SpecifiedDowntimesRatio = "Отмеченные простои";
        public const string H_SpecifiedDowntimesComment = "Комментарий к простоям";
        public const string H_SetupRatioTitle = "Наладка";
        public const string H_MasterSetupComment = "Невыполнение норматива наладки";
        public const string H_ProductionRatioTitle = "Изготовление";
        public const string H_MasterMachiningComment = "Невыполнение норматива изготовления";
        public const string H_MasterComment = "Комментарий мастера";
        public const string H_FixedSetupTimePlan = "(И) Норматив наладки";
        public const string H_FixedProductionTimePlan = "(И) Норматив изготовления";
        public const string H_EngineerComment = "Комментарий техотдела";
        public const string H_ExcludedOperationsTime = "Исключённое время";
        public const string H_IncreaseReason = "Причина увеличения";
        public const string H_ExcludeFromReports = "Исключить";

        // ── Карта колонок ─────────────────────────────────────────────────
        public static readonly IReadOnlyDictionary<int, ColumnMeta> Map =
            new ReadOnlyDictionary<int, ColumnMeta>(
                new Dictionary<int, ColumnMeta>
                {
                    {  0, new() { SqlColumn="Machine",                  DisplayName=H_Machine,                   Kind=FilterKind.Text   } },
                    {  1, new() { SqlColumn="ShiftDate",                DisplayName=H_ShiftDate,                 Kind=FilterKind.None   } }, // основной фильтр по датам
                    {  2, new() { SqlColumn="Shift",                    DisplayName=H_Shift,                     Kind=FilterKind.Text   } },
                    {  3, new() { SqlColumn="Operator",                 DisplayName=H_Operator,                  Kind=FilterKind.Text   } },
                    {  4, new() { SqlColumn="PartName",                 DisplayName=H_PartName,                  Kind=FilterKind.Text   } },
                    {  5, new() { SqlColumn="[Order]",                  DisplayName=H_Order,                     Kind=FilterKind.Text   } },
                    {  6, new() { SqlColumn="TotalCount",               DisplayName=H_TotalCount,                Kind=FilterKind.Number } },
                    {  7, new() { SqlColumn="FinishedCount",            DisplayName=H_FinishedCount,             Kind=FilterKind.Number } },
                    {  8, new() { SqlColumn="DefectiveCount",           DisplayName=H_DefectiveCount,            Kind=FilterKind.Number } },
                    {  9, new() { SqlColumn="Setup",                    DisplayName=H_Setup,                     Kind=FilterKind.Number } },
                    { 10, new() { SqlColumn="",                         DisplayName=H_StartSetupTime,            Kind=FilterKind.None   } }, // HH:mm ≠ datetime в БД
                    { 11, new() { SqlColumn="",                         DisplayName=H_StartMachiningTime,        Kind=FilterKind.None   } },
                    { 12, new() { SqlColumn="",                         DisplayName=H_EndMachiningTime,          Kind=FilterKind.None   } },
                    { 13, new() { SqlColumn="SetupTimePlan",            DisplayName=H_SetupTimePlan,             Kind=FilterKind.Number } },
                    { 14, new() { SqlColumn="SetupTimeFact",            DisplayName=H_SetupTimeFact,             Kind=FilterKind.Number } },
                    { 15, new() { SqlColumn="SingleProductionTimePlan", DisplayName=H_SingleProductionTimePlan,  Kind=FilterKind.Number } },

                    { 16, new() {
                        SqlColumn   = "",
                        DisplayName = H_MachiningTime,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            p.MachiningTime.ToString(@"hh\:mm\:ss") == val.Trim(),
                    }},


                    { 17, new() { SqlColumn="",                         DisplayName=H_PartReplacementTime,       Kind=FilterKind.None   } }, // вычисляемое
                    { 18, new() { SqlColumn="",                         DisplayName=H_SingleProductionTime,      Kind=FilterKind.None   } }, // вычисляемое

                    { 19, new() { SqlColumn="ProductionTimeFact",       DisplayName=H_ProductionTimeFact,        Kind=FilterKind.Number } },
                    { 20, new() { SqlColumn="",                         DisplayName=H_PlanForBatch,              Kind=FilterKind.None   } }, // вычисляемое
                    { 21, new() { SqlColumn="OperatorComment",          DisplayName=H_OperatorComment,           Kind=FilterKind.Text   } },
                    { 22, new() { SqlColumn="",                         DisplayName=H_Problems,                  Kind=FilterKind.None   } }, // вычисляемое
                    { 23, new() { SqlColumn="SetupDowntimes",           DisplayName=H_SetupDowntimes,            Kind=FilterKind.Number } },
                    { 24, new() { SqlColumn="MachiningDowntimes",       DisplayName=H_MachiningDowntimes,        Kind=FilterKind.Number } },
                    { 25, new() { SqlColumn="PartialSetupTime",         DisplayName=H_PartialSetupTime,          Kind=FilterKind.Number } },
                    { 26, new() { SqlColumn="CreateNcProgramTime",      DisplayName=H_CreateNcProgramTime,       Kind=FilterKind.Number } },
                    { 27, new() { SqlColumn="MaintenanceTime",          DisplayName=H_MaintenanceTime,           Kind=FilterKind.Number } },
                    { 28, new() { SqlColumn="ToolSearchingTime",        DisplayName=H_ToolSearchingTime,         Kind=FilterKind.Number } },
                    { 29, new() { SqlColumn="ToolChangingTime",         DisplayName=H_ToolChangingTime,          Kind=FilterKind.Number } },
                    { 30, new() { SqlColumn="MentoringTime",            DisplayName=H_MentoringTime,             Kind=FilterKind.Number } },
                    { 31, new() { SqlColumn="ContactingDepartmentsTime",DisplayName=H_ContactingDepartmentsTime, Kind=FilterKind.Number } },
                    { 32, new() { SqlColumn="FixtureMakingTime",        DisplayName=H_FixtureMakingTime,         Kind=FilterKind.Number } },
                    { 33, new() { SqlColumn="HardwareFailureTime",      DisplayName=H_HardwareFailureTime,       Kind=FilterKind.Number } },
                    { 34, new() { SqlColumn="SpecialDowntimeTime",      DisplayName=H_SpecialDowntimeTime,       Kind=FilterKind.Number } },

                    // SpecifiedDowntimesRatio — вычисляется в модели из нескольких полей
                    { 35, new() {
                        SqlColumn   = "",
                        DisplayName = H_SpecifiedDowntimesRatio,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                        {
                            // Значение в ячейке: "8%" → сравниваем строку с тем что показывает StringFormat=0%
                            var normalized = val.TrimEnd('%', ' ');
                            return double.TryParse(normalized,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var pct)
                                && Math.Abs(p.SpecifiedDowntimesRatio * 100 - pct) < 0.5;
                        },
                    }},

                    { 36, new() { SqlColumn="SpecifiedDowntimesComment", DisplayName=H_SpecifiedDowntimesComment, Kind=FilterKind.Text } },

                    // SetupRatioTitle — строка, вычисляемая в модели
                    { 37, new() {
                        SqlColumn   = "",
                        DisplayName = H_SetupRatioTitle,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            string.Equals(p.SetupRatioTitle, val, StringComparison.OrdinalIgnoreCase),
                    }},

                    { 38, new() { SqlColumn="MasterSetupComment",       DisplayName=H_MasterSetupComment,        Kind=FilterKind.Text   } },

                    // ProductionRatioTitle — строка, вычисляемая в модели
                    { 39, new() {
                        SqlColumn   = "",
                        DisplayName = H_ProductionRatioTitle,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            string.Equals(p.ProductionRatioTitle, val, StringComparison.OrdinalIgnoreCase),
                    }},

                    { 40, new() { SqlColumn="MasterMachiningComment",   DisplayName=H_MasterMachiningComment,    Kind=FilterKind.Text   } },
                    { 41, new() { SqlColumn="MasterComment",            DisplayName=H_MasterComment,             Kind=FilterKind.Text   } },
                    { 42, new() { SqlColumn="FixedSetupTimePlan",       DisplayName=H_FixedSetupTimePlan,        Kind=FilterKind.Number } },
                    { 43, new() { SqlColumn="FixedProductionTimePlan",  DisplayName=H_FixedProductionTimePlan,   Kind=FilterKind.Number } },
                    { 44, new() { SqlColumn="EngineerComment",          DisplayName=H_EngineerComment,           Kind=FilterKind.Text   } },
                    { 45, new() { SqlColumn="ExcludedOperationsTime",   DisplayName=H_ExcludedOperationsTime,    Kind=FilterKind.Number } },
                    { 46, new() { SqlColumn="IncreaseReason",           DisplayName=H_IncreaseReason,            Kind=FilterKind.Text   } },
                    { 47, new() { SqlColumn="ExcludeFromReports",       DisplayName=H_ExcludeFromReports,        Kind=FilterKind.Bool   } },
                });
    }
}