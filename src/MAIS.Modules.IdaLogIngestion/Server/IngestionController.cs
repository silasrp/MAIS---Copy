using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// Exposes all IDA log-ingestion HTTP endpoints under /api/v1/ida.
///
/// Endpoints:
///   GET  /health                         — liveness probe (unauthenticated)
///   POST /ingest/batch                   — gzip NDJSON of LogRecord[], accepts client uploads
///   POST /stats/batch                    — gzip NDJSON of StatsAggregate[], accepts client uploads
///   POST /templates/novel                — novel-template report from a client
///   GET  /templates/registry             — conditional registry GET (?appId=&currentVersion=)
///   GET  /templates/pending              — pending review items for the sidebar
///   POST /templates/{templateId}/approve — approve/classify a pending novel template
///   GET  /rate                           — recent rate buckets for sidebar backfill
/// </summary>
[ApiController]
[Route("api/v1/ida")]
public sealed class IngestionController : ControllerBase
{
    private readonly DurableReceiptBuffer   _buffer;
    private readonly TemplateRegistryService _registry;
    private readonly IngestionRateTracker   _rateTracker;

    private static readonly JsonSerializerOptions _json = new();

    public IngestionController(
        DurableReceiptBuffer buffer,
        TemplateRegistryService registry,
        IngestionRateTracker rateTracker)
    {
        _buffer      = buffer;
        _registry    = registry;
        _rateTracker = rateTracker;
    }

    // ── Liveness ─────────────────────────────────────────────────────────────

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok" });

    // ── Ingest batch ─────────────────────────────────────────────────────────

    [HttpPost("ingest/batch")]
    public async Task<IActionResult> IngestBatch(CancellationToken ct)
    {
        var records = await ReadBodyAsync<LogRecord>(ct);
        if (records.Count == 0) return BadRequest("Empty or unreadable batch");

        await _buffer.WriteIngestAsync(records, ct);
        return Accepted();
    }

    // ── Stats batch ──────────────────────────────────────────────────────────

    [HttpPost("stats/batch")]
    public async Task<IActionResult> StatsBatch(CancellationToken ct)
    {
        var records = await ReadBodyAsync<StatsAggregate>(ct);
        if (records.Count == 0) return BadRequest("Empty or unreadable batch");

        await _buffer.WriteStatsAsync(records, ct);
        return Accepted();
    }

    // ── Novel template report ─────────────────────────────────────────────────

    [HttpPost("templates/novel")]
    public async Task<IActionResult> ReportNovel([FromBody] NovelTemplateCandidate candidate, CancellationToken ct)
    {
        await _registry.ReportNovelAsync(candidate, ct);
        return Accepted();
    }

    // ── Registry conditional GET ──────────────────────────────────────────────

    [HttpGet("templates/registry")]
    public async Task<IActionResult> GetRegistry(
        [FromQuery] string appId,
        [FromQuery] string currentVersion,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appId)) return BadRequest("appId required");

        var snapshot = await _registry.GetRegistryAsync(appId, ct);
        if (snapshot.Version.ToString() == currentVersion)
            return StatusCode(304);

        return Ok(snapshot);
    }

    // ── Pending review items ──────────────────────────────────────────────────

    [HttpGet("templates/pending")]
    public async Task<IActionResult> GetPendingReviews([FromQuery] string appId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appId)) return BadRequest("appId required");

        var items = await _registry.GetPendingReviewsAsync(appId, ct);
        return Ok(items);
    }

    // ── Approve template ──────────────────────────────────────────────────────

    [HttpPost("templates/{templateId}/approve")]
    public async Task<IActionResult> ApproveTemplate(
        string templateId,
        [FromBody] ApproveRequest request,
        CancellationToken ct)
    {
        var approvedBy = User.Identity?.Name ?? "unknown";
        await _registry.ApproveAsync(templateId, request.Action, approvedBy, ct);
        return Ok();
    }

    // ── Rate backfill ─────────────────────────────────────────────────────────

    [HttpGet("rate")]
    public IActionResult GetRate()
    {
        var buckets = _rateTracker.GetRecentBuckets();
        return Ok(buckets);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<T>> ReadBodyAsync<T>(CancellationToken ct)
    {
        var isGzip = Request.Headers.ContentEncoding.Contains("gzip");

        Stream source = isGzip
            ? new GZipStream(Request.Body, CompressionMode.Decompress, leaveOpen: true)
            : Request.Body;

        try
        {
            using var reader = new StreamReader(source, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            var result = new List<T>();
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonSerializer.Deserialize<T>(line, _json);
                if (item is not null) result.Add(item);
            }
            return result.AsReadOnly();
        }
        finally
        {
            if (isGzip) await source.DisposeAsync();
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public sealed record ApproveRequest(ClassificationAction Action);
}
