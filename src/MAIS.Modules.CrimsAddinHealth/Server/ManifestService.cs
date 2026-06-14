using System.Security.Cryptography;
using System.Diagnostics;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;

namespace MAIS.Modules.CrimsAddinHealth.Server;

/// <summary>
/// Builds and caches the addin manifest from the server DLL repository folder.
/// Watches the folder for changes and rebuilds automatically.
/// </summary>
public sealed class ManifestService : IDisposable
{
    private readonly CrimsAddinHealthOptions _options;
    private readonly ILogger<ManifestService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private AddinManifest _cached = new();

    public ManifestService(IOptions<CrimsAddinHealthOptions> options, ILogger<ManifestService> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public AddinManifest GetManifest() => _cached;

    public async Task InitialiseAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        StartWatcher();
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var folder = _options.RepositoryFolderPath;
            if (!Directory.Exists(folder))
            {
                _logger.LogWarning("Addin repository folder not found: {Path}", folder);
                return;
            }

            var sw = Stopwatch.StartNew();
            var entries = new List<AddinManifestEntry>();

            foreach (var file in Directory.EnumerateFiles(folder, "*.dll"))
            {
                try
                {
                    var info    = new FileInfo(file);
                    var version = FileVersionInfo.GetVersionInfo(file).FileVersion ?? "0.0.0.0";
                    var sha256  = await ComputeSha256Async(file, ct);

                    entries.Add(new AddinManifestEntry
                    {
                        FileName      = info.Name,
                        Version       = version,
                        FileSizeBytes = info.Length,
                        Sha256        = sha256,
                        LastModified  = info.LastWriteTimeUtc
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read DLL info for {File}", file);
                }
            }

            _cached = new AddinManifest
            {
                GeneratedAt    = DateTimeOffset.UtcNow,
                RepositoryPath = folder,
                Entries        = entries.AsReadOnly()
            };

            _logger.LogInformation("Manifest refreshed: {Count} DLLs in {Elapsed}ms",
                entries.Count, sw.ElapsedMilliseconds);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void StartWatcher()
    {
        var folder = _options.RepositoryFolderPath;
        if (!Directory.Exists(folder)) return;

        _watcher = new FileSystemWatcher(folder, "*.dll")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnRepositoryChanged;
        _watcher.Created += OnRepositoryChanged;
        _watcher.Deleted += OnRepositoryChanged;
        _watcher.Renamed += OnRepositoryChanged;
    }

    private void OnRepositoryChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("Addin repository changed ({Type}): {File} — refreshing manifest", e.ChangeType, e.Name);
        _ = Task.Run(() => RefreshAsync(CancellationToken.None));
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _lock.Dispose();
    }
}