namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>One assembled multiline entry from the log file, before parsing.</summary>
public sealed class RawLogEntry
{
    public required string AppId           { get; init; }
    public required string MachineName     { get; init; }
    public required string AssetTag        { get; init; }
    public required string FirstLine       { get; init; }
    public IReadOnlyList<string> AdditionalLines { get; init; } = [];
    public DateTimeOffset ReceivedAt       { get; init; } = DateTimeOffset.UtcNow;
}
