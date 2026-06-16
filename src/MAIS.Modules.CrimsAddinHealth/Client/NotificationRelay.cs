using MAIS.Modules.CrimsAddinHealth.Hubs;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsAddinHealth.Client;

public sealed class NotificationRelay
{
    private readonly IHubContext<AddinHealthHub, IAddinHealthHubClient> _hub;
    private readonly ILogger<NotificationRelay> _logger;

    public NotificationRelay(
        IHubContext<AddinHealthHub, IAddinHealthHubClient> hub,
        ILogger<NotificationRelay> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    public void ShowToast(ToastMessage message)
    {
        _ = Task.Run(async () =>
        {
            try   { await _hub.Clients.All.ShowToastNotification(message); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to relay toast: {Title}", message.Title); }
        });
    }

    public void DismissToast(string toastId)
    {
        _ = Task.Run(async () =>
        {
            try   { await _hub.Clients.All.DismissToastNotification(toastId); }
            catch { }
        });
    }
}
