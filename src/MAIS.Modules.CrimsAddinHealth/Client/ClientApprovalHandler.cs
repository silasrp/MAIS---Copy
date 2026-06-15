using MAIS.Modules.CrimsAddinHealth.Hubs;
using MAIS.Modules.CrimsAddinHealth.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MAIS.Modules.CrimsAddinHealth.Client;

/// <summary>
/// Receives hub-dispatched approval decisions from the sidebar and forwards them
/// to the server's REST API. Only registered on the client service.
/// </summary>
internal sealed class ClientApprovalHandler : IAddinHealthMessageHandler
{
    private readonly IHttpClientFactory _factory;

    public ClientApprovalHandler(IHttpClientFactory factory) => _factory = factory;

    public Task HandleScanResultAsync(ScanResult result, CancellationToken ct)       => Task.CompletedTask;
    public Task HandleCrimsStatusAsync(string queueId, CrimsProcessStatus status, CancellationToken ct) => Task.CompletedTask;
    public Task HandleUpdateOutcomeAsync(string queueId, UpdateOutcome outcome, CancellationToken ct)   => Task.CompletedTask;

    public async Task HandleApprovalAsync(UpdateApproval approval, CancellationToken ct)
    {
        var client  = _factory.CreateClient("AddinHealthServer");
        var json    = JsonSerializer.Serialize(approval);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await client.PostAsync("/api/v1/addin-health/approvals", content, ct);
    }
}
