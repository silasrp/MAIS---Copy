public sealed class TemplateMatch
{
    public required string   TemplateId         { get; init; }
    public required bool     IsNew              { get; init; }
    public required string[] ExtractedVariables { get; init; }
    public required string[] TokenPattern       { get; init; }

    [SetsRequiredMembers]
    public TemplateMatch(string templateId, bool isNew, string[] extractedVariables, string[] tokenPattern)
    {
        TemplateId         = templateId;
        IsNew              = isNew;
        ExtractedVariables = extractedVariables;
        TokenPattern       = tokenPattern;
    }
}
