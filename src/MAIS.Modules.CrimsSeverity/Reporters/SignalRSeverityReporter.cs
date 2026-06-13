using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsSeverity.Reporters;

/// <summary>
/// Server-side reporter using SignalR to broadcast severity data to connected clients.
/// Used when the module runs on MAIS.Server.Service.
/// </summary>
public sealed class SignalRSeverityReporter : ISeverityReporter
{
    private readonly IHubContext<CrimsSeverityHub, ICrimsSeverityHubClient> _hub;
    private readonly ILogger<SignalRSeverityReporter> _logger;

    public SignalRSeverityReporter(
        IHubContext<CrimsSeverityHub, ICrimsSeverityHubClient> hub,
        ILogger<SignalRSeverityReporter> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task ReportSeverityDataAsync(SeverityDataUpdate update, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.All.SeverityDataUpdated(update);
            _logger.LogDebug("Severity data broadcasted via SignalR: {EntryCount} entries, Spike={IsSpikeActive}",
                update.Data.Count, update.IsSpikeActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast severity data via SignalR");
        }
    }

    public async Task ReportEndpointUnavailableAsync(string reason, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.All.SeverityEndpointUnavailable(reason);
            _logger.LogWarning("Severity endpoint unavailability broadcasted via SignalR: {Reason}", reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast endpoint unavailability via SignalR");
        }
    }

    public async Task ReportModuleStatusAsync(string moduleId, string status, string? message, CancellationToken ct)
    {
        // For server, status updates are handled via IModuleRegistry
        // This is a no-op but kept for interface compliance
        _logger.LogDebug("Module status change logged: {ModuleId} -> {Status}: {Message}",
            moduleId, status, message ?? "(no message)");
        await Task.CompletedTask;
    }
}
