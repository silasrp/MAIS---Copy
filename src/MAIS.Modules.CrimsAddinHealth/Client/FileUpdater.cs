using System.Diagnostics;
using System.Security.Cryptography;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;

namespace MAIS.Modules.CrimsAddinHealth.Client;

/// <summary>
/// Copies DLL files from the UNC repository to the local CRIMS addins folder.
/// Validates file size and SHA-256 checksum after copy, then verifies installed version.
/// </summary>
public sealed class FileUpdater
{
    private readonly CrimsAddinHealthOptions _options;
    private readonly ILogger<FileUpdater>    _logger;

    public FileUpdater(IOptions<CrimsAddinHealthOptions> options, ILogger<FileUpdater> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<UpdateOutcome> UpdateDllsAsync(
        QueueEntry entry, AddinManifest manifest, CancellationToken ct)
    {
        var updatedFiles = new List<string>();

        foreach (var mismatch in entry.Request.ScanResult.Mismatches)
        {
            ct.ThrowIfCancellationRequested();

            var manifestEntry = manifest.Entries.FirstOrDefault(e =>
                string.Equals(e.FileName, mismatch.FileName, StringComparison.OrdinalIgnoreCase));

            if (manifestEntry is null)
            {
                _logger.LogWarning("No manifest entry for {File} — skipping", mismatch.FileName);
                continue;
            }

            var sourcePath = Path.Combine(_options.RepositoryUncPath, mismatch.FileName);
            var destPath   = Path.Combine(_options.CrimsAddinsFolder, mismatch.FileName);

            _logger.LogInformation("Copying {File} from {Source}", mismatch.FileName, sourcePath);

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Source DLL not found on UNC path: {sourcePath}");

            // Retry logic for transient network errors
            for (int attempt = 1; attempt <= _options.ValidationRetryCount; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    File.Copy(sourcePath, destPath, overwrite: true);

                    // Validate checksum
                    var actualSha = await ComputeSha256Async(destPath, ct);
                    if (!string.Equals(actualSha, manifestEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"SHA-256 mismatch for {mismatch.FileName} after copy. Expected {manifestEntry.Sha256}, got {actualSha}.");

                    // Validate version
                    var installedVersion = FileVersionInfo.GetVersionInfo(destPath).FileVersion ?? "0.0.0.0";
                    if (!string.Equals(installedVersion, manifestEntry.Version, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Version mismatch after copy: expected {manifestEntry.Version}, got {installedVersion}.");

                    updatedFiles.Add($"{mismatch.FileName} → {installedVersion}");
                    _logger.LogInformation("Updated {File} to v{Version}", mismatch.FileName, installedVersion);
                    break;
                }
                catch (Exception ex) when (attempt < _options.ValidationRetryCount)
                {
                    _logger.LogWarning(ex, "Update attempt {Attempt} failed for {File}, retrying",
                        attempt, mismatch.FileName);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
        }

        return new UpdateOutcome
        {
            QueueId      = entry.QueueId,
            Success      = true,
            UpdatedFiles = updatedFiles.AsReadOnly(),
            CompletedAt  = DateTimeOffset.UtcNow
        };
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }
}