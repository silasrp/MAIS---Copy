namespace MAIS.Core.Models;

/// <summary>
/// A point-in-time health snapshot returned by <c>IModule.GetHealthAsync</c>.
/// Carries structured diagnostics so the health monitor can make informed decisions
/// without needing to know each module's internals.
/// </summary>
public sealed record ModuleHealth
{
    /// <summary>The id of the module this health record describes.</summary>
    public required string ModuleId { get; init; }

    /// <summary>Observed status at the time of the check.</summary>
    public required ModuleStatus Status { get; init; }

    /// <summary>Human-readable explanation of the current status.</summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Structured key-value diagnostics (e.g. queue depth, latency, last run time).
    /// Values must be primitive types safe to serialise to JSON.
    /// </summary>
    public IReadOnlyDictionary<string, object> Diagnostics { get; init; }
        = new Dictionary<string, object>();

    /// <summary>UTC timestamp when this health check was performed.</summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Elapsed time taken to complete the health check.</summary>
    public TimeSpan CheckDuration { get; init; }

    // --- Factory helpers -------------------------------------------------------

    public static ModuleHealth Healthy(string moduleId, string? message = null,
        IReadOnlyDictionary<string, object>? diagnostics = null) =>
        new()
        {
            ModuleId = moduleId,
            Status = ModuleStatus.Running,
            StatusMessage = message ?? "Healthy",
            Diagnostics = diagnostics ?? new Dictionary<string, object>()
        };

    public static ModuleHealth Degraded(string moduleId, string message,
        IReadOnlyDictionary<string, object>? diagnostics = null) =>
        new()
        {
            ModuleId = moduleId,
            Status = ModuleStatus.Degraded,
            StatusMessage = message,
            Diagnostics = diagnostics ?? new Dictionary<string, object>()
        };

    public static ModuleHealth Faulted(string moduleId, Exception exception) =>
        new()
        {
            ModuleId = moduleId,
            Status = ModuleStatus.Faulted,
            StatusMessage = exception.Message,
            Diagnostics = new Dictionary<string, object>
            {
                ["exceptionType"] = exception.GetType().Name,
                ["stackTrace"] = exception.StackTrace ?? string.Empty
            }
        };
}
