namespace MAIS.Modules.IdaLogIngestion.Models;

public enum SpoolBatchKind { Ingest, Stats }

/// <summary>Metadata for one on-disk spool batch file. The payload itself is NDJSON on disk.</summary>
public sealed class SpoolBatch
{
    public required string        BatchId        { get; init; }
    public required SpoolBatchKind Kind          { get; init; }
    public required string        AppId          { get; init; }
    public required string        FilePath       { get; init; }
    public required int           RecordCount    { get; init; }
    public required DateTimeOffset CreatedAt     { get; init; }
    public required long          FileSizeBytes  { get; init; }
}
