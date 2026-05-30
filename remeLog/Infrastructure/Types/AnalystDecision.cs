namespace remeLog.Infrastructure.Types
{
    /// <summary>
    /// Итоговое решение аналитика по суткам (станок + дата).
    /// </summary>
    public enum AnalystDecision
    {
        /// <summary>
        /// Сутки проверены, все записи однозначны. Аналитик закрыл день.
        /// </summary>
        Ok,

        /// <summary>
        /// Есть неоднозначные записи — сутки переданы экспертам на углублённую проверку.
        /// </summary>
        Escalated,
    }

    public static class AnalystDecisionExtensions
    {
        public static string ToDbString(this AnalystDecision d) => d switch
        {
            AnalystDecision.Ok => "ok",
            AnalystDecision.Escalated => "escalated",
            _ => "ok",
        };

        public static AnalystDecision FromDbString(string? s) => s?.ToLowerInvariant() switch
        {
            "escalated" => AnalystDecision.Escalated,
            _ => AnalystDecision.Ok,
        };

        public static string ToDisplayString(this AnalystDecision d) => d switch
        {
            AnalystDecision.Ok => "Проверено",
            AnalystDecision.Escalated => "На эскалацию",
            _ => "Проверено",
        };
    }
}