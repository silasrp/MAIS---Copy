using System.Text.RegularExpressions;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// Joins continuation lines into complete log entries. Holds back the in-progress
/// entry until one of three conditions confirms it is complete:
///   1. A new entry-start line arrives (the previous entry is flushed first).
///   2. SignalRotation() is called by the watcher on file rename.
///   3. No new lines arrive within MultilineMaxWaitSeconds (inactivity timer fires).
/// </summary>
public sealed class MultilineAssembler : IDisposable
{
    private readonly Regex _startPattern;
    private readonly string _appId;
    private readonly string _machineName;
    private readonly string _assetTag;
    private readonly int _maxWaitMs;
    private readonly object _syncRoot = new();

    private string? _pendingFirstLine;
    private readonly List<string> _pendingAdditional = [];
    private Timer? _flushTimer;

    public event Action<RawLogEntry>? EntryAssembled;

    public MultilineAssembler(LogSourceDefinition source, string machineName, string assetTag)
    {
        _startPattern = new Regex(source.MultilineStartPattern, RegexOptions.Compiled);
        _appId        = source.AppId;
        _machineName  = machineName;
        _assetTag     = assetTag;
        _maxWaitMs    = source.MultilineMaxWaitSeconds * 1_000;
    }

    public void FeedLines(IReadOnlyList<string> lines)
    {
        lock (_syncRoot)
        {
            foreach (var line in lines)
            {
                if (_startPattern.IsMatch(line))
                {
                    FlushPendingLocked();
                    _pendingFirstLine = line;
                    _pendingAdditional.Clear();
                }
                else if (_pendingFirstLine is not null)
                {
                    _pendingAdditional.Add(line);
                }
                // Lines that arrive before the first entry-start are silently discarded.
            }

            ResetFlushTimerLocked();
        }
    }

    /// <summary>
    /// Called by LogFileWatcher.Rotated. Flushes any in-progress entry immediately
    /// rather than waiting for the inactivity timer, since the file is gone.
    /// </summary>
    public void SignalRotation()
    {
        lock (_syncRoot)
        {
            _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            FlushPendingLocked();
        }
    }

    private void FlushPendingLocked()
    {
        if (_pendingFirstLine is null) return;

        EntryAssembled?.Invoke(new RawLogEntry
        {
            AppId           = _appId,
            MachineName     = _machineName,
            AssetTag        = _assetTag,
            FirstLine       = _pendingFirstLine,
            AdditionalLines = [.. _pendingAdditional],
            ReceivedAt      = DateTimeOffset.UtcNow
        });

        _pendingFirstLine = null;
        _pendingAdditional.Clear();
    }

    private void ResetFlushTimerLocked()
    {
        if (_pendingFirstLine is null) return;

        // Lazy-create the timer on first use; subsequent calls just reschedule it.
        _flushTimer ??= new Timer(_ =>
        {
            lock (_syncRoot)
                FlushPendingLocked();
        });

        _flushTimer.Change(_maxWaitMs, Timeout.Infinite);
    }

    public void Dispose() => _flushTimer?.Dispose();
}
