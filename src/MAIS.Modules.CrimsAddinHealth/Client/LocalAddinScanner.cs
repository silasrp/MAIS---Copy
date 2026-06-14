using System.Diagnostics;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;

namespace MAIS.Modules.CrimsAddinHealth.Client;

/// <summary>
/// Compares locally installed CRIMS addin DLLs against the server manifest.
/// Reports version mismatches and missing files.
/// </summary>
public sealed class LocalAddinScanner
{
    private readonly CrimsAddinHealthOptions        _options;
    private readonly ILogger<LocalAddinScanner>     _logger;

    public LocalAddinScanner(IOptions<CrimsAddinHealthOptions> options, ILogger<LocalAddinScanner> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public Task<ScanResult> ScanAsync(AddinManifest serverManifest, string clientId, string machineRole, CancellationToken ct)
    {
        var addinsPath = _options.CrimsAddinsFolder;
        var mismatches = new List<DllMismatch>();

        _logger.LogInformation("Scanning {Path} against manifest ({Count} entries)",
            addinsPath, serverManifest.Entries.Count);

        foreach (var entry in serverManifest.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var localPath = Path.Combine(addinsPath, entry.FileName);

            if (!File.Exists(localPath))
            {
                mismatches.Add(new DllMismatch
                {
                    FileName         = entry.FileName,
                    InstalledVersion = "N/A",
                    ExpectedVersion  = entry.Version,
                    IsMissing        = true
                });
                continue;
            }

            var localVersion = FileVersionInfo.GetVersionInfo(localPath).FileVersion ?? "0.0.0.0";
            if (!string.Equals(localVersion, entry.Version, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(new DllMismatch
                {
                    FileName         = entry.FileName,
                    InstalledVersion = localVersion,
                    ExpectedVersion  = entry.Version
                });
            }
        }

        _logger.LogInformation("Scan complete: {Mismatches} mismatch(es) found", mismatches.Count);

        return Task.FromResult(new ScanResult
        {
            ClientId    = clientId,
            MachineName = Environment.MachineName,
            AssetTag    = _options.AssetTag,
            CrimsUserId = _options.CrimsUserId,
            MachineRole = machineRole,
            ScannedAt   = DateTimeOffset.UtcNow,
            Mismatches  = mismatches.AsReadOnly()
        });
    }
}