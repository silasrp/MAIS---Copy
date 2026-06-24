using Microsoft.AspNetCore.SignalR;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// SignalR hub that delivers live ingestion-rate updates to connected sidebar clients.
/// The server pushes only; no client-to-server calls are defined.
/// </summary>
public sealed class IdaRateHub : Hub<IIdaRateHubClient>
{
    // Intentionally empty — this hub exists purely to provide a typed connection target.
    // IngestionRateTracker (via IndexerWorker) calls IHubContext<IdaRateHub, IIdaRateHubClient>
    // to broadcast completed rate buckets to all connected clients.
}
