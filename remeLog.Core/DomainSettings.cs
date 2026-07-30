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
        public static HashSet<string> SerialParts { get; set; } = new();
        public static List<(string Reason, bool RequireComment)> SetupReasons { get; set; } = new();
        public static List<(string Reason, bool RequireComment)> MachiningReasons { get; set; } = new();
        public static double MaxSetupLimit { get; set; } = 2;
        public static Dictionary<string, double> MaxSetupLimits { get; set; } = new();
        public static double FallbackMaxSetupLimitValue { get; set; } = 1.5;
        public static DateTime[] Holidays { get; set; } = Array.Empty<DateTime>();
        public static string? ConnectionString { get; set; }
        public static bool DebugMode { get; set; }
        public static double LongSetupLimit { get; set; } = 240;
        public static string NcArchivePath { get; set; } = "";
        public static string NcIntermediatePath { get; set; } = "";
        public static string[] Administrators { get; set; } = Array.Empty<string>();
        public static string[] CncOperations { get; set; } = Array.Empty<string>();
        public static string[] EngineerComments { get; set; } = Array.Empty<string>();
        public static string? PcaReportPath { get; set; }
        public static string AiIp { get; set; } = string.Empty;
        public static string AiModel { get; set; } = "qwen3:14b";
        public static int SchemaVersion { get; set; }
        public static RemeLogFeature EnabledFeatures { get; set; } = RemeLogFeature.None;
        public static bool FeaturesExplicitlySet { get; set; }

        public static int GetWorkDaysBetween(DateTime start, DateTime end) =>
            (int)(end - start).TotalDays + 1 - Holidays.Count(d => d >= start && d <= end);
    }
}
