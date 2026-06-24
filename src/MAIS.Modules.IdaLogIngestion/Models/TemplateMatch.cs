using System.Diagnostics.CodeAnalysis;

namespace MAIS.Modules.IdaLogIngestion.Models;

public sealed class TemplateMatch
{
    public required string   TemplateId         { get; init; }
    public required bool     IsNew              { get; init; }
    public required string[] ExtractedVariables { get; init; }

    [SetsRequiredMembers]
    public TemplateMatch(string templateId, bool isNew, string[] extractedVariables)
    {
        TemplateId         = templateId;
        IsNew              = isNew;
        ExtractedVariables = extractedVariables;
    }
}