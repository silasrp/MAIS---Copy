using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Core.Models;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// IHostedService that owns the full client-side log ingestion pipeline.
/// Creates one sub-pipeline per LogSourceDefinition pulled from options and
/// runs them all concurrently for the lifetime of the service.
/// </summary>
public sealed class IdaClientWorker : BackgroundService
{
    private readonly IdaLogIngestionOptions    _options;
    private readonly HttpClient                _http;
    private readonly ILoggerFactory            _loggerFactory;
    private readonly ILogger<IdaClientWorker>  _logger;

    public IdaClientWorker(
        IOptions<IdaLogIngestionOptions> options,
        HttpClient http,
        ILoggerFactory loggerFactory,
        ILogger<IdaClientWorker> logger)
    {
        _options       = options.Value;
        _http          = http;
        _loggerFactory = loggerFactory;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Sources.Count == 0)
        {
            _logger.LogWarning("IdaClientWorker: no log sources configured");
            return;
        }

        _logger.LogInformation("IdaClientWorker starting {Count} source pipeline(s)", _options.Sources.Count);

        var pipelines = _options.Sources
            .Select(source => RunSourcePipelineAsync(source, stoppingToken));

        await Task.WhenAll(pipelines);
    }

    private async Task RunSourcePipelineAsync(LogSourceDefinition source, CancellationToken ct)
    {
        var assetTag    = string.IsNullOrEmpty(_options.AssetTag) ? Environment.MachineName : _options.AssetTag;
        var machineName = Environment.MachineName;

        var registryCachePath = Path.Combine(
            _options.LocalRegistryCachePath, $"{source.AppId}.json");

        var registryCache = new TemplateRegistryCache(
            _http, source.AppId, registryCachePath,
            _loggerFactory.CreateLogger<TemplateRegistryCache>());

        var spool    = new FlatFileSpoolQueue(_options.SpoolPath, source);
        var miner    = new LogTemplateMiner(source, registryCache);
        var engine   = new ClassificationEngine(registryCache);
        var agg      = new StatsAggregator(source, assetTag);
        var parser   = new LogLineParser();
        var assembler = new MultilineAssembler(source, machineName, assetTag);
        var watcher  = new LogFileWatcher(
            source.LogFolderPath, source.ActiveFileName, source.RotationBackstopPollSeconds);
        var uploader = new SmartUploader(
            _http, spool, source, _loggerFactory.CreateLogger<SmartUploader>());

        // Templates reported to the server this session — avoids flooding on repeated novel hits.
        var reportedTemplates = new HashSet<string>();

        assembler.EntryAssembled += rawEntry =>
        {
            var entry = parser.Parse(rawEntry);
            if (!entry.IsParsed) return;

            var match    = miner.Process(entry.Message);
            var decision = engine.Classify(entry, match);

            switch (decision.Action)
            {
                case ClassificationAction.Ingest:
                    spool.EnqueueIngest(BuildLogRecord(entry, match, source, assetTag));
                    break;
                case ClassificationAction.StatsOnly:
                    agg.Record(match.TemplateId);
                    break;
            }

            if (decision.IsNovel && reportedTemplates.Add(match.TemplateId))
                _ = ReportNovelAsync(match, entry, source, ct);
        };

        watcher.LinesAvailable += lines  => assembler.FeedLines(lines);
        watcher.Rotated        += ()     => assembler.SignalRotation();

        try
        {
            await registryCache.RefreshAsync(ct);

            await Task.WhenAll(
                uploader.RunAsync(ct),
                RunRegistryRefreshLoopAsync(registryCache, source, ct),
                RunStatsFlushLoopAsync(agg, spool, source, ct));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source pipeline for {App} faulted", source.AppId);
        }
        finally
        {
            watcher.Dispose();
            assembler.Dispose();
            spool.Dispose();
        }
    }

    private static LogRecord BuildLogRecord(
        ParsedLogEntry entry, TemplateMatch match,
        LogSourceDefinition source, string assetTag) => new()
    {
        IdempotencyKey      = $"{assetTag}-{entry.Timestamp:yyyyMMddHHmmss}-{match.TemplateId}-{Guid.NewGuid():N}",
        AppId               = source.AppId,
        SourceApplication   = source.DisplayName,
        CompatibilityAppName = source.CompatibilityAppName,
        MachineName         = entry.MachineName,
        AssetTag            = assetTag,
        Timestamp           = entry.Timestamp,
        Level               = entry.Level,
        Message             = entry.Message,
        TemplateId          = match.TemplateId,
        Classification      = ClassificationAction.Ingest.ToString()
    };

    private async Task ReportNovelAsync(
        TemplateMatch match, ParsedLogEntry entry,
        LogSourceDefinition source, CancellationToken ct)
    {
        try
        {
            var candidate = new NovelTemplateCandidate
            {
                AppId          = source.AppId,
                TemplateId     = match.TemplateId,
                TokenPattern   = [],
                SampleMessages = [entry.Message],
                MachineName    = entry.MachineName
            };

            await _http.PostAsJsonAsync("/api/v1/ida/templates/novel", candidate, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Novel template report failed for {Id}", match.TemplateId);
        }
    }

    private static async Task RunRegistryRefreshLoopAsync(
        TemplateRegistryCache cache, LogSourceDefinition source, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(source.RegistryRefreshIntervalSeconds));
        while (await timer.WaitForNextTickAsync(ct))
            await cache.RefreshAsync(ct);
    }

    private static async Task RunStatsFlushLoopAsync(
        StatsAggregator aggregator, FlatFileSpoolQueue spool,
        LogSourceDefinition source, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(source.StatsBucketSeconds));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var closed = aggregator.DrainClosedBuckets();
            foreach (var agg in closed)
                spool.EnqueueStats(agg);
            if (closed.Count > 0)
                spool.FlushStats();
        }
    }
}
