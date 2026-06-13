using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsAddinHealth.Client;

/// <summary>
/// Receives toast commands from the local SignalR hub and displays Windows toast notifications.
/// Runs on the client service only.
/// </summary>
public sealed class NotificationRelay
{
    private readonly ILogger<NotificationRelay> _logger;

    public NotificationRelay(ILogger<NotificationRelay> logger) => _logger = logger;

    public void ShowToast(ToastMessage message)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(message.Title)
                .AddText(message.Body);

            if (message.RequiresAction && !string.IsNullOrWhiteSpace(message.ActionLabel))
                builder.AddButton(new ToastButton(message.ActionLabel, message.ToastId));

            builder.Show(toast =>
            {
                toast.Tag   = message.ToastId;
                toast.Group = "MAIS-AddinHealth";
            });

            _logger.LogDebug("Toast shown: {Title}", message.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show toast: {Title}", message.Title);
        }
    }

    public void DismissToast(string toastId)
    {
        try
        {
            ToastNotificationManagerCompat.History.Remove(toastId, "MAIS-AddinHealth");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dismiss toast {ToastId}", toastId);
        }
    }
}