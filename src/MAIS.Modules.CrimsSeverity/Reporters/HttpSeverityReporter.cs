using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsSeverity.Reporters;

/// <summary>
/// Client-side reporter that logs severity data locally.
/// Used when the module runs on MAIS.Client.Service.
/// The module reports its own status separately via the normal health reporting mechanism.
/// This reporter simply ensures the module can consume and process data without errors.
/// </summary>
public sealed class HttpSeverityReporter : ISeverityReporter
{
    private readonly ILogger<HttpSeverityReporter> _logger;

    public HttpSeverityReporter(ILogger<HttpSeverityReporter> logger)
    {
        _logger = logger;
    }

    public Task ReportSeverityDataAsync(SeverityDataUpdate update, CancellationToken ct)
    {
        _logger.LogDebug(
            "Severity data received (client-side): {EntryCount} entries, Spike={IsSpikeActive}, Delta={CriticalDelta}",
            update.Data.Count, update.IsSpikeActive, update.CriticalDelta);

        return Task.CompletedTask;
    }

    public Task ReportEndpointUnavailableAsync(string reason, CancellationToken ct)
    {
        _logger.LogWarning("Severity endpoint unavailable (client-side): {Reason}", reason);
        return Task.CompletedTask;
    }

    public Task ReportModuleStatusAsync(string moduleId, string status, string? message, CancellationToken ct)
    {
        _logger.LogDebug("Module status change (client-side): {ModuleId} -> {Status}: {Message}",
            moduleId, status, message ?? "(no message)");
        return Task.CompletedTask;
    }
}
