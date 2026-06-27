using System.Text.Json.Serialization;

namespace AiService.Models;

public class AnalyzeRequest
{
    public string Machine { get; set; } = "";
    public string ShiftDate { get; set; } = "";
    public List<string> Signals { get; set; } = [];
    public List<PartContext> Parts { get; set; } = [];
    public bool EnableThinking { get; set; } = false;
    public string? Model { get; set; }
}

public class PartContext
{
    public string PartName { get; set; } = "";
    public string Order { get; set; } = "";
    public int Setup { get; set; }

    public double? SetupRatio { get; set; }
    public double? ProductionRatio { get; set; }
    public double FinishedCount { get; set; }
    public double SetupTimePlan { get; set; }
    public double SetupTimeFact { get; set; }
    public double SingleProductionTimePlan { get; set; }
    public double ProductionTimeFact { get; set; } 
    public double PartialSetup { get; set; } 
    public double MachiningTime { get; set; }
    public double? DowntimeRatio { get; set; }
    public string OperatorComment { get; set; } = "";
    public string MasterSetupComment { get; set; } = "";
    public string MasterMachiningComment { get; set; } = "";
    public string MasterComment { get; set; } = "";
    public string SpecifiedDowntimesList { get; set; } = "";
    public string SpecifiedDowntimesComment { get; set; } = "";

    public bool NoSetupHappened { get; set; }
    public bool NoProductionHappened { get; set; }
    public bool NoManualOperatorComment { get; set; }

    // Детерминированные сигналы от remeLog
    public List<string> Signals { get; set; } = [];
    public PartsHistoryDto? PartsHistory { get; set; }
}

public class PartsHistoryDto
{
    public int RecordsFound { get; set; }
    public List<PartsHistoryLineDto> Lines { get; set; } = new();
}

public class PartsHistoryLineDto
{
    public string ShiftDate { get; set; } = "";
    public string ProductionRatio { get; set; } = "";
    public string SetupRatio { get; set; } = "";
    public int FinishedCount { get; set; }
    public string AnalystDecision { get; set; } = "";
    public string? AnalystComment { get; set; }
    public string? AiExplanation { get; set; }
    public bool HasUnexplainedLowEfficiency { get; set; }
}

public class AnalyzeResponse
{
    public bool RequiresReview { get; set; }
    public double Confidence { get; set; }
    public List<string> Signals { get; set; } = [];
    public List<string> DowngradedSignals { get; set; } = [];
    public List<string> SuggestExcludeFromReports { get; set; } = [];

    public string Explanation { get; set; } = "";
    public string ThinkingProcess { get; set; } = "";
    public string SuggestedReason { get; set; } = "";
    public string? Error { get; set; }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string? PromptVersion { get; set; }

}