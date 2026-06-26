using System.Text.Json.Serialization;

namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>Final ingest-tier record: a parsed, classified log entry ready for Elasticsearch.</summary>
public sealed class LogRecord
{
    public required string        IdempotencyKey      { get; init; }
    public required string        AppId               { get; init; }
    public required string        SourceApplication   { get; init; }
    [JsonPropertyName("log_type")]
    public required string        CompatibilityAppName { get; init; }
    public required string        MachineName         { get; init; }
    [JsonPropertyName("workstation")]
    public required string        AssetTag            { get; init; }
    [JsonPropertyName("@timestamp")]
    public required DateTime      Timestamp           { get; init; }
    public required string        Level               { get; init; }
    public required string        Message             { get; init; }
    public required string        TemplateId          { get; init; }
    public required string        Classification      { get; init; }
}
