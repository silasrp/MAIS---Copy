using MAIS.Core.Models;

namespace MAIS.Core.Abstractions;

/// <summary>
/// Defines the contract for all MAIS modules. Every capability unit — whether an
/// in-process worker, a containerised service, or an external endpoint — implements
/// this interface so the orchestration layer can manage it uniformly.
/// </summary>
public interface IModule
{
    /// <summary>Stable, unique identifier. Use reverse-domain notation: "mais.sla-monitor".</summary>
    string Id { get; }

    /// <summary>Human-readable display name shown in the sidebar.</summary>
    string DisplayName { get; }

    /// <summary>Brief description of what this module does.</summary>
    string Description { get; }

    /// <summary>Semantic version of this module.</summary>
    string Version { get; }

    /// <summary>Deployment topology of this module.</summary>
    ModuleType Type { get; }

    /// <summary>
    /// Optional URI used by the sidebar to deep-link into this module's UI.
    /// Null means the module has no interactive UI.
    /// </summary>
    Uri? LaunchUri { get; }

    /// <summary>
    /// Specifies where this module is deployed and executed.
    /// Used for filtering modules during service startup based on deployment model.
    /// </summary>
    ModuleHostType HostType { get; }

    /// <summary>
    /// Called once by the orchestrator to initialise the module before it starts.
    /// Use this for dependency checks and one-time setup — not for long-running work.
    /// </summary>
    Task InitialiseAsync(CancellationToken cancellationToken);

    /// <summary>Begins the module's active work.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gracefully stops the module. Implementations must honour the cancellation token
    /// and complete within the host's shutdown timeout.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a point-in-time health snapshot. Called periodically by the health monitor.
    /// Must never throw — return a Faulted health record instead.
    /// </summary>
    Task<ModuleHealth> GetHealthAsync(CancellationToken cancellationToken);
}
