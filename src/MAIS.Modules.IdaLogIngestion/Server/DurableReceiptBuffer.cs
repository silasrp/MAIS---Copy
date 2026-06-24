using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.IdaLogIngestion.Server;

public enum ReceiptBatchKind { Ingest, Stats }

/// <summary>
/// Crash-safe server-side receipt store. IngestionController writes batches atomically here
/// before returning 202; IndexerWorker and StatsIndexerWorker drain them to Elasticsearch.
/// Each file is NDJSON: one JSON record per line.
/// </summary>
public sealed class DurableReceiptBuffer
{
    private readonly string _ingestDir;
    private readonly string _statsDir;

    private static readonly JsonSerializerOptions _json = new();

    public DurableReceiptBuffer(IOptions<IdaLogIngestionOptions> options)
    {
        var root   = options.Value.ReceiptBufferPath;
        _ingestDir = Path.Combine(root, "ingest");
        _statsDir  = Path.Combine(root, "stats");

        Directory.CreateDirectory(_ingestDir);
        Directory.CreateDirectory(_statsDir);
    }

    public Task WriteIngestAsync(IReadOnlyList<LogRecord> records, CancellationToken ct) =>
        WriteNdjsonAsync(_ingestDir, records, ct);

    public Task WriteStatsAsync(IReadOnlyList<StatsAggregate> records, CancellationToken ct) =>
        WriteNdjsonAsync(_statsDir, records, ct);

    public IReadOnlyList<string> GetPendingBatches(ReceiptBatchKind kind)
    {
        var dir = kind == ReceiptBatchKind.Ingest ? _ingestDir : _statsDir;
        var files = new List<string>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.pending"))
            files.Add(f);
        files.Sort(StringComparer.Ordinal);
        return files.AsReadOnly();
    }

    public void Acknowledge(string filePath)
    {
        try { File.Delete(filePath); }
        catch (IOException) { }
    }

    public Task<IReadOnlyList<LogRecord>> ReadIngestBatchAsync(string filePath, CancellationToken ct) =>
        ReadNdjsonAsync<LogRecord>(filePath, ct);

    public Task<IReadOnlyList<StatsAggregate>> ReadStatsBatchAsync(string filePath, CancellationToken ct) =>
        ReadNdjsonAsync<StatsAggregate>(filePath, ct);

    private static async Task WriteNdjsonAsync<T>(string dir, IReadOnlyList<T> records, CancellationToken ct)
    {
        var baseName  = Guid.NewGuid().ToString("N");
        var tempPath  = Path.Combine(dir, $"{baseName}.tmp");
        var finalPath = Path.Combine(dir, $"{baseName}.pending");

        await using (var stream = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true))
        {
            using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
            foreach (var record in records)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(record, _json).AsMemory(), ct);
            }
            await writer.FlushAsync(ct);
        }

        File.Move(tempPath, finalPath);
    }

    private static async Task<IReadOnlyList<T>> ReadNdjsonAsync<T>(string filePath, CancellationToken ct)
    {
        var result = new List<T>();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var item = JsonSerializer.Deserialize<T>(line, _json);
            if (item is not null) result.Add(item);
        }
        return result.AsReadOnly();
    }
}
