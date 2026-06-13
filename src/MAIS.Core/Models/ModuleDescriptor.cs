namespace MAIS.Core.Models;

/// <summary>
/// An immutable, serialisable snapshot of a module's current state.
/// Used by the REST API, SignalR hub, and sidebar — never holds live object references.
/// </summary>
public sealed record ModuleDescriptor
{
    /// <summary>Stable unique identifier. E.g. "mais.sla-monitor".</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Brief description shown in the sidebar card.</summary>
    public required string Description { get; init; }

    /// <summary>Semantic version string.</summary>
    public required string Version { get; init; }

    /// <summary>Deployment topology of this module.</summary>
    public required ModuleType Type { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public ModuleStatus Status { get; init; } = ModuleStatus.Unknown;

    /// <summary>Optional message accompanying the current status (e.g. an error message).</summary>
    public string? StatusMessage { get; init; }

    /// <summary>URI the sidebar should open when the user clicks "Open".</summary>
    public string? LaunchUri { get; init; }

    /// <summary>When this module was first registered with the framework.</summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp of the most recent health check.</summary>
    public DateTimeOffset? LastHealthCheck { get; init; }

    /// <summary>
    /// Returns a new descriptor with the provided status applied.
    /// Records are immutable — callers must use this to create updated snapshots.
    /// </summary>
    public ModuleDescriptor WithStatus(ModuleStatus status, string? message = null) =>
        this with { Status = status, StatusMessage = message, LastHealthCheck = DateTimeOffset.UtcNow };
}
