using System;
using System.Collections.Generic;
using System.Linq;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// Thread-safe accumulator for confirmed-write counts from IndexerWorker.
/// Buckets are minute-granularity. Closed buckets are moved to a rolling history window
/// when drained; the history feeds the sidebar backfill REST response.
/// </summary>
public sealed class IngestionRateTracker
{
    private const int BucketSeconds     = 60;
    private const int MaxStoredBuckets  = 60; // 1 hour

    private readonly object                             _syncRoot = new();
    private readonly Dictionary<DateTimeOffset, int>   _live     = new();
    private readonly List<IngestionRateBucket>          _history  = new();

    /// <summary>Adds count records to the current open bucket.</summary>
    public void Record(int count)
    {
        var bucket = CurrentBucket();
        lock (_syncRoot)
            _live[bucket] = _live.GetValueOrDefault(bucket) + count;
    }

    /// <summary>
    /// Returns buckets whose time window has fully elapsed and removes them from the live map.
    /// Also appends them to the rolling history so GetRecentBuckets can serve backfill.
    /// Called by IndexerWorker on its rate-publish timer.
    /// </summary>
    public IReadOnlyList<IngestionRateBucket> DrainClosedBuckets()
    {
        var cutoff = CurrentBucket();
        lock (_syncRoot)
        {
            var closed = _live
                .Where(kv => kv.Key < cutoff)
                .Select(kv => new IngestionRateBucket { BucketStart = kv.Key, Count = kv.Value })
                .OrderBy(b => b.BucketStart)
                .ToList();

            foreach (var b in closed)
                _live.Remove(b.BucketStart);

            _history.AddRange(closed);
            while (_history.Count > MaxStoredBuckets)
                _history.RemoveAt(0);

            return closed.AsReadOnly();
        }
    }

    /// <summary>Returns the most recent stored buckets for sidebar backfill, oldest first.</summary>
    public IReadOnlyList<IngestionRateBucket> GetRecentBuckets()
    {
        lock (_syncRoot)
            return _history.ToList().AsReadOnly();
    }

    private static DateTimeOffset CurrentBucket()
    {
        var epochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(epochSeconds - epochSeconds % BucketSeconds);
    }
}
