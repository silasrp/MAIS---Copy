using System.Collections.Generic;

namespace MAIS.Modules.IdaLogIngestion.Models;

/// <summary>Versioned snapshot of the canonical registry for one application, served to clients.</summary>
public sealed class TemplateRegistrySnapshot
{
    public required string AppId                              { get; init; }
    public required int    Version                            { get; init; }
    public required IReadOnlyList<TemplateRegistryEntry> Entries { get; init; }
}
