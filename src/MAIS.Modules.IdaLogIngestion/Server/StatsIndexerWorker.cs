using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// BackgroundService that drains stats receipt batches into the Elasticsearch stats index.
/// Mirrors IndexerWorker's drain pattern without the rate-tracking concern.
/// </summary>
public sealed class StatsIndexerWorker : BackgroundService
{
    private readonly DurableReceiptBuffer    _buffer;
    private readonly ElasticsearchSink       _sink;
    private readonly int                     _maxConcurrent;
    private readonly ILogger<StatsIndexerWorker> _logger;

    public StatsIndexerWorker(
        DurableReceiptBuffer buffer,
        ElasticsearchSink sink,
        IOptions<IdaLogIngestionOptions> options,
        ILogger<StatsIndexerWorker> logger)
    {
        _buffer        = buffer;
        _sink          = sink;
        _maxConcurrent = options.Value.MaxConcurrentIndexBatches;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batches = _buffer.GetPendingBatches(ReceiptBatchKind.Stats);
            if (batches.Count == 0)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            foreach (var chunk in batches.Chunk(_maxConcurrent))
            {
                if (stoppingToken.IsCancellationRequested) break;
                await Task.WhenAll(chunk.Select(f => IndexBatchAsync(f, stoppingToken)));
            }
        }
    }

    private async Task IndexBatchAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var records = await _buffer.ReadStatsBatchAsync(filePath, ct);
            if (records.Count == 0) { _buffer.Acknowledge(filePath); return; }

            if (await _sink.IndexStatsAsync(records, ct))
                _buffer.Acknowledge(filePath);
            else
                _logger.LogWarning("ES stats indexing failed for {File}; batch queued for retry", filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled error processing stats receipt batch {File}", filePath);
        }
    }
}
