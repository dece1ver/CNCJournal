using System;
using System.Text.Json.Serialization;

namespace remeLog.Infrastructure
{
    public class AiAnalysisResult
    {
        [JsonPropertyName("requiresReview")] public bool RequiresReview { get; set; }
        [JsonPropertyName("confidence")] public double Confidence { get; set; }
        [JsonPropertyName("signals")] public string[] Signals { get; set; } = Array.Empty<string>();
        [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
        [JsonPropertyName("suggestedReason")] public string SuggestedReason { get; set; } = "";
        [JsonPropertyName("suggestExcludeFromReports")]
        public string[] SuggestExcludeFromReports { get; set; } = Array.Empty<string>();
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("promptVersion")] public string? PromptVersion { get; set; }
        public bool HasError => !string.IsNullOrEmpty(Error);
    }
}
