using System;

namespace remeLog.Models
{
    /// <summary>
    /// Текущее состояние станка (наладка/изготовление/простой) — то, что eLog
    /// молча пишет по мере работы, ещё до закрытия строки в parts.
    /// </summary>
    public sealed class MachineActivity
    {
        public const string SetupStatus = "Наладка";
        public const string MachiningStatus = "Изготовление";
        public const string IdleStatus = "Простой";

        public string Machine { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string PartName { get; init; } = string.Empty;
        public string Order { get; init; } = string.Empty;
        public string Operator { get; init; } = string.Empty;
        public byte Setup { get; init; }
        public string Shift { get; init; } = string.Empty;
        public DateTime? PhaseStartLocal { get; init; }
        public DateTime UpdatedLocal { get; init; }

        /// <summary>
        /// Нет свежего heartbeat от eLog (дольше, чем в 2-3 раза интервал записи) —
        /// значит "нет данных", а не "простой": eLog мог не запуститься вовсе.
        /// </summary>
        public bool IsStale => (DateTime.Now - UpdatedLocal).TotalSeconds > 40;
    }
}
