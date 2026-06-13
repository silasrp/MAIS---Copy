namespace MAIS.Core.Contracts;

/// <summary>
/// Server response to client registration request.
/// </summary>
public class ClientRegistrationResponse
{
    /// <summary>Echoed client ID for confirmation.</summary>
    public required string ClientId { get; set; }

    /// <summary>Whether registration was successful.</summary>
    public bool Registered { get; set; }

    /// <summary>Server-side timestamp of registration.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Optional message from server (e.g., policy notes).</summary>
    public string? Message { get; set; }
}
