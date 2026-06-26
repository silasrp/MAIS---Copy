using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// BackgroundService that periodically enriches pending novel-template review items
/// with AI-generated descriptions and classification suggestions via IdaReviewAgent.
///
/// Flow:
///   1. For each configured source app, fetch pending review items.
///   2. Skip items that already carry an AI description (already enriched).
///   3. Call IdaReviewAgent for each remaining item — one at a time to avoid
///      spawning multiple simultaneous Python processes.
///   4. Persist the enrichment back via TemplateRegistryService.EnrichPendingAsync.
/// </summary>
public sealed class NovelTemplateReviewService : BackgroundService
{
    private readonly TemplateRegistryService         _registry;
    private readonly IdaReviewAgent                  _agent;
    private readonly ILogger<NovelTemplateReviewService> _logger;

    private static readonly TimeSpan ReviewInterval = TimeSpan.FromMinutes(5);

    public NovelTemplateReviewService(
        TemplateRegistryService registry,
        IdaReviewAgent agent,
        ILogger<NovelTemplateReviewService> logger)
    {
        _registry = registry;
        _agent    = agent;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once immediately on startup, then on the periodic interval.
        await ReviewAllPendingAsync(stoppingToken);

        using var timer = new PeriodicTimer(ReviewInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ReviewAllPendingAsync(stoppingToken);
    }

    private async Task ReviewAllPendingAsync(CancellationToken ct)
    {
        foreach (var appId in await _registry.GetPendingAppIdsAsync(ct))
        {
            if (ct.IsCancellationRequested) return;

            IReadOnlyList<NovelTemplateReviewItem> pending;
            try
            {
                pending = await _registry.GetPendingReviewsAsync(appId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch pending reviews for {App}", appId);
                continue;
            }

            var unenriched = pending
                .Where(item => string.IsNullOrEmpty(item.AiHumanReadableDescription))
                .ToList();

            if (unenriched.Count == 0) continue;

            _logger.LogInformation(
                "NovelTemplateReviewService: enriching {Count} template(s) for {App}",
                unenriched.Count, appId);

            foreach (var item in unenriched)
            {
                if (ct.IsCancellationRequested) return;
                await EnrichItemAsync(item, ct);
            }
        }
    }

    private async Task EnrichItemAsync(NovelTemplateReviewItem item, CancellationToken ct)
    {
        TemplateReviewResult? result;
        try
        {
            result = await _agent.ReviewTemplateAsync(item, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Agent review threw for template {Id}", item.TemplateId);
            return;
        }

        if (result is null)
        {
            _logger.LogDebug("Agent returned null for template {Id}; skipping enrichment", item.TemplateId);
            return;
        }

        try
        {
            await _registry.EnrichPendingAsync(
                item.TemplateId,
                result.HumanReadableDescription,
                result.SuggestedClassification,
                result.Rationale,
                result.SuggestedExtractionFields,
                ct);

            _logger.LogDebug("Template {Id} enriched: {Description}",
                item.TemplateId, result.HumanReadableDescription);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist enrichment for template {Id}", item.TemplateId);
        }
    }
}
