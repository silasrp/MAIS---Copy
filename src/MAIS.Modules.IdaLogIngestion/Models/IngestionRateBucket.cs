namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>One minute-granularity bucket of confirmed Elasticsearch writes.</summary>
public sealed class IngestionRateBucket
{
    public required DateTimeOffset BucketStart { get; init; }
    public required int            Count       { get; init; }
}
