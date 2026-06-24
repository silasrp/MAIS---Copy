using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// Crash-safe durable queue backed by plain files on disk. No native dependencies.
///
/// File lifecycle:
///   {guid}.writing  — current batch being appended to
///   {guid}.ready    — sealed and waiting for upload
///   {guid}.sending  — claimed by SmartUploader for an in-progress upload attempt
///
/// On startup, any leftover .sending files (from a previous crash mid-upload) are
/// renamed back to .ready so they are retried rather than lost.
///
/// Each file is NDJSON: one serialised record per line.
/// </summary>
public sealed class FlatFileSpoolQueue : IDisposable
{
    private readonly string              _ingestDir;
    private readonly string              _statsDir;
    private readonly LogSourceDefinition _source;
    private readonly object              _syncRoot = new();

    private ActiveBatch? _ingestBatch;
    private ActiveBatch? _statsBatch;

    private static readonly JsonSerializerOptions _json = new();

    private const int  MaxRecordsPerBatch = 1_000;
    private const long MaxBatchBytes      = 1 * 1024 * 1024; // 1 MB

    public FlatFileSpoolQueue(string spoolRoot, LogSourceDefinition source)
    {
        _source    = source;
        _ingestDir = Path.Combine(spoolRoot, "ingest", source.AppId);
        _statsDir  = Path.Combine(spoolRoot, "stats",  source.AppId);

        Directory.CreateDirectory(_ingestDir);
        Directory.CreateDirectory(_statsDir);

        RecoverSendingFiles();
    }

    public void EnqueueIngest(LogRecord record)
    {
        lock (_syncRoot)
        {
            _ingestBatch ??= OpenNewBatch(_ingestDir);
            _ingestBatch.WriteLine(JsonSerializer.Serialize(record, _json));
            if (_ingestBatch.ShouldSeal(MaxRecordsPerBatch, MaxBatchBytes))
                SealBatch(ref _ingestBatch, _ingestDir);
        }
    }

    public void EnqueueStats(StatsAggregate aggregate)
    {
        lock (_syncRoot)
        {
            _statsBatch ??= OpenNewBatch(_statsDir);
            _statsBatch.WriteLine(JsonSerializer.Serialize(aggregate, _json));
            if (_statsBatch.ShouldSeal(MaxRecordsPerBatch, MaxBatchBytes))
                SealBatch(ref _statsBatch, _statsDir);
        }
    }

    /// <summary>Seals the current ingest writing batch into a ready batch.</summary>
    public void FlushIngest()
    {
        lock (_syncRoot)
            if (_ingestBatch?.RecordCount > 0)
                SealBatch(ref _ingestBatch, _ingestDir);
    }

    /// <summary>Seals the current stats writing batch into a ready batch.</summary>
    public void FlushStats()
    {
        lock (_syncRoot)
            if (_statsBatch?.RecordCount > 0)
                SealBatch(ref _statsBatch, _statsDir);
    }

    public IReadOnlyList<SpoolBatch> GetReadyBatches(SpoolBatchKind kind)
    {
        var dir = KindDir(kind);
        return Directory.EnumerateFiles(dir, "*.ready")
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new SpoolBatch
                {
                    BatchId       = Path.GetFileNameWithoutExtension(f),
                    Kind          = kind,
                    AppId         = _source.AppId,
                    FilePath      = f,
                    RecordCount   = 0,
                    CreatedAt     = new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                    FileSizeBytes = info.Length
                };
            })
            .OrderBy(b => b.CreatedAt)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>Atomically claims a ready batch for upload by renaming it to .sending.</summary>
    public bool TryClaimForUpload(SpoolBatch batch, out string sendingPath)
    {
        sendingPath = Path.ChangeExtension(batch.FilePath, ".sending");
        try
        {
            File.Move(batch.FilePath, sendingPath);
            return true;
        }
        catch (IOException)
        {
            sendingPath = "";
            return false;
        }
    }

    /// <summary>Deletes a successfully uploaded batch.</summary>
    public void Acknowledge(string sendingPath)
    {
        try { File.Delete(sendingPath); }
        catch (IOException) { }
    }

    /// <summary>Renames a .sending batch back to .ready so it is retried on the next cycle.</summary>
    public void Release(string sendingPath)
    {
        try { File.Move(sendingPath, Path.ChangeExtension(sendingPath, ".ready"), overwrite: false); }
        catch (IOException) { }
    }

    /// <summary>
    /// Deletes the oldest ready batches when total spool size exceeds MaxQueueSizeMb,
    /// and any batch older than MaxQueueAgeHours regardless of size.
    /// Called by SmartUploader at the start of each upload cycle.
    /// </summary>
    public void EnforceOverflowPolicy()
    {
        var maxBytes   = (long)_source.MaxQueueSizeMb * 1024 * 1024;
        var maxAge     = TimeSpan.FromHours(_source.MaxQueueAgeHours);
        var cutoffTime = DateTime.UtcNow - maxAge;

        foreach (var dir in new[] { _ingestDir, _statsDir })
        {
            // Age eviction
            foreach (var file in Directory.EnumerateFiles(dir, "*.ready"))
                if (File.GetCreationTimeUtc(file) < cutoffTime)
                    try { File.Delete(file); } catch (IOException) { }

            // Size eviction: delete oldest until under the cap
            var files = Directory.EnumerateFiles(dir, "*.ready")
                .Select(f => (Path: f, Info: new FileInfo(f)))
                .OrderBy(f => f.Info.CreationTimeUtc)
                .ToList();

            long totalBytes = files.Sum(f => f.Info.Length);
            foreach (var (path, info) in files)
            {
                if (totalBytes <= maxBytes) break;
                try
                {
                    File.Delete(path);
                    totalBytes -= info.Length;
                }
                catch (IOException) { }
            }
        }
    }

    private void RecoverSendingFiles()
    {
        foreach (var dir in new[] { _ingestDir, _statsDir })
            foreach (var file in Directory.EnumerateFiles(dir, "*.sending"))
                try { File.Move(file, Path.ChangeExtension(file, ".ready"), overwrite: false); }
                catch (IOException) { }
    }

    private string KindDir(SpoolBatchKind kind) =>
        kind == SpoolBatchKind.Ingest ? _ingestDir : _statsDir;

    private static ActiveBatch OpenNewBatch(string dir)
    {
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}.writing");
        return new ActiveBatch(path);
    }

    private static void SealBatch(ref ActiveBatch? batch, string dir)
    {
        if (batch is null) return;
        batch.Seal();
        var readyPath = Path.ChangeExtension(batch.FilePath, ".ready");
        File.Move(batch.FilePath, readyPath);
        batch = null;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            // Seal any in-progress batches so data written up to this point is not lost.
            if (_ingestBatch?.RecordCount > 0) SealBatch(ref _ingestBatch, _ingestDir);
            else _ingestBatch?.Seal();

            if (_statsBatch?.RecordCount > 0) SealBatch(ref _statsBatch, _statsDir);
            else _statsBatch?.Seal();
        }
    }

    // ── Nested helper ────────────────────────────────────────────────────────

    private sealed class ActiveBatch : IDisposable
    {
        private readonly StreamWriter _writer;
        private long _bytesWritten;

        public string FilePath    { get; }
        public int    RecordCount { get; private set; }

        public ActiveBatch(string filePath)
        {
            FilePath = filePath;
            _writer  = new StreamWriter(
                new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                Encoding.UTF8,
                bufferSize: 4096,
                leaveOpen: false);
        }

        public void WriteLine(string json)
        {
            _writer.WriteLine(json);
            _bytesWritten += Encoding.UTF8.GetByteCount(json) + 1;
            RecordCount++;
        }

        public bool ShouldSeal(int maxRecords, long maxBytes) =>
            RecordCount >= maxRecords || _bytesWritten >= maxBytes;

        public void Seal()
        {
            _writer.Flush();
            _writer.Dispose();
        }

        public void Dispose() => Seal();
    }
}
