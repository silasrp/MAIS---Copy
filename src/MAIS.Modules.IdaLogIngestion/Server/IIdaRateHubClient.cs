using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// Typed SignalR client interface for the IdaRateHub.
/// The server pushes completed rate buckets; clients have no server-callable methods.
/// </summary>
public interface IIdaRateHubClient
{
    Task ReceiveRateBucket(IngestionRateBucket bucket);
}
