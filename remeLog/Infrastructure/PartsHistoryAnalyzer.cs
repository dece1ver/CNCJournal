using remeLog.Models;
using System;
using System.Collections.Generic;

namespace remeLog.Infrastructure
{
        /// <summary>
        /// Одна прошлая смена той же детали (PartName+Order+Machine)
        /// с данными производства и решением аналитика.
        /// </summary>
        public class PartsHistoryEntry
        {
            public Part Part { get; init; } = null!;
            /// <summary> "escalated" / "ok" / null — если день не проверен аналитиком </summary>
            public string? AnalystDecision { get; init; }
            public string? AnalystComment { get; init; }
            public string? AiExplanation { get; init; }
            public string? AiFeedback { get; init; }
        }

        /// <summary>
        /// Одна строка в построчном списке истории для промпта.
        /// Агрегат по смене: за сутки может быть несколько Part-записей одной детали
        /// (разные установки) — объединяем в одну строку.
        /// </summary>
        public class PartsHistoryLine
        {
            public DateTime ShiftDate { get; init; }
            public double? ProductionRatio { get; init; }
            public double? SetupRatio { get; init; }
            public double FinishedCount { get; init; }
            public string? AnalystDecision { get; init; }
            public string? AnalystComment { get; init; }
            public string? AiExplanation { get; init; }
            public string? AiFeedback { get; init; }
            /// <summary>
            /// Хотя бы одна запись смены имела КПД изготовления &lt; 70%
            /// без комментария мастера — повторение этого флага сигнализирует
            /// о системной проблеме, а не разовой накладке.
            /// </summary>
            public bool HasUnexplainedLowEfficiency { get; init; }
        }

        public class PartsHistorySummary
        {
            public int RecordsFound { get; set; }
            public bool HasHistory => RecordsFound > 0;
            public List<PartsHistoryLine> Lines { get; set; } = new List<PartsHistoryLine>();
        }
}
