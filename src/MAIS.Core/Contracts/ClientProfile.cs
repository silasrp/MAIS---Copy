namespace MAIS.Core.Contracts;

/// <summary>
/// Policy and configuration for a client runtime.
/// Retrieved from server at startup and cached locally for policy enforcement.
/// </summary>
public class ClientProfile
{
    /// <summary>Client unique identifier (hostname or custom ID).</summary>
    public required string ClientId { get; set; }

    /// <summary>Role-based assignment (e.g., "Support", "Trader", "Admin").</summary>
    public required string Role { get; set; }

    /// <summary>Whether to launch the WPF sidebar on this client.</summary>
    public bool EnableSidebar { get; set; } = false;

    /// <summary>List of module IDs allowed to run on this client.</summary>
    public List<string> EnabledModules { get; set; } = new();

    /// <summary>Timestamp when policy was fetched from server.</summary>
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
}
