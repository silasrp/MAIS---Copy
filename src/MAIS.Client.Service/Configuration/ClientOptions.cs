namespace MAIS.Client.Service.Configuration;

/// <summary>
/// Client-specific configuration options (extends MaisOptions from MAIS.Infrastructure).
/// </summary>
public sealed class ClientOptions
{
    public const string SectionName = "Client";

    /// <summary>Unique client identifier. If empty, will be generated from hostname + GUID.</summary>
    public string? ClientId { get; set; }

    /// <summary>Server URL for API communication (e.g., "https://localhost:5001").</summary>
    public string ServerUrl { get; set; } = "https://localhost:5001";

    /// <summary>User role for policy assignment (e.g., "Support", "Trader", "Admin").</summary>
    public string UserRole { get; set; } = "Default";

    /// <summary>Whether to launch the sidebar on this client (can be overridden by server policy).</summary>
    public bool LaunchSidebarOnStart { get; set; } = false;

    /// <summary>Interval in seconds for health reporting to server.</summary>
    public int HealthReportIntervalSeconds { get; set; } = 60;

    /// <summary>Interval in seconds for policy refresh from server.</summary>
    public int PolicyRefreshIntervalSeconds { get; set; } = 300;

    /// <summary>Authentication token or API key for server communication.</summary>
    public string? ApiKey { get; set; }
}
