using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// BackgroundService that drains ingest receipt batches into Elasticsearch and publishes
/// completed rate buckets to connected SignalR clients.
///
/// Runs two concurrent loops:
///   - Drain loop: polls DurableReceiptBuffer, indexes up to MaxConcurrentIndexBatches at a time.
///   - Rate-publish loop: ticks every minute, drains IngestionRateTracker and broadcasts.
/// </summary>
public sealed class IndexerWorker : BackgroundService
{
    private readonly DurableReceiptBuffer                          _buffer;
    private readonly ElasticsearchSink                             _sink;
    private readonly IngestionRateTracker                          _rateTracker;
    private readonly IHubContext<IdaRateHub, IIdaRateHubClient>    _hub;
    private readonly int                                           _maxConcurrent;
    private readonly ILogger<IndexerWorker>                        _logger;

    public IndexerWorker(
        DurableReceiptBuffer buffer,
        ElasticsearchSink sink,
        IngestionRateTracker rateTracker,
        IHubContext<IdaRateHub, IIdaRateHubClient> hub,
        IOptions<IdaLogIngestionOptions> options,
        ILogger<IndexerWorker> logger)
    {
        _buffer        = buffer;
        _sink          = sink;
        _rateTracker   = rateTracker;
        _hub           = hub;
        _maxConcurrent = options.Value.MaxConcurrentIndexBatches;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            DrainLoopAsync(stoppingToken),
            RatePublishLoopAsync(stoppingToken));
    }

    private async Task DrainLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batches = _buffer.GetPendingBatches(ReceiptBatchKind.Ingest);
            if (batches.Count == 0)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            foreach (var chunk in batches.Chunk(_maxConcurrent))
            {
                if (ct.IsCancellationRequested) break;
                await Task.WhenAll(chunk.Select(f => IndexBatchAsync(f, ct)));
            }
        }
    }

    private async Task IndexBatchAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var records = await _buffer.ReadIngestBatchAsync(filePath, ct);
            if (records.Count == 0) { _buffer.Acknowledge(filePath); return; }

            if (await _sink.IndexAsync(records, ct))
            {
                _buffer.Acknowledge(filePath);
                _rateTracker.Record(records.Count);
            }
            else
            {
                _logger.LogWarning("ES indexing failed for {File}; batch queued for retry", filePath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled error processing receipt batch {File}", filePath);
        }
    }

    private async Task RatePublishLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var closed = _rateTracker.DrainClosedBuckets();
            foreach (var bucket in closed)
            {
                try { await _hub.Clients.All.ReceiveRateBucket(bucket); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Rate bucket push to hub failed");
                }
            }
        }
    }
}
