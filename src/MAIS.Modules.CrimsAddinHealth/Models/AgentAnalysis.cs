namespace MAIS.Modules.CrimsAddinHealth.Models;

public sealed class AgentAnalysis
{
    public string ApprovalMessage        { get; init; } = "";
    public string RiskLevel              { get; init; } = "Normal";
    public string RiskRationale          { get; init; } = "";
    public string RecommendedTiming      { get; init; } = "EndOfDay";
    public string TimingRationale        { get; init; } = "";
    public int    EstimatedDurationSeconds { get; init; }
}