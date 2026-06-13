namespace MAIS.Sidebar.Configuration;

/// <summary>
/// Configuration for connecting to MAIS Services.
/// Bound from appsettings.json "ServiceConnection" section.
/// 
/// Separates concerns:
/// - ApiBaseUrl: REST API endpoint (can be Client or Server)
/// - SignalRUrl: SignalR Hub endpoint (always Server for real-time updates)
/// </summary>
public sealed class ServiceConnectionOptions
{
    public const string SectionName = "ServiceConnection";

    /// <summary>
    /// Base URL for REST API (modules discovery).
    /// Can point to Client Service (5002) or Server Service (5000).
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5002";

    /// <summary>
    /// SignalR Hub endpoint URL (real-time updates).
    /// Always points to Server Service (5001) for real-time module status.
    /// </summary>
    public string SignalRUrl { get; set; } = "https://localhost:5001";

    /// <summary>HTTP request timeout in seconds.</summary>
    public int Timeout { get; set; } = 10;
}
