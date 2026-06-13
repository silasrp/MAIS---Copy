using System.Collections.Concurrent;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsAddinHealth.Server;

/// <summary>
/// Thread-safe FIFO update queue with support for deferred (scheduled) entries.
/// Scheduled entries are promoted to the waiting queue once their ScheduledFor time arrives.
/// </summary>
public sealed class UpdateQueue : IDisposable
{
    private readonly ConcurrentQueue<QueueEntry> _waiting = new();
    private readonly List<QueueEntry> _scheduled = [];
    private readonly Lock _scheduleLock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ILogger<UpdateQueue> _logger;
    private readonly Timer _promotionTimer;

    public UpdateQueue(ILogger<UpdateQueue> logger)
    {
        _logger = logger;
        _promotionTimer = new Timer(PromoteScheduledEntries, null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Enqueue(QueueEntry entry)
    {
        entry.Status = QueueEntryStatus.Waiting;
        _waiting.Enqueue(entry);
        _signal.Release();
        _logger.LogInformation("Enqueued update for {Machine} (QueueId={QueueId})",
            entry.Request.ScanResult.MachineName, entry.QueueId);
    }

    public void Schedule(QueueEntry entry)
    {
        entry.Status = QueueEntryStatus.Scheduled;
        lock (_scheduleLock)
            _scheduled.Add(entry);

        _logger.LogInformation("Scheduled update for {Machine} at {Time} (QueueId={QueueId})",
            entry.Request.ScanResult.MachineName, entry.ScheduledFor, entry.QueueId);
    }

    public IReadOnlyList<QueueEntry> GetPending()
    {
        return _waiting.ToArray();
    }

    public IReadOnlyList<QueueEntry> GetScheduled()
    {
        lock (_scheduleLock)
            return _scheduled.ToList().AsReadOnly();
    }

    public bool CancelScheduled(string queueId)
    {
        lock (_scheduleLock)
        {
            var entry = _scheduled.FirstOrDefault(e => e.QueueId == queueId);
            if (entry is null) return false;
            _scheduled.Remove(entry);
            return true;
        }
    }

    public async Task<QueueEntry?> DequeueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _signal.WaitAsync(ct);
            if (_waiting.TryDequeue(out var entry))
            {
                entry.Status    = QueueEntryStatus.InProgress;
                entry.StartedAt = DateTimeOffset.UtcNow;
                return entry;
            }
        }

        return null;
    }

    public void Complete(string queueId, bool success, string? failureReason = null)
    {
        _logger.LogInformation("Queue entry {QueueId} completed. Success={Success}", queueId, success);
    }

    private void PromoteScheduledEntries(object? state)
    {
        List<QueueEntry> toPromote;
        lock (_scheduleLock)
        {
            toPromote = _scheduled
                .Where(e => e.ScheduledFor <= DateTimeOffset.UtcNow)
                .ToList();

            foreach (var entry in toPromote)
                _scheduled.Remove(entry);
        }

        foreach (var entry in toPromote)
        {
            _logger.LogInformation("Promoting scheduled entry {QueueId} to waiting queue", entry.QueueId);
            Enqueue(entry);
        }
    }

    public void Dispose()
    {
        _promotionTimer.Dispose();
        _signal.Dispose();
    }
}