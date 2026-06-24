namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>Result of ClassificationEngine: what to do with a log entry and why.</summary>
public sealed class ClassificationDecision
{
    public required ClassificationAction Action     { get; init; }
    public required string               TemplateId { get; init; }
    public required string               Reason     { get; init; }
    public required bool                 IsNovel    { get; init; }
}
