using MAIS.Modules.CrimsAddinHealth.Models;

namespace MAIS.Modules.CrimsAddinHealth.Hubs;

/// <summary>
/// Typed SignalR client interface. All messages the server can push to clients
/// are declared here. Used by both the hub and IHubContext injections.
/// </summary>
public interface IAddinHealthHubClient
{
    // ── Server → Support/Admin sidebar clients ────────────────────────────
    Task NewUpdateRequestAlert(UpdateRequest request);
    Task UpdateRequestResolved(string requestId);
    Task UpdateStatusChanged(QueueEntry entry);
    Task ScheduledUpdatesChanged(IReadOnlyList<QueueEntry> scheduled);

    // ── Server → specific client (targeted by ClientId group) ────────────
    Task TriggerScan(string requestId);
    Task CheckCrimsStatus(string queueId);
    Task InitiateUpdate(QueueEntry entry);
    Task CancelUpdate(string queueId, string reason);

    // ── Client service → Sidebar (local hub only) ─────────────────────────
    Task ShowToastNotification(ToastMessage message);
    Task DismissToastNotification(string toastId);
}