using MAIS.Core.Models;

namespace MAIS.Core.Events;

/// <summary>Base record for all MAIS domain events.</summary>
public abstract record MaisEvent
{
    /// <summary>Unique identifier for this event instance.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>UTC timestamp when this event was raised.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

// ---------------------------------------------------------------------------
// Module lifecycle events
// ---------------------------------------------------------------------------

/// <summary>Published when a new module is registered with the framework.</summary>
public sealed record ModuleRegisteredEvent(
    string ModuleId,
    string DisplayName,
    ModuleType ModuleType) : MaisEvent;

/// <summary>Published whenever a module's status changes.</summary>
public sealed record ModuleStatusChangedEvent(
    string ModuleId,
    string DisplayName,
    ModuleStatus PreviousStatus,
    ModuleStatus NewStatus,
    string? Message) : MaisEvent;

/// <summary>Published when a module is removed from the registry.</summary>
public sealed record ModuleUnregisteredEvent(
    string ModuleId,
    string DisplayName) : MaisEvent;

/// <summary>Published when a module's health check returns a non-healthy result.</summary>
public sealed record ModuleHealthAlertEvent(
    string ModuleId,
    ModuleStatus Status,
    string Message,
    IReadOnlyDictionary<string, object> Diagnostics) : MaisEvent;

// ---------------------------------------------------------------------------
// Event args (used by IModuleRegistry's C# event)
// ---------------------------------------------------------------------------

/// <summary>Arguments supplied to <c>IModuleRegistry.ModuleStatusChanged</c>.</summary>
public sealed class ModuleStatusChangedEventArgs : EventArgs
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required ModuleStatus PreviousStatus { get; init; }
    public required ModuleStatus NewStatus { get; init; }
    public string? Message { get; init; }
}
