namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>A rolled-up stats-tier record: count of a single template over one time bucket.</summary>
public sealed class StatsAggregate
{
    public required string        IdempotencyKey { get; init; }
    public required string        AppId          { get; init; }
    public required string        TemplateId     { get; init; }
    public required DateTimeOffset BucketStart   { get; init; }
    public required int           Count          { get; init; }
    public required string        AssetTag       { get; init; }
}
