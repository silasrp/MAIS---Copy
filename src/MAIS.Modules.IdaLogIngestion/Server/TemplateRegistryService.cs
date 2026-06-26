using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// Server-side owner of the canonical template registry. Persists approved classifications
/// to disk, deduplicates novel-template reports arriving from up to 800 clients, and
/// serves versioned snapshots on demand.
///
/// The disk file at {TemplateRegistryPath}\{appId}.json — not any model weights — is
/// the actual long-term memory of what is a known log pattern.
/// </summary>
public sealed class TemplateRegistryService
{
    private readonly IdaLogIngestionOptions          _options;
    private readonly ILogger<TemplateRegistryService> _logger;
    private readonly SemaphoreSlim                   _gate = new(1, 1);

    private static readonly JsonSerializerOptions _json =
        new() { WriteIndented = true };

    // appId → versioned snapshot (lazy-loaded from disk on first access per app)
    private readonly Dictionary<string, TemplateRegistrySnapshot> _snapshots = new();

    // appId → (templateId → pending review item) — in-memory only; AI enrichment added in Phase 6
    private readonly Dictionary<string, Dictionary<string, NovelTemplateReviewItem>> _pendingByApp = new();

    public TemplateRegistryService(
        IOptions<IdaLogIngestionOptions> options,
        ILogger<TemplateRegistryService> logger)
    {
        _options = options.Value;
        _logger  = logger;

        Directory.CreateDirectory(_options.TemplateRegistryPath);
        Directory.CreateDirectory(_options.PendingReviewPath);
    }

    public async Task<TemplateRegistrySnapshot> GetRegistryAsync(string appId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_snapshots.TryGetValue(appId, out var snapshot))
            {
                snapshot = await LoadFromDiskAsync(appId, ct);
                _snapshots[appId] = snapshot;
            }
            return snapshot;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Called when a client reports a template shape it has not seen in its local cache.
    /// Deduplicates: if this shape has already been reported by another client, the existing
    /// pending item is returned with its seen-on-machine count incremented.
    /// </summary>
    public async Task<NovelTemplateReviewItem> ReportNovelAsync(NovelTemplateCandidate candidate, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Confirm the template is genuinely absent from the canonical registry before
            // creating a review item; a slow client cache refresh could cause false reports.
            if (!_snapshots.TryGetValue(candidate.AppId, out var snapshot))
                snapshot = await LoadFromDiskAsync(candidate.AppId, ct);

            if (snapshot.Entries.Any(e => e.TemplateId == candidate.TemplateId))
            {
                _logger.LogDebug("Novel report for {Id} ignored — already in registry for {App}",
                    candidate.TemplateId, candidate.AppId);
                var known = snapshot.Entries.First(e => e.TemplateId == candidate.TemplateId);
                return new NovelTemplateReviewItem
                {
                    TemplateId         = known.TemplateId,
                    AppId              = known.AppId,
                    TokenPattern       = known.TokenPattern,
                    SampleMessages     = candidate.SampleMessages,
                    SeenOnMachineCount = 1,
                    FirstSeenAt        = known.ApprovedAt
                };
            }

            if (!_pendingByApp.TryGetValue(candidate.AppId, out var pending))
            {
                pending = new Dictionary<string, NovelTemplateReviewItem>();
                _pendingByApp[candidate.AppId] = pending;
            }

            if (pending.TryGetValue(candidate.TemplateId, out var existing))
            {
                existing.SeenOnMachineCount++;
                _logger.LogDebug("Novel report for {Id} merged; seen on {N} machines",
                    candidate.TemplateId, existing.SeenOnMachineCount);
                return existing;
            }

            var item = new NovelTemplateReviewItem
            {
                TemplateId          = candidate.TemplateId,
                AppId               = candidate.AppId,
                TokenPattern        = candidate.TokenPattern,
                SampleMessages      = candidate.SampleMessages,
                SeenOnMachineCount  = 1,
                FirstSeenAt         = DateTimeOffset.UtcNow
            };

            pending[candidate.TemplateId] = item;
            _logger.LogInformation("Novel template {Id} registered for review ({App})",
                candidate.TemplateId, candidate.AppId);
            return item;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Commits a reviewed classification into the canonical registry and bumps its version.
    /// The templateId alone is sufficient — it is globally unique (SHA-256 of the token pattern).
    /// </summary>
    public async Task ApproveAsync(string templateId, ClassificationAction action, string approvedBy, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Locate which app this template belongs to via pending reviews.
            string? appId = null;
            NovelTemplateReviewItem? reviewItem = null;

            foreach (var (aid, pending) in _pendingByApp)
            {
                if (pending.TryGetValue(templateId, out reviewItem))
                {
                    appId = aid;
                    break;
                }
            }

            if (appId is null)
            {
                _logger.LogWarning("Approve called for unknown templateId {Id}", templateId);
                return;
            }

            if (!_snapshots.TryGetValue(appId, out var snapshot))
                snapshot = await LoadFromDiskAsync(appId, ct);

            var newEntry = new TemplateRegistryEntry
            {
                TemplateId               = templateId,
                AppId                    = appId,
                TokenPattern             = reviewItem!.TokenPattern,
                Classification           = action,
                HumanReadableDescription = reviewItem.AiHumanReadableDescription,
                ExtractionFields         = reviewItem.AiSuggestedExtractionFields,
                ApprovedBy               = approvedBy,
                ApprovedAt               = DateTimeOffset.UtcNow
            };

            var entries = snapshot.Entries
                .Where(e => e.TemplateId != templateId)
                .Append(newEntry)
                .ToList()
                .AsReadOnly();

            var updated = new TemplateRegistrySnapshot
            {
                AppId   = appId,
                Version = snapshot.Version + 1,
                Entries = entries
            };

            _snapshots[appId] = updated;
            _pendingByApp[appId].Remove(templateId);

            await PersistToDiskAsync(updated, ct);

            _logger.LogInformation("Template {Id} approved as {Action} for {App} by {User}",
                templateId, action, appId, approvedBy);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<NovelTemplateReviewItem>> GetPendingReviewsAsync(string appId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _pendingByApp.TryGetValue(appId, out var pending)
                ? pending.Values.ToList().AsReadOnly()
                : (IReadOnlyList<NovelTemplateReviewItem>)[];
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Replaces a pending review item with an enriched version carrying AI-generated fields.
    /// Called by NovelTemplateReviewService after IdaReviewAgent returns a result.
    /// No-ops silently if the template has already been approved or removed.
    /// </summary>
    public async Task EnrichPendingAsync(
        string templateId,
        string description,
        ClassificationAction? suggestedClassification,
        string rationale,
        string[] extractionFields,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var (_, pending) in _pendingByApp)
            {
                if (!pending.TryGetValue(templateId, out var existing)) continue;

                pending[templateId] = new NovelTemplateReviewItem
                {
                    TemplateId                   = existing.TemplateId,
                    AppId                        = existing.AppId,
                    TokenPattern                 = existing.TokenPattern,
                    SampleMessages               = existing.SampleMessages,
                    SeenOnMachineCount           = existing.SeenOnMachineCount,
                    FirstSeenAt                  = existing.FirstSeenAt,
                    AiHumanReadableDescription   = description,
                    AiSuggestedClassification    = suggestedClassification,
                    AiRationale                  = rationale,
                    AiSuggestedExtractionFields  = extractionFields
                };
                return;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<int> GetPendingReviewCountAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return _pendingByApp.Values.Sum(d => d.Count); }
        finally { _gate.Release(); }
    }

    private async Task<TemplateRegistrySnapshot> LoadFromDiskAsync(string appId, CancellationToken ct)
    {
        var path = Path.Combine(_options.TemplateRegistryPath, $"{appId}.json");
        if (!File.Exists(path))
            return new TemplateRegistrySnapshot { AppId = appId, Version = 0, Entries = [] };

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<TemplateRegistrySnapshot>(json, _json)
                ?? new TemplateRegistrySnapshot { AppId = appId, Version = 0, Entries = [] };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load template registry for {App}; starting empty", appId);
            return new TemplateRegistrySnapshot { AppId = appId, Version = 0, Entries = [] };
        }
    }

    private async Task PersistToDiskAsync(TemplateRegistrySnapshot snapshot, CancellationToken ct)
    {
        var path     = Path.Combine(_options.TemplateRegistryPath, $"{snapshot.AppId}.json");
        var tempPath = path + ".tmp";
        var json     = JsonSerializer.Serialize(snapshot, _json);

        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, path, overwrite: true);
    }

    public async Task<IReadOnlyList<string>> GetPendingAppIdsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return _pendingByApp.Keys.ToList().AsReadOnly(); }
        finally { _gate.Release(); }
    }

}
