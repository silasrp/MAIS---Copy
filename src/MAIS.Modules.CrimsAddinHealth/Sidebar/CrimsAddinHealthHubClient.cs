using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace MAIS.Modules.CrimsAddinHealth.Sidebar;

/// <summary>
/// Connects to the LOCAL service's AddinHealth hub (client service on 5002,
/// or server service on 5000 when running server-side).
/// Never connects to a remote service directly.
/// </summary>
public sealed class CrimsAddinHealthHubClient : IAsyncDisposable
{
    private readonly HubConnection _hub;

    public event EventHandler<UpdateRequest>? AlertReceived;
    public event EventHandler<string>?        AlertResolved;
    public event EventHandler<QueueEntry>?    StatusChanged;
    public event EventHandler<ToastMessage>? ToastReceived;


    public CrimsAddinHealthHubClient(string localHubUrl)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(localHubUrl)
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)
            ])
            .Build();

        _hub.On<UpdateRequest>("NewUpdateRequestAlert",
            req   => AlertReceived?.Invoke(this, req));

        _hub.On<string>("UpdateRequestResolved",
            id    => AlertResolved?.Invoke(this, id));

        _hub.On<QueueEntry>("UpdateStatusChanged",
            entry => StatusChanged?.Invoke(this, entry));

        _hub.On<ToastMessage>("ShowToastNotification",
            msg => ToastReceived?.Invoke(this, msg));

        _hub.Reconnected += async _ => await JoinApproversGroupAsync();
    }

public async Task StartAsync()
{
    System.Diagnostics.Debug.WriteLine($"[CAH] HubClient.StartAsync entered. Thread={Environment.CurrentManagedThreadId}");
    while (true)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CAH] Calling _hub.StartAsync. Thread={Environment.CurrentManagedThreadId}");
            await _hub.StartAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[CAH] _hub.StartAsync returned. Thread={Environment.CurrentManagedThreadId}");
            await JoinApproversGroupAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[CAH] JoinApproversGroup complete.");
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CAH] StartAsync exception: {ex.GetType().Name}: {ex.Message}");
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
    }
}


    private async Task JoinApproversGroupAsync()
    {
        try   { await _hub.InvokeAsync("JoinApproversGroup").ConfigureAwait(false); }
        catch { }
    }

    public async Task SubmitApprovalAsync(UpdateApproval approval)
    {
        try   { await _hub.InvokeAsync("SubmitApproval", approval).ConfigureAwait(false); }
        catch { }
    }


    public async ValueTask DisposeAsync() => await _hub.DisposeAsync();
}
