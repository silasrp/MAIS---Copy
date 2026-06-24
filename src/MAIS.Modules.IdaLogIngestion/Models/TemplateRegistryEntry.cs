namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>One entry in the canonical template registry: a known log shape and its approved classification.</summary>
public sealed class TemplateRegistryEntry
{
    public required string              TemplateId              { get; init; }
    public required string              AppId                   { get; init; }
    public required string[]            TokenPattern            { get; init; }
    public required ClassificationAction Classification         { get; init; }
    public string                       HumanReadableDescription { get; init; } = "";
    public string[]                     ExtractionFields        { get; init; } = [];
    public string                       ApprovedBy              { get; init; } = "";
    public DateTimeOffset               ApprovedAt              { get; init; }
    public long                         TotalMatchCount         { get; init; }
}

public enum ClassificationAction
{
    Ingest,
    StatsOnly,
    Discard
}
