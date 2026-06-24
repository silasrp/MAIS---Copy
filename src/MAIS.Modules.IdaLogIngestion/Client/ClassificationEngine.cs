using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// Classifies a parsed log entry using the server's canonical registry. Falls back to a
/// level-based default for templates that are pending review (not yet in the registry).
/// The fallback is conservative: ERROR/FATAL/WARN are ingested so nothing critical is lost
/// while the review queue catches up; INFO becomes stats-only; DEBUG/TRACE are discarded.
/// </summary>
public sealed class ClassificationEngine
{
    private readonly TemplateRegistryCache _registryCache;

    public ClassificationEngine(TemplateRegistryCache registryCache)
    {
        _registryCache = registryCache;
    }

    public ClassificationDecision Classify(ParsedLogEntry entry, TemplateMatch match)
    {
        var registryEntry = _registryCache.Lookup(match.TemplateId);

        if (registryEntry is not null)
        {
            return new ClassificationDecision
            {
                Action     = registryEntry.Classification,
                TemplateId = match.TemplateId,
                Reason     = "registry",
                IsNovel    = false
            };
        }

        return new ClassificationDecision
        {
            Action     = LevelBasedDefault(entry.Level),
            TemplateId = match.TemplateId,
            Reason     = "level-fallback",
            IsNovel    = match.IsNew
        };
    }

    private static ClassificationAction LevelBasedDefault(string level) =>
        level.ToUpperInvariant() switch
        {
            "FATAL" or "ERROR" => ClassificationAction.Ingest,
            "WARN"  or "WARNING" => ClassificationAction.Ingest,
            "INFO"               => ClassificationAction.StatsOnly,
            _                    => ClassificationAction.Discard
        };
}
