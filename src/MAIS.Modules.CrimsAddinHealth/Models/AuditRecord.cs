namespace MAIS.Modules.CrimsAddinHealth.Models;

public sealed class AuditRecord
{
    public string         AuditId              { get; init; } = Guid.NewGuid().ToString();
    public string         ClientId             { get; init; } = "";
    public string         MachineName          { get; init; } = "";
    public string         AssetTag             { get; init; } = "";
    public string         CrimsUserId          { get; init; } = "";
    public IReadOnlyList<string> UpdatedDlls       { get; init; } = [];
    public IReadOnlyList<string> DllVersionsBefore { get; init; } = [];
    public IReadOnlyList<string> DllVersionsAfter  { get; init; } = [];
    public string         ApprovedByMachineName { get; init; } = "";
    public string         ApprovedByMachineIp  { get; init; } = "";
    public string         ApprovedByUserId     { get; init; } = "";
    public DateTimeOffset ApprovedAt           { get; init; }
    public DateTimeOffset UpdateStartedAt      { get; init; }
    public DateTimeOffset UpdateCompletedAt    { get; init; }
    public bool           Success              { get; init; }
    public string?        FailureReason        { get; init; }
}