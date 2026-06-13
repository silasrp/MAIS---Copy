namespace MAIS.Modules.CrimsSeverity.Reporters;

/// <summary>
/// Abstraction for reporting severity data from the CrimsSeverity module.
/// Allows the module to work on both Client and Server without knowing about transport.
/// Implementations handle transport-specific concerns (SignalR, HTTP, etc).
/// </summary>
public interface ISeverityReporter
{
    /// <summary>Report severity data update to connected consumers.</summary>
    Task ReportSeverityDataAsync(SeverityDataUpdate update, CancellationToken ct);

    /// <summary>Report that the severity endpoint is unavailable.</summary>
    Task ReportEndpointUnavailableAsync(string reason, CancellationToken ct);

    /// <summary>Report a module status change.</summary>
    Task ReportModuleStatusAsync(string moduleId, string status, string? message, CancellationToken ct);
}
