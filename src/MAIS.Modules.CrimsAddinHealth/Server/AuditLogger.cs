using System.Text.Json;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.CrimsAddinHealth.Server;

/// <summary>
/// Writes JSON audit records to rolling daily files under AuditFolderPath.
/// Thread-safe via a SemaphoreSlim(1) gate.
/// </summary>
public sealed class AuditLogger
{
    private readonly CrimsAddinHealthOptions _options;
    private readonly ILogger<AuditLogger>   _logger;
    private readonly SemaphoreSlim          _gate = new(1, 1);

    private static readonly JsonSerializerOptions _json =
        new() { WriteIndented = false };

    public AuditLogger(IOptions<CrimsAddinHealthOptions> options, ILogger<AuditLogger> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task WriteAsync(AuditRecord record, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now  = DateTimeOffset.UtcNow;
            var dir  = Path.Combine(_options.AuditFolderPath, now.ToString("yyyy-MM"));
            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, $"addin-health-audit-{now:yyyy-MM-dd}.json");
            var line = JsonSerializer.Serialize(record, _json);

            await File.AppendAllTextAsync(file, line + Environment.NewLine, ct);

            _logger.LogInformation("Audit record written for {Machine} (AuditId={AuditId})",
                record.MachineName, record.AuditId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit record {AuditId}", record.AuditId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditRecord>> GetRecentAsync(int days, CancellationToken ct = default)
    {
        var results  = new List<AuditRecord>();
        var cutoff   = DateTimeOffset.UtcNow.AddDays(-days);
        var rootDir  = _options.AuditFolderPath;

        if (!Directory.Exists(rootDir)) return results;

        foreach (var file in Directory.EnumerateFiles(rootDir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                foreach (var line in await File.ReadAllLinesAsync(file, ct))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var record = JsonSerializer.Deserialize<AuditRecord>(line, _json);
                    if (record is not null && record.UpdateCompletedAt >= cutoff)
                        results.Add(record);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read audit file {File}", file);
            }
        }

        return results.OrderByDescending(r => r.UpdateCompletedAt).ToList().AsReadOnly();
    }
}