using MAIS.Modules.CrimsAddinHealth.Hubs;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.CrimsAddinHealth.Client;

/// <summary>
/// Connects to the server's AddinHealth hub, joins the approvers group,
/// and relays alerts to the local hub so the sidebar never talks to the server directly.
/// </summary>
public sealed class ServerHubRelay : BackgroundService
{
    private readonly IHubContext<AddinHealthHub, IAddinHealthHubClient> _localHub;
    private readonly AddinScanWorker         _scanWorker;
    private readonly CrimsAddinHealthOptions _options;
    private readonly ILogger<ServerHubRelay> _logger;


    public ServerHubRelay(
        IHubContext<AddinHealthHub, IAddinHealthHubClient> localHub,
        AddinScanWorker scanWorker,
        IOptions<CrimsAddinHealthOptions> options,
        ILogger<ServerHubRelay> logger)
    {
        _localHub   = localHub;
        _scanWorker = scanWorker;
        _options    = options.Value;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverHubUrl = _options.ServerApiUrl.TrimEnd('/') + ModuleConstants.HubPath;

        var hub = new HubConnectionBuilder()
            .WithUrl(serverHubUrl)
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)
            ])
            .Build();

        hub.On<UpdateRequest>("NewUpdateRequestAlert", async req =>
            await _localHub.Clients.Group(ModuleConstants.ApproversGroup)
                .NewUpdateRequestAlert(req));

        hub.On<string>("UpdateRequestResolved", async id =>
            await _localHub.Clients.Group(ModuleConstants.ApproversGroup)
                .UpdateRequestResolved(id));

        hub.On<QueueEntry>("UpdateStatusChanged", async entry =>
            await _localHub.Clients.Group(ModuleConstants.ApproversGroup)
                .UpdateStatusChanged(entry));

        hub.On<string>("CheckCrimsStatus", async queueId =>
            await _scanWorker.HandleCheckCrimsStatusAsync(queueId, stoppingToken));

        hub.On<QueueEntry>("InitiateUpdate", async entry =>
            await _scanWorker.HandleInitiateUpdateAsync(entry, stoppingToken));

        hub.On<string>("TriggerScan", async requestId =>
            await _scanWorker.RunOnDemandScanAsync(requestId, stoppingToken));

        hub.Reconnected += async _ =>
        {
            try
            {
                await hub.InvokeAsync("JoinApproversGroup", stoppingToken);
                await hub.InvokeAsync("JoinClientGroup", Environment.MachineName, stoppingToken);
            }
            catch { }
        };

        // Initial connection with retry
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await hub.StartAsync(stoppingToken);
                await hub.InvokeAsync("JoinApproversGroup", stoppingToken);
                await hub.InvokeAsync("JoinClientGroup", Environment.MachineName, stoppingToken);
                _logger.LogInformation("ServerHubRelay connected to {Url}", serverHubUrl);
                break;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning("ServerHubRelay could not connect: {Error}. Retrying in 15s", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        // Hold until shutdown, then clean up
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
        finally { await hub.DisposeAsync(); }
    }
}
