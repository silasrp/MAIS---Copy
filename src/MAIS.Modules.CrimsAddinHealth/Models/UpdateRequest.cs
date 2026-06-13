namespace MAIS.Modules.CrimsAddinHealth.Models;

public sealed class UpdateRequest
{
    public string          RequestId     { get; init; } = Guid.NewGuid().ToString();
    public ScanResult      ScanResult    { get; init; } = null!;
    public AgentAnalysis   AgentAnalysis { get; init; } = null!;
    public DateTimeOffset  CreatedAt     { get; init; } = DateTimeOffset.UtcNow;
    public UpdateRequestStatus Status    { get; set; }  = UpdateRequestStatus.PendingApproval;
}

public enum UpdateRequestStatus
{
    PendingApproval,
    Approved,
    Deferred,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

public sealed class UpdateApproval
{
    public string         RequestId              { get; init; } = "";
    public bool           IsApproved             { get; init; }
    public DateTimeOffset? DeferUntil            { get; init; }
    public string         ApprovedByClientId     { get; init; } = "";
    public string         ApprovedByMachineName  { get; init; } = "";
    public string         ApprovedByMachineIp    { get; init; } = "";
    public string         ApprovedByUserId       { get; init; } = "";
    public DateTimeOffset DecisionAt             { get; init; } = DateTimeOffset.UtcNow;
}