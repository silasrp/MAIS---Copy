using System.IO;
using System.Text;

namespace MAIS.Modules.IdaLogIngestion.Client;

public sealed class LogFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _folderPath;
    private readonly string _activeFileName;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _syncRoot = new();
    private readonly int _backstopPollSeconds;
    private long _lastOffset;

    /// <summary>Fired with one or more complete lines read from the active log file.</summary>
    public event Action<IReadOnlyList<string>>? LinesAvailable;

    /// <summary>
    /// Fired after the active file is renamed away (rotation), after any remaining
    /// bytes have been drained. Subscribers should flush any in-progress multiline entry.
    /// </summary>
    public event Action? Rotated;

    public LogFileWatcher(string folderPath, string activeFileName, int backstopPollSeconds = 10)
    {
        _folderPath          = folderPath;
        _activeFileName      = activeFileName;
        _backstopPollSeconds = backstopPollSeconds;

        _watcher = new FileSystemWatcher(folderPath)
        {
            Filter        = "*.log",
            NotifyFilter  = NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Created += OnCreated;

        // Start at the end of any pre-existing active file so we don't re-ingest
        // history on service startup.
        var activePath = Path.Combine(folderPath, activeFileName);
        if (File.Exists(activePath))
            _lastOffset = new FileInfo(activePath).Length;

        _ = StartBackstopPollAsync();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        // The OS hands us exactly where the rotated file went. No file
        // identity computation needed, no directory scan, no assumption
        // that the old file was already fully read.
        if (!IsActiveFile(e.OldFullPath)) return;

        lock (_syncRoot)
        {
            EmitLines(e.OldFullPath, _lastOffset);
            _lastOffset = 0;
        }

        Rotated?.Invoke();
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (IsActiveFile(e.FullPath))
        {
            lock (_syncRoot)
                _lastOffset = 0;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (IsActiveFile(e.FullPath))
            ReadNewBytes(e.FullPath);
    }

    private bool IsActiveFile(string path) =>
        Path.GetFileName(path).Equals(_activeFileName, StringComparison.OrdinalIgnoreCase);

    private void ReadNewBytes(string path)
    {
        lock (_syncRoot)
            EmitLines(path, _lastOffset);
    }

    // Must be called while _syncRoot is held.
    private void EmitLines(string path, long fromOffset)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (fromOffset >= fs.Length) return;
            fs.Seek(fromOffset, SeekOrigin.Begin);

            using var reader = new StreamReader(fs, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var content = reader.ReadToEnd();
            if (content.Length == 0) return;

            // Only consume up to and including the last complete line. Any bytes
            // after the final '\n' are a partial line still being written — leave
            // them for the next read by not advancing the offset past them.
            var lastNewline = content.LastIndexOf('\n');
            if (lastNewline < 0) return;

            var consumed = content[..(lastNewline + 1)];
            _lastOffset = fromOffset + Encoding.UTF8.GetByteCount(consumed);

            var lines = consumed
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToArray();

            if (lines.Length > 0)
                LinesAvailable?.Invoke(lines);
        }
        catch (FileNotFoundException) { }
        catch (IOException) { }
    }

    private async Task StartBackstopPollAsync()
    {
        // Backstop only: FileSystemWatcher can occasionally drop events if its internal
        // buffer overflows under extreme burst volume. A coarse periodic size check
        // catches that rare case without taking on any of the complexity the rename-based
        // rotation handling otherwise avoids.
        var path = Path.Combine(_folderPath, _activeFileName);
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_backstopPollSeconds), _cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }

            if (!File.Exists(path)) continue;
            try { ReadNewBytes(path); }
            catch (IOException) { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watcher.Dispose();
        _cts.Dispose();
    }
}
