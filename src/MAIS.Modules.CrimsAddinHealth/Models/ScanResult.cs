namespace MAIS.Modules.CrimsAddinHealth.Models;

public sealed class ScanResult
{
    public string         ClientId    { get; init; } = "";
    public string         MachineName { get; init; } = "";
    public string         AssetTag    { get; init; } = "";
    public string         CrimsUserId { get; init; } = "";
    public string         MachineRole { get; init; } = "";
    public DateTimeOffset ScannedAt   { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<DllMismatch> Mismatches { get; init; } = [];
    public bool HasMismatches => Mismatches.Count > 0;
}

public sealed class DllMismatch
{
    public string FileName         { get; init; } = "";
    public string InstalledVersion { get; init; } = "";
    public string ExpectedVersion  { get; init; } = "";
    public bool   IsMissing        { get; init; }
}