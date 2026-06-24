namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>Payload a client POSTs when it encounters a template shape not present in its cached registry.</summary>
public sealed class NovelTemplateCandidate
{
    public required string   AppId          { get; init; }
    public required string   TemplateId     { get; init; }
    public required string[] TokenPattern   { get; init; }
    public required string[] SampleMessages { get; init; }
    public required string   MachineName    { get; init; }
}
