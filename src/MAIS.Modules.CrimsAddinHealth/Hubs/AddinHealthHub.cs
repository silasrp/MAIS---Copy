using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsAddinHealth.Hubs;

/// <summary>
/// SignalR hub for the CRIMS addin health module.
/// Hosted on both server (/hubs/addin-health) and client (same path, local port).
///
/// Clients join named groups:
///   "approvers"       — support/admin sidebar clients that receive alert broadcasts
///   "client:{id}"     — each client service joins its own group for targeted commands
///
/// Inbound hub calls from clients (scan results, CRIMS status, outcomes, approvals)
/// are dispatched to IAddinHealthMessageHandler so the hub stays thin.
/// </summary>
public sealed class AddinHealthHub : Hub<IAddinHealthHubClient>
{
    private readonly IAddinHealthMessageHandler _handler;
    private readonly ILogger<AddinHealthHub>    _logger;

    public AddinHealthHub(IAddinHealthMessageHandler handler, ILogger<AddinHealthHub> logger)
    {
        _handler = handler;
        _logger  = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogDebug("AddinHealth client connected: {Id}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? ex)
    {
        _logger.LogDebug("AddinHealth client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(ex);
    }

    // ── Group management (called by connecting clients) ───────────────────

    public Task JoinApproversGroup() =>
        Groups.AddToGroupAsync(Context.ConnectionId, ModuleConstants.ApproversGroup);

    public Task JoinClientGroup(string clientId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, ModuleConstants.ClientGroupPrefix + clientId);

    // ── Client → Server messages ──────────────────────────────────────────

    public Task ReportScanResult(ScanResult result) =>
        _handler.HandleScanResultAsync(result, Context.GetHttpContext()?.RequestAborted ?? default);

    public Task ReportCrimsStatus(string queueId, CrimsProcessStatus status) =>
        _handler.HandleCrimsStatusAsync(queueId, status, Context.GetHttpContext()?.RequestAborted ?? default);

    public Task ReportUpdateOutcome(string queueId, UpdateOutcome outcome) =>
        _handler.HandleUpdateOutcomeAsync(queueId, outcome, Context.GetHttpContext()?.RequestAborted ?? default);

    public Task SubmitApproval(UpdateApproval approval) =>
        _handler.HandleApprovalAsync(approval, Context.GetHttpContext()?.RequestAborted ?? default);
}

/// <summary>
/// Receives inbound hub messages and dispatches them to the appropriate server-side handler.
/// Implemented by UpdateOrchestrator on the server; no-op on the client.
/// </summary>
public interface IAddinHealthMessageHandler
{
    Task HandleScanResultAsync(ScanResult result, CancellationToken ct);
    Task HandleCrimsStatusAsync(string queueId, CrimsProcessStatus status, CancellationToken ct);
    Task HandleUpdateOutcomeAsync(string queueId, UpdateOutcome outcome, CancellationToken ct);
    Task HandleApprovalAsync(UpdateApproval approval, CancellationToken ct);
}

/// <summary>
/// No-op implementation for the client service — the client never receives
/// inbound hub calls of this type (server sends them; clients only respond).
/// </summary>
public sealed class NullAddinHealthMessageHandler : IAddinHealthMessageHandler
{
    public Task HandleScanResultAsync(ScanResult result, CancellationToken ct) => Task.CompletedTask;
    public Task HandleCrimsStatusAsync(string queueId, CrimsProcessStatus status, CancellationToken ct) => Task.CompletedTask;
    public Task HandleUpdateOutcomeAsync(string queueId, UpdateOutcome outcome, CancellationToken ct) => Task.CompletedTask;
    public Task HandleApprovalAsync(UpdateApproval approval, CancellationToken ct) => Task.CompletedTask;
}