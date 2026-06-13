namespace MAIS.Core.Contracts;

/// <summary>
/// Client registration payload sent to server on startup.
/// Establishes identity, capabilities, and current module state.
/// </summary>
public class ClientRegistrationRequest
{
    /// <summary>Unique client identifier (e.g., hostname, machine GUID).</summary>
    public required string ClientId { get; set; }

    /// <summary>Machine hostname or friendly name for display.</summary>
    public required string MachineName { get; set; }

    /// <summary>Current user or role hint for policy assignment.</summary>
    public required string UserRole { get; set; }

    /// <summary>Status of all client modules at registration time.</summary>
    public List<ModuleStatusReport> Modules { get; set; } = new();

    /// <summary>Client service semantic version.</summary>
    public string? ClientVersion { get; set; }

    /// <summary>Registration timestamp (UTC).</summary>
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
}
