using remeLog.Infrastructure.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace remeLog.Core
{
    /// <summary>
    /// Общее рантайм-состояние бизнес-настроек (нормативы, праздники, серийные детали),
    /// нужное моделям remeLog.Core. Физически перенесено сюда из remeLog.Infrastructure.AppSettings
    /// (которая теперь делегирует сюда же), т.к. Part/CombinedParts читают эти поля напрямую,
    /// а AppSettings в целом (UI-настройки, ColumnProfiles) остаётся WPF-специфичной.
    /// Заполняется как раньше — из БД при старте remeLog; в remeLog.Core только хранилище.
    /// </summary>
    public static class DomainSettings
    {
        /// <summary>Нормализованные имена серийных деталей.</summary>
        public static HashSet<string> SerialParts { get; set; } = new();
        /// <summary>Причины отклонения наладки и требуют ли комментария.</summary>
        public static List<(string Reason, bool RequireComment)> SetupReasons { get; set; } = new();
        /// <summary>Причины отклонения изготовления и требуют ли комментария.</summary>
        public static List<(string Reason, bool RequireComment)> MachiningReasons { get; set; } = new();
        /// <summary>Общий лимит КПД наладки, если для станка нет своего значения в <see cref="MaxSetupLimits"/>.</summary>
        public static double MaxSetupLimit { get; set; } = 2;
        /// <summary>Лимит КПД наладки по станкам.</summary>
        public static Dictionary<string, double> MaxSetupLimits { get; set; } = new();
        /// <summary>Запасной лимит КПД наладки, если для станка нет значения ни в конфиге, ни в <see cref="MaxSetupLimits"/>.</summary>
        public static double FallbackMaxSetupLimitValue { get; set; } = 1.5;
        /// <summary>Праздничные дни — не считаются рабочими сменами.</summary>
        public static DateTime[] Holidays { get; set; } = Array.Empty<DateTime>();
        /// <summary>Строка подключения к SQL Server.</summary>
        public static string? ConnectionString { get; set; }
        /// <summary>Включает подробное логирование операций с БД.</summary>
        public static bool DebugMode { get; set; }
        /// <summary>Порог времени наладки (мин), после которого наладка считается длинной.</summary>
        public static double LongSetupLimit { get; set; } = 240;
        public static string NcArchivePath { get; set; } = "";
        public static string NcIntermediatePath { get; set; } = "";
        public static string[] Administrators { get; set; } = Array.Empty<string>();
        public static string[] CncOperations { get; set; } = Array.Empty<string>();
        public static string[] EngineerComments { get; set; } = Array.Empty<string>();
        public static string? PcaReportPath { get; set; }
        /// <summary>Адрес AiService.</summary>
        public static string AiIp { get; set; } = string.Empty;
        /// <summary>Имя модели, используемой AiService.</summary>
        public static string AiModel { get; set; } = "qwen3:14b";
        /// <summary>Версия схемы БД, зафиксированная в cnc_remelog_config — см. AppSettings.RequiredSchemaVersion.</summary>
        public static int SchemaVersion { get; set; }
        /// <summary>Функции remeLog, включённые для текущего пользователя.</summary>
        public static RemeLogFeature EnabledFeatures { get; set; } = RemeLogFeature.None;
        /// <summary>Заданы ли <see cref="EnabledFeatures"/> явно (флагом запуска), а не из БД.</summary>
        public static bool FeaturesExplicitlySet { get; set; }

        /// <summary>Число рабочих дней в интервале (включительно), за вычетом <see cref="Holidays"/>.</summary>
        public static int GetWorkDaysBetween(DateTime start, DateTime end) =>
            (int)(end - start).TotalDays + 1 - Holidays.Count(d => d >= start && d <= end);
    }
}
