namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>A novel template shape pending human review, enriched with AI suggestion.</summary>
public sealed class NovelTemplateReviewItem
{
    public required string   TemplateId                  { get; init; }
    public required string   AppId                       { get; init; }
    public required string[] TokenPattern                { get; init; }
    public required string[] SampleMessages              { get; init; }
    public required int      SeenOnMachineCount          { get; set; }
    public required DateTimeOffset FirstSeenAt           { get; init; }

    public ClassificationAction? AiSuggestedClassification { get; init; }
    public string                AiRationale               { get; init; } = "";
    public string                AiHumanReadableDescription { get; init; } = "";
    public string[]              AiSuggestedExtractionFields { get; init; } = [];
}
