using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// Sends batches to Elasticsearch via the bulk API.
/// Uses IdempotencyKey as the document _id so re-delivered batches are safe to re-index.
/// </summary>
public sealed class ElasticsearchSink
{
    private readonly HttpClient              _http;
    private readonly string                  _indexPrefix;
    private readonly ILogger<ElasticsearchSink> _logger;

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ElasticsearchSink(HttpClient http, string indexPrefix, ILogger<ElasticsearchSink> logger)
    {
        _http        = http;
        _indexPrefix = indexPrefix;
        _logger      = logger;
    }

    public async Task<bool> IndexAsync(IReadOnlyList<LogRecord> records, CancellationToken ct)
    {
        if (records.Count == 0) return true;

        var sb = new StringBuilder(records.Count * 300);
        foreach (var r in records)
        {
            var indexName = $"{_indexPrefix}-{r.Timestamp:yyyy.MM.dd}";
            AppendAction(sb, indexName, r.IdempotencyKey);
            sb.Append(JsonSerializer.Serialize(r, _json)).Append('\n');
        }

        return await BulkSendAsync(sb, records.Count, ct);
    }

    public async Task<bool> IndexStatsAsync(IReadOnlyList<StatsAggregate> records, CancellationToken ct)
    {
        if (records.Count == 0) return true;

        var sb = new StringBuilder(records.Count * 200);
        foreach (var r in records)
        {
            var indexName = $"{_indexPrefix}-stats-{r.BucketStart:yyyy.MM.dd}";
            AppendAction(sb, indexName, r.IdempotencyKey);
            sb.Append(JsonSerializer.Serialize(r, _json)).Append('\n');
        }

        return await BulkSendAsync(sb, records.Count, ct);
    }

    private async Task<bool> BulkSendAsync(StringBuilder sb, int recordCount, CancellationToken ct)
    {
        using var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson");
        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsync("/_bulk", content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Elasticsearch bulk request failed for {Count} records", recordCount);
            return false;
        }

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Elasticsearch bulk API returned {Status} for {Count} records",
                resp.StatusCode, recordCount);
            return false;
        }

        var body = await resp.Content.ReadFromJsonAsync<EsBulkResponse>(ct);
        if (body?.Errors == true)
            _logger.LogWarning("Elasticsearch bulk API reported partial errors for {Count} records", recordCount);

        return body?.Errors != true;
    }

    private static void AppendAction(StringBuilder sb, string indexName, string id)
    {
        sb.Append("{\"index\":{\"_index\":");
        sb.Append(JsonSerializer.Serialize(indexName));
        sb.Append(",\"_id\":");
        sb.Append(JsonSerializer.Serialize(id));
        sb.Append("}}\n");
    }

    private sealed class EsBulkResponse
    {
        [JsonPropertyName("errors")]
        public bool Errors { get; init; }
    }
}
