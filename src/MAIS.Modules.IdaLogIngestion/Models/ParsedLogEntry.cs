namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>A RawLogEntry after structured-prefix extraction and message cleaning.</summary>
public sealed class ParsedLogEntry
{
    public required string AppId       { get; init; }
    public required string MachineName { get; init; }
    public required string AssetTag    { get; init; }

    public DateTime Timestamp  { get; init; }
    public string   Thread     { get; init; } = "";
    public string   Level      { get; init; } = "";
    public string   Message    { get; init; } = "";
    public bool     IsParsed   { get; init; } = true;

    public static ParsedLogEntry Unparsed(RawLogEntry entry) => new()
    {
        AppId       = entry.AppId,
        MachineName = entry.MachineName,
        AssetTag    = entry.AssetTag,
        Timestamp   = entry.ReceivedAt.UtcDateTime,
        Message     = entry.FirstLine,
        IsParsed    = false
    };
}
