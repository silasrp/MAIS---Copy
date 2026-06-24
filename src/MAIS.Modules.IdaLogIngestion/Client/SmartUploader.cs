using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// Drains the FlatFileSpoolQueue on a configurable interval, compressing batches and
/// uploading them to the server. Backs off exponentially on failure and probes
/// connectivity before attempting uploads so it does not hammer an unreachable server.
/// </summary>
public sealed class SmartUploader
{
    private readonly HttpClient          _http;
    private readonly FlatFileSpoolQueue  _spool;
    private readonly LogSourceDefinition _source;
    private readonly ILogger<SmartUploader> _logger;

    public SmartUploader(
        HttpClient http,
        FlatFileSpoolQueue spool,
        LogSourceDefinition source,
        ILogger<SmartUploader> logger)
    {
        _http   = http;
        _spool  = spool;
        _source = source;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var minBackoff = TimeSpan.FromSeconds(_source.UploadBackoffMinSeconds);
        var maxBackoff = TimeSpan.FromSeconds(_source.UploadBackoffMaxSeconds);
        var backoff    = minBackoff;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { break; }

            _spool.EnforceOverflowPolicy();

            if (!await IsServerReachableAsync(ct))
            {
                backoff = Clamp(backoff * 2, minBackoff, maxBackoff);
                continue;
            }

            // Flush the current partial ingest batch so recently classified records
            // don't sit on disk waiting for the batch-size threshold to trigger.
            _spool.FlushIngest();

            bool ingestOk = await UploadKindAsync(SpoolBatchKind.Ingest, ct);
            bool statsOk  = await UploadKindAsync(SpoolBatchKind.Stats,  ct);

            backoff = (ingestOk && statsOk)
                ? minBackoff
                : Clamp(backoff * 2, minBackoff, maxBackoff);
        }
    }

    private async Task<bool> IsServerReachableAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync("/api/v1/ida/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> UploadKindAsync(SpoolBatchKind kind, CancellationToken ct)
    {
        var batches = _spool.GetReadyBatches(kind);
        if (batches.Count == 0) return true;

        bool allOk = true;
        foreach (var batch in batches)
        {
            if (ct.IsCancellationRequested) break;

            if (!_spool.TryClaimForUpload(batch, out var sendingPath))
                continue;

            try
            {
                await UploadFileAsync(sendingPath, kind, ct);
                _spool.Acknowledge(sendingPath);
                _logger.LogDebug("Uploaded {Kind} batch {Id} ({Bytes} bytes)",
                    kind, batch.BatchId, batch.FileSizeBytes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Upload failed for {Kind} batch {Id}; will retry", kind, batch.BatchId);
                _spool.Release(sendingPath);
                allOk = false;
            }
        }
        return allOk;
    }

    private async Task UploadFileAsync(string filePath, SpoolBatchKind kind, CancellationToken ct)
    {
        using var fileStream = File.OpenRead(filePath);
        using var compressed = new MemoryStream();

        await using (var gz = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            await fileStream.CopyToAsync(gz, ct);

        compressed.Position = 0;

        using var content = new StreamContent(compressed);
        content.Headers.ContentEncoding.Add("gzip");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");

        var endpoint = kind == SpoolBatchKind.Ingest
            ? "/api/v1/ida/ingest/batch"
            : "/api/v1/ida/stats/batch";

        var response = await _http.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;
}
