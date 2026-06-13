using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsSeverity
{
    // ── DTOs ─────────────────────────────────────────────────────────────────

    public sealed class SeverityEntry
    {
        public string Key   { get; set; } = "";
        public int    Count { get; set; }
    }

    public sealed class SeverityDataUpdate
    {
        public required IReadOnlyList<SeverityEntry> Data                    { get; init; }
        public required bool                         IsSpikeActive           { get; init; }
        public required int                          CooldownRemainingSeconds { get; init; }
        public required int                          CriticalDelta            { get; init; }
        public required string                       SourceApplicationUrl    { get; init; }
        public DateTimeOffset                        Timestamp               { get; init; } = DateTimeOffset.UtcNow;
    }

    // ── Hub ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// SignalR hub for the CRIMS severity module.
    /// Self-contained in the module assembly and mapped by the module's own
    /// <see cref="Extensions.ServiceExtensions.UseCrimsSeverityModule"/> extension.
    /// </summary>
    public sealed class CrimsSeverityHub : Hub<ICrimsSeverityHubClient>
    {
        private readonly ILogger<CrimsSeverityHub> _logger;

        public CrimsSeverityHub(ILogger<CrimsSeverityHub> logger) => _logger = logger;

        public override Task OnConnectedAsync()
        {
            _logger.LogDebug("Severity client connected: {Id}", Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? ex)
        {
            _logger.LogDebug("Severity client disconnected: {Id}", Context.ConnectionId);
            return base.OnDisconnectedAsync(ex);
        }
    }

    public interface ICrimsSeverityHubClient
    {
        Task SeverityDataUpdated(SeverityDataUpdate update);
        Task SeverityEndpointUnavailable(string reason);
    }
}
