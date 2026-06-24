using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using MAIS.Modules.IdaLogIngestion.Server;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace MAIS.Modules.IdaLogIngestion.Sidebar;

/// <summary>
/// Client-side BackgroundService that maintains a SignalR connection to IdaRateHub.
/// On connection it immediately backfills recent rate buckets via REST, then receives
/// live pushes until the service stops or the connection drops.
///
/// Retries with a 15-second pause on any connection failure.
/// WithAutomaticReconnect handles transient network blips within each session.
/// </summary>
public sealed class IdaRateRelayWorker : BackgroundService
{
    private readonly IngestionRateCardViewModel  _viewModel;
    private readonly HttpClient                  _http;
    private readonly string                      _hubUrl;
    private readonly ILogger<IdaRateRelayWorker> _logger;

    public IdaRateRelayWorker(
        IngestionRateCardViewModel viewModel,
        HttpClient http,
        string hubUrl,
        ILogger<IdaRateRelayWorker> logger)
    {
        _viewModel = viewModel;
        _http      = http;
        _hubUrl    = hubUrl;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            _viewModel.SetConnected(false);

            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _viewModel.SetConnected(false);
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        connection.On<IngestionRateBucket>(nameof(IIdaRateHubClient.ReceiveRateBucket),
            _viewModel.AddBucket);

        try
        {
            await connection.StartAsync(ct);
            _viewModel.SetConnected(true);
            _logger.LogInformation("IdaRateRelayWorker connected to {Url}", _hubUrl);

            // Backfill the last hour of rate buckets from the REST endpoint.
            var backfill = await _http.GetFromJsonAsync<IReadOnlyList<IngestionRateBucket>>(
                "/api/v1/ida/rate", ct);
            if (backfill is not null)
                foreach (var bucket in backfill)
                    _viewModel.AddBucket(bucket);

            // Hold the connection open; WithAutomaticReconnect handles transient failures.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdaRateRelayWorker hub session ended; will retry");
        }
        finally
        {
            _viewModel.SetConnected(false);
            await connection.DisposeAsync();
        }
    }
}
