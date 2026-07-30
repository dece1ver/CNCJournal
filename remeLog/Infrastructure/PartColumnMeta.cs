using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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

        /// <summary>
        /// Роли, которым колонка видна по умолчанию (используется для встроенных
        /// ролевых профилей — см. PartColumnMeta.GetColumnIdsForRole). По умолчанию
        /// видна всем ролям.
        /// </summary>
        public IReadOnlySet<User> Roles { get; init; } = PartColumnMeta.AllRoles;
    }

    public static class PartColumnMeta
    {
        // ── Ролевые группы для встроенных профилей ──────────────────────────
        // Соответствуют группам, ранее закодированным как ConverterParameter
        // UserRoleVisibilityConverter'а прямо в PartsInfoWindow.xaml.
        public static readonly IReadOnlySet<User> AllRoles =
            new HashSet<User> { User.Viewer, User.Master, User.Engineer, User.Developer };
        /// <summary>Скрыта от технолога (Engineer)</summary>
        private static readonly IReadOnlySet<User> HiddenFromEngineer =
            new HashSet<User> { User.Viewer, User.Master, User.Developer };
        /// <summary>Скрыта от мастера и технолога</summary>
        private static readonly IReadOnlySet<User> HiddenFromMasterAndEngineer =
            new HashSet<User> { User.Viewer, User.Developer };
        /// <summary>Скрыта от мастера (Master)</summary>
        private static readonly IReadOnlySet<User> HiddenFromMaster =
            new HashSet<User> { User.Viewer, User.Engineer, User.Developer };
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
        public const string H_MasterSetupComment = "Отклонения в наладке";
        public const string H_ProductionRatioTitle = "Изготовление";
        public const string H_MasterMachiningComment = "Отклонения в изготовлении";
        public const string H_MasterComment = "Комментарий мастера";
        public const string H_MasterSetupDetail = "Комментарий мастера (наладка)";
        public const string H_MasterMachiningDetail = "Комментарий мастера (изготовление)";
        public const string H_FixedSetupTimePlan = "(И) Норматив наладки";
        public const string H_FixedProductionTimePlan = "(И) Норматив изготовления";
        public const string H_EngineerConclusion = "Заключение техотдела";
        public const string H_EngineerComment = "Комментарий техотдела";
        public const string H_ExcludedOperationsTime = "Исключённое время";
        public const string H_IncreaseReason = "Причина увеличения";
        public const string H_ExcludeFromReports = "Исключить";

        // ── Карта колонок ─────────────────────────────────────────────────
        // Ключ — стабильный ID колонки, НЕ позиция. Тот же ID проставлен как
        // Tag у соответствующего DataGridColumn в PartsInfoWindow.xaml (см.
        // x:Key="<Id>Column" там же) и используется в code-behind/ViewModel
        // вместо DisplayIndex. Единственная точка синхронизации — совпадение
        // строк здесь и в XAML; добавление/удаление/перестановка колонок в
        // <DataGrid.Columns> больше не требует правок нигде, кроме как здесь
        // (при добавлении новой колонки) — порядок в словаре не имеет значения.
        public static readonly IReadOnlyDictionary<string, ColumnMeta> Map =
            new ReadOnlyDictionary<string, ColumnMeta>(
                new Dictionary<string, ColumnMeta>
                {
                    { "Machine",                  new() { SqlColumn="Machine",                  DisplayName=H_Machine,                   Kind=FilterKind.Text   } },
                    { "ShiftDate",                new() { SqlColumn="ShiftDate",                DisplayName=H_ShiftDate,                 Kind=FilterKind.None   } }, // основной фильтр по датам
                    { "Shift",                    new() { SqlColumn="Shift",                    DisplayName=H_Shift,                     Kind=FilterKind.Text   } },
                    { "Operator",                 new() { SqlColumn="Operator",                 DisplayName=H_Operator,                  Kind=FilterKind.Text   } },
                    { "PartName",                 new() { SqlColumn="PartName",                 DisplayName=H_PartName,                  Kind=FilterKind.Text   } },
                    { "Order",                    new() { SqlColumn="[Order]",                  DisplayName=H_Order,                     Kind=FilterKind.Text   } },
                    { "TotalCount",               new() { SqlColumn="TotalCount",               DisplayName=H_TotalCount,                Kind=FilterKind.Number } },
                    { "FinishedCount",            new() { SqlColumn="FinishedCount",            DisplayName=H_FinishedCount,             Kind=FilterKind.Number } },
                    { "DefectiveCount",           new() { SqlColumn="DefectiveCount",           DisplayName=H_DefectiveCount,            Kind=FilterKind.Number, Roles=HiddenFromEngineer } },
                    { "Setup",                    new() { SqlColumn="Setup",                    DisplayName=H_Setup,                     Kind=FilterKind.Number } },
                    { "StartSetupTime",           new() { SqlColumn="",                         DisplayName=H_StartSetupTime,            Kind=FilterKind.None   } }, // HH:mm ≠ datetime в БД
                    { "StartMachiningTime",       new() { SqlColumn="",                         DisplayName=H_StartMachiningTime,        Kind=FilterKind.None   } },
                    { "EndMachiningTime",         new() { SqlColumn="",                         DisplayName=H_EndMachiningTime,          Kind=FilterKind.None   } },
                    { "SetupTimePlan",            new() { SqlColumn="SetupTimePlan",            DisplayName=H_SetupTimePlan,             Kind=FilterKind.Number } },
                    { "SetupTimeFact",            new() { SqlColumn="SetupTimeFact",            DisplayName=H_SetupTimeFact,             Kind=FilterKind.Number } },
                    { "SingleProductionTimePlan", new() { SqlColumn="SingleProductionTimePlan", DisplayName=H_SingleProductionTimePlan,  Kind=FilterKind.Number } },

                    { "MachiningTime", new() {
                        SqlColumn   = "",
                        DisplayName = H_MachiningTime,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            p.MachiningTime.ToString(@"hh\:mm\:ss") == val.Trim(),
                    }},

                    { "PartReplacementTime",      new() { SqlColumn="",                         DisplayName=H_PartReplacementTime,       Kind=FilterKind.None   } }, // вычисляемое
                    { "SingleProductionTime",     new() { SqlColumn="",                         DisplayName=H_SingleProductionTime,      Kind=FilterKind.None   } }, // вычисляемое

                    { "ProductionTimeFact",       new() { SqlColumn="ProductionTimeFact",       DisplayName=H_ProductionTimeFact,        Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "PlanForBatch",             new() { SqlColumn="",                         DisplayName=H_PlanForBatch,              Kind=FilterKind.None,   Roles=HiddenFromMasterAndEngineer } }, // вычисляемое
                    { "OperatorComment",          new() { SqlColumn="OperatorComment",          DisplayName=H_OperatorComment,           Kind=FilterKind.Text   } },
                    { "Problems",                 new() { SqlColumn="",                         DisplayName=H_Problems,                  Kind=FilterKind.None   } }, // вычисляемое
                    { "SetupDowntimes",           new() { SqlColumn="SetupDowntimes",           DisplayName=H_SetupDowntimes,            Kind=FilterKind.Number, Roles=HiddenFromEngineer } },
                    { "MachiningDowntimes",       new() { SqlColumn="MachiningDowntimes",       DisplayName=H_MachiningDowntimes,        Kind=FilterKind.Number, Roles=HiddenFromEngineer } },
                    { "PartialSetupTime",         new() { SqlColumn="PartialSetupTime",         DisplayName=H_PartialSetupTime,          Kind=FilterKind.Number } },
                    { "CreateNcProgramTime",      new() { SqlColumn="CreateNcProgramTime",      DisplayName=H_CreateNcProgramTime,       Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "MaintenanceTime",          new() { SqlColumn="MaintenanceTime",          DisplayName=H_MaintenanceTime,           Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "ToolSearchingTime",        new() { SqlColumn="ToolSearchingTime",        DisplayName=H_ToolSearchingTime,         Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "ToolChangingTime",         new() { SqlColumn="ToolChangingTime",         DisplayName=H_ToolChangingTime,          Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "MentoringTime",            new() { SqlColumn="MentoringTime",            DisplayName=H_MentoringTime,             Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "ContactingDepartmentsTime",new() { SqlColumn="ContactingDepartmentsTime",DisplayName=H_ContactingDepartmentsTime, Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "FixtureMakingTime",        new() { SqlColumn="FixtureMakingTime",        DisplayName=H_FixtureMakingTime,         Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "HardwareFailureTime",      new() { SqlColumn="HardwareFailureTime",      DisplayName=H_HardwareFailureTime,       Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },
                    { "SpecialDowntimeTime",      new() { SqlColumn="SpecialDowntimeTime",      DisplayName=H_SpecialDowntimeTime,       Kind=FilterKind.Number, Roles=HiddenFromMasterAndEngineer } },

                    // SpecifiedDowntimesRatio — вычисляется в модели из нескольких полей
                    { "SpecifiedDowntimesRatio", new() {
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
                        Roles = HiddenFromEngineer,
                    }},

                    { "SpecifiedDowntimesComment", new() { SqlColumn="SpecifiedDowntimesComment", DisplayName=H_SpecifiedDowntimesComment, Kind=FilterKind.Text, Roles=HiddenFromEngineer } },

                    // SetupRatioTitle — строка, вычисляемая в модели
                    { "SetupRatioTitle", new() {
                        SqlColumn   = "",
                        DisplayName = H_SetupRatioTitle,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            string.Equals(p.SetupRatioTitle, val, StringComparison.OrdinalIgnoreCase),
                    }},

                    // Ячейка показывает эффективную причину (переопределение СГТ, иначе отметка
                    // мастера), поэтому фильтр обязан искать по ней же — SQL-фильтр по
                    // MasterSetupComment находил бы не то, что видно в колонке.
                    { "MasterSetupComment", new() {
                        SqlColumn   = "",
                        DisplayName = H_MasterSetupComment,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            string.Equals(p.EffectiveSetupReason, val, StringComparison.OrdinalIgnoreCase),
                    }},

                    // ProductionRatioTitle — строка, вычисляемая в модели
                    { "ProductionRatioTitle", new() {
                        SqlColumn   = "",
                        DisplayName = H_ProductionRatioTitle,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            string.Equals(p.ProductionRatioTitle, val, StringComparison.OrdinalIgnoreCase),
                    }},

                    // См. комментарий у MasterSetupComment.
                    { "MasterMachiningComment", new() {
                        SqlColumn   = "",
                        DisplayName = H_MasterMachiningComment,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            string.Equals(p.EffectiveMachiningReason, val, StringComparison.OrdinalIgnoreCase),
                    }},
                    { "MasterComment",            new() { SqlColumn="MasterComment",            DisplayName=H_MasterComment,             Kind=FilterKind.Text   } },
                    // См. комментарий у MasterSetupComment: показывается и фильтруется
                    // эффективная детализация (обоснование СГТ при переопределении).
                    { "MasterSetupDetail", new() {
                        SqlColumn   = "",
                        DisplayName = H_MasterSetupDetail,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            p.EffectiveSetupDetail?.Contains(val, StringComparison.OrdinalIgnoreCase) == true,
                    }},
                    { "MasterMachiningDetail", new() {
                        SqlColumn   = "",
                        DisplayName = H_MasterMachiningDetail,
                        Kind        = FilterKind.InMemory,
                        Predicate   = (p, val) =>
                            p.EffectiveMachiningDetail?.Contains(val, StringComparison.OrdinalIgnoreCase) == true,
                    }},
                    { "AiCheck",                  new() { SqlColumn="",                         DisplayName="ИИ-проверка",               Kind=FilterKind.None   } }, // вычисляемое, не фильтруется
                    { "FixedSetupTimePlan",       new() { SqlColumn="FixedSetupTimePlan",       DisplayName=H_FixedSetupTimePlan,        Kind=FilterKind.Number, Roles=HiddenFromMaster } },
                    { "FixedProductionTimePlan",  new() { SqlColumn="FixedProductionTimePlan",  DisplayName=H_FixedProductionTimePlan,   Kind=FilterKind.Number, Roles=HiddenFromMaster } },
                    { "EngineerConclusion",       new() { SqlColumn="EngineerConclusion",       DisplayName=H_EngineerConclusion,        Kind=FilterKind.Text,   Roles=HiddenFromMaster } },
                    { "EngineerComment",          new() { SqlColumn="EngineerComment",          DisplayName=H_EngineerComment,           Kind=FilterKind.Text,   Roles=HiddenFromMaster } },
                    { "ExcludedOperationsTime",   new() { SqlColumn="ExcludedOperationsTime",   DisplayName=H_ExcludedOperationsTime,    Kind=FilterKind.Number, Roles=HiddenFromMaster } },
                    { "IncreaseReason",           new() { SqlColumn="IncreaseReason",           DisplayName=H_IncreaseReason,            Kind=FilterKind.Text,   Roles=HiddenFromMaster } },
                    { "ExcludeFromReports",       new() { SqlColumn="ExcludeFromReports",       DisplayName=H_ExcludeFromReports,        Kind=FilterKind.Bool,   Roles=HiddenFromMaster } },
                });

        /// <summary>
        /// Идентификаторы колонок, видимых встроенному ролевому профилю (для обратной
        /// совместимости с прежней ролевой моделью — используется как содержимое
        /// профиля "по умолчанию" при выборе роли Мастер/Технолог/Показать всё).
        /// </summary>
        public static HashSet<string> GetColumnIdsForRole(User role) =>
            Map.Where(kv => kv.Value.Roles.Contains(role)).Select(kv => kv.Key).ToHashSet();

        /// <summary>
        /// Порядок колонок PartsInfoWindow — единственный источник истины: сам грид
        /// строит DataGrid.Columns по этому списку (см. конструктор PartsInfoWindow),
        /// им же упорядочен чеклист в редакторе профилей столбцов. Не влияет на
        /// ColumnId/Map и контекстные меню — те завязаны на строковый ID, не на позицию.
        /// </summary>
        public static readonly IReadOnlyList<string> ColumnOrder = new[]
        {
            "Machine", "ShiftDate", "Shift", "Operator", "PartName", "Order", "TotalCount",
            "FinishedCount", "DefectiveCount", "Setup", "StartSetupTime", "StartMachiningTime",
            "EndMachiningTime", "SetupTimePlan", "SetupTimeFact", "SingleProductionTimePlan",
            "MachiningTime", "PartReplacementTime", "SingleProductionTime", "ProductionTimeFact",
            "PlanForBatch", "OperatorComment", "Problems", "SetupDowntimes", "MachiningDowntimes",
            "PartialSetupTime", "CreateNcProgramTime", "MaintenanceTime", "ToolSearchingTime",
            "ToolChangingTime", "MentoringTime", "ContactingDepartmentsTime", "FixtureMakingTime",
            "HardwareFailureTime", "SpecialDowntimeTime", "SpecifiedDowntimesRatio",
            "SpecifiedDowntimesComment", "SetupRatioTitle", "MasterSetupComment", "MasterSetupDetail",
            "ProductionRatioTitle", "MasterMachiningComment", "MasterMachiningDetail", "MasterComment",
            "AiCheck", "FixedSetupTimePlan", "FixedProductionTimePlan", "EngineerComment",
            "EngineerConclusion", "ExcludedOperationsTime", "IncreaseReason", "ExcludeFromReports",
        };
    }
}