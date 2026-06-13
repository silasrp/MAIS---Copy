namespace MAIS.Server.Service.Configuration;

/// <summary>
/// Strongly-typed configuration for the MAIS server service.
/// Bound from the "Mais" section in appsettings.json.
/// </summary>
public sealed class MaisOptions
{
    public const string SectionName = "Mais";

    /// <summary>How often (seconds) the health monitor polls all modules.</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// How long (seconds) the orchestrator waits for a module to start
    /// before marking it Faulted.
    /// </summary>
    public int ModuleStartTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How long (seconds) the orchestrator waits for a module to stop
    /// during graceful shutdown.
    /// </summary>
    public int ModuleStopTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of consecutive failed health checks before the orchestrator
    /// attempts an automatic restart.
    /// </summary>
    public int MaxConsecutiveHealthFailures { get; set; } = 3;

    /// <summary>
    /// Whether to automatically restart faulted modules.
    /// Should be false in environments with strict change control.
    /// </summary>
    public bool AutoRestartFaultedModules { get; set; } = false;

    /// <summary>Sidebar process path. Launched by the service on startup if set.</summary>
    public string? SidebarExecutablePath { get; set; }

    /// <summary>Whether the service should launch the sidebar on startup.</summary>
    public bool LaunchSidebarOnStart { get; set; } = false;
}
