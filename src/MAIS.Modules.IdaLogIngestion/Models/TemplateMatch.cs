namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>Output of LogTemplateMiner.Process — the template this message matched (or was assigned to).</summary>
public sealed class TemplateMatch
{
    public required string   TemplateId          { get; init; }
    public required bool     IsNew               { get; init; }
    public required string[] ExtractedVariables  { get; init; }

    public TemplateMatch(string templateId, bool isNew, string[] extractedVariables)
    {
        TemplateId         = templateId;
        IsNew              = isNew;
        ExtractedVariables = extractedVariables;
    }
}
