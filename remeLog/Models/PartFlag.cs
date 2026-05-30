using System;

namespace remeLog.Models
{
    /// <summary>
    /// Флаг конкретной записи журнала внутри проверяемых суток.
    /// Если день полностью однозначен, флаги на отдельные записи необязательны.
    /// </summary>
    public class PartFlag
    {
        public PartFlag() { }

        public PartFlag(int dayReviewId, Guid partGuid, bool isCleared, string? comment = null)
        {
            DayReviewId = dayReviewId;
            PartGuid = partGuid;
            IsCleared = isCleared;
            Comment = comment ?? string.Empty;
        }

        public int Id { get; set; }
        public int DayReviewId { get; set; }
        public Guid PartGuid { get; set; }

        /// <summary>
        /// true  — запись однозначна, аналитик её "закрыл" внутри эскалированного дня.
        /// false — запись проблемна, требует внимания экспертов.
        /// </summary>
        public bool IsCleared { get; set; }
        public string Comment { get; set; } = string.Empty;

        // AI-поля на уровне строки (заполняются на этапе 3)
        public bool? AiRequiresReview { get; set; }
        public double? AiConfidence { get; set; }
        public string? AiSuggestedReason { get; set; }
        public string? AiSignals { get; set; }  // JSON
        public string? AiExplanation { get; set; }
    }

}