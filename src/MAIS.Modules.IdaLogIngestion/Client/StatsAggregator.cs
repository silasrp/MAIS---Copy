using System.Collections.Generic;
using System.Linq;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// Accumulates per-template hit counts in configurable time buckets. Callers record a
/// hit per StatsOnly-classified entry; SmartUploader drains closed (fully elapsed) buckets
/// on its upload interval and turns them into StatsAggregate records for the server.
/// </summary>
public sealed class StatsAggregator
{
    private readonly int    _bucketSeconds;
    private readonly string _appId;
    private readonly string _assetTag;
    private readonly object _syncRoot = new();

    private readonly Dictionary<(string templateId, DateTimeOffset bucketStart), int> _counts = new();

    public StatsAggregator(LogSourceDefinition source, string assetTag)
    {
        _bucketSeconds = source.StatsBucketSeconds;
        _appId         = source.AppId;
        _assetTag      = assetTag;
    }

    public void Record(string templateId)
    {
        var bucket = CurrentBucket();
        lock (_syncRoot)
        {
            var key = (templateId, bucket);
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>
    /// Returns and removes all buckets whose time window has fully elapsed.
    /// Called by SmartUploader on its upload interval.
    /// </summary>
    public IReadOnlyList<StatsAggregate> DrainClosedBuckets()
    {
        var cutoff = CurrentBucket();
        lock (_syncRoot)
        {
            var closed = _counts
                .Where(kv => kv.Key.bucketStart < cutoff)
                .ToList();

            foreach (var kv in closed)
                _counts.Remove(kv.Key);

            return closed
                .Select(kv => new StatsAggregate
                {
                    IdempotencyKey = $"{_appId}-{kv.Key.templateId}-{kv.Key.bucketStart:yyyyMMddHHmmss}",
                    AppId          = _appId,
                    TemplateId     = kv.Key.templateId,
                    BucketStart    = kv.Key.bucketStart,
                    Count          = kv.Value,
                    AssetTag       = _assetTag
                })
                .ToList()
                .AsReadOnly();
        }
    }

    private DateTimeOffset CurrentBucket()
    {
        var epochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bucketStart  = epochSeconds - (epochSeconds % _bucketSeconds);
        return DateTimeOffset.FromUnixTimeSeconds(bucketStart);
    }
}
