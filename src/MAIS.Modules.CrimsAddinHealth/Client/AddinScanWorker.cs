using System.Net.Http.Json;
using MAIS.Modules.CrimsAddinHealth.Hubs;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace MAIS.Modules.CrimsAddinHealth.Client;

/// <summary>
/// Client-side background worker.
/// - Runs a scheduled DLL scan (default 8 AM daily, configurable via cron).
/// - Handles CheckCrimsStatus and InitiateUpdate commands arriving from the server via SignalR.
/// - After a successful update, relays toast notifications to the sidebar.
///
/// Manifest is fetched from the server REST API before each scan.
/// Scan results are sent back to the server via the local SignalR hub's hub methods,
/// which the server picks up through its own hub connection.
/// </summary>
public sealed class AddinScanWorker : BackgroundService
{
    private readonly LocalAddinScanner  _scanner;
    private readonly ProcessDetector    _processDetector;
    private readonly FileUpdater        _fileUpdater;
    private readonly NotificationRelay  _notificationRelay;
    private readonly CrimsAddinHealthOptions _options;
    private readonly IHubContext<AddinHealthHub, IAddinHealthHubClient> _localHub;
    private readonly HttpClient         _serverClient;
    private readonly ILogger<AddinScanWorker> _logger;

    private string _clientId    = Environment.MachineName;
    private string _machineRole = "Unknown";

    public AddinScanWorker(
        LocalAddinScanner scanner,
        ProcessDetector processDetector,
        FileUpdater fileUpdater,
        NotificationRelay notificationRelay,
        IOptions<CrimsAddinHealthOptions> options,
        IHubContext<AddinHealthHub, IAddinHealthHubClient> localHub,
        IHttpClientFactory httpClientFactory,
        ILogger<AddinScanWorker> logger)
    {
        _scanner           = scanner;
        _processDetector   = processDetector;
        _fileUpdater       = fileUpdater;
        _notificationRelay = notificationRelay;
        _options           = options.Value;
        _localHub          = localHub;
        _serverClient      = httpClientFactory.CreateClient("AddinHealthServer");
        _logger            = logger;
    }

    public void SetClientContext(string clientId, string machineRole)
    {
        _clientId    = clientId;
        _machineRole = machineRole;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the client orchestrator time to register and receive policy
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        _logger.LogInformation("AddinScanWorker started. Schedule: {Schedule}", _options.ScanSchedule);

        var schedule = CrontabSchedule.Parse(_options.ScanSchedule);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = schedule.GetNextOccurrence(DateTime.UtcNow);
            var delay   = nextRun - DateTime.UtcNow;

            _logger.LogDebug("Next scheduled scan at {NextRun} (in {Delay})", nextRun, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await RunScheduledScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in scan worker loop");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    // ── Scan logic ────────────────────────────────────────────────────────────

    public async Task RunScheduledScanAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running scheduled addin scan");
        await RunScanInternalAsync(ct);
    }

    public async Task RunOnDemandScanAsync(string requestId, CancellationToken ct)
    {
        _logger.LogInformation("Running on-demand addin scan (RequestId={RequestId})", requestId);
        await RunScanInternalAsync(ct);
    }

    private async Task RunScanInternalAsync(CancellationToken ct)
    {
        var manifest = await FetchManifestAsync(ct);
        if (manifest is null)
        {
            _logger.LogWarning("Could not fetch manifest from server — scan aborted");
            return;
        }

        var result = await _scanner.ScanAsync(manifest, _clientId, _machineRole, ct);

        if (!result.HasMismatches)
        {
            _logger.LogInformation("All CRIMS DLLs are up to date");
            return;
        }

        _logger.LogInformation("{Count} DLL mismatch(es) found — notifying user and reporting to server",
            result.Mismatches.Count);

        _notificationRelay.ShowToast(new ToastMessage
        {
            Title = "CRIMS Addin Update Required",
            Body  = $"{result.Mismatches.Count} DLL(s) are out of date. Support will be notified.",
            Type  = ToastType.Warning
        });

        // Deliver scan result to the server via our local hub (server is connected as a client too,
        // OR the client reports results via REST POST in a production integration).
        // Here we use REST as the most reliable delivery path.
        await ReportScanResultToServerAsync(result, ct);
    }

    // ── Server-initiated commands (called by client hub listener) ─────────────

    /// <summary>Called when server sends CheckCrimsStatus for a queue entry.</summary>
    public async Task HandleCheckCrimsStatusAsync(string queueId, CancellationToken ct)
    {
        var isRunning = _processDetector.IsCrimsRunning();

        if (isRunning)
        {
            _notificationRelay.ShowToast(new ToastMessage
            {
                Title  = "CRIMS Update Pending",
                Body   = "Please save your work and close CRIMS to allow an addin update.",
                Type   = ToastType.Warning,
                RequiresAction = false
            });
        }

        var status = new CrimsProcessStatus
        {
            QueueId     = queueId,
            IsRunning   = isRunning,
            MachineName = Environment.MachineName
        };

        await ReportCrimsStatusToServerAsync(status, ct);
    }

    /// <summary>Called when server sends InitiateUpdate for a queue entry.</summary>
    public async Task HandleInitiateUpdateAsync(QueueEntry entry, CancellationToken ct)
    {
        _logger.LogInformation("Server initiated update for QueueId={QueueId}", entry.QueueId);

        var manifest = await FetchManifestAsync(ct);
        if (manifest is null)
        {
            await ReportUpdateOutcomeToServerAsync(new UpdateOutcome
            {
                QueueId       = entry.QueueId,
                Success       = false,
                FailureReason = "Could not fetch manifest from server.",
                CompletedAt   = DateTimeOffset.UtcNow
            }, ct);
            return;
        }

        try
        {
            // Ensure CRIMS is not running before updating
            if (_processDetector.IsCrimsRunning())
            {
                var timeout = TimeSpan.FromSeconds(_options.CrimsCloseTimeoutSeconds);
                await _processDetector.WaitForCrimsCloseAsync(timeout, ct);
            }

            var outcome = await _fileUpdater.UpdateDllsAsync(entry, manifest, ct);

            _notificationRelay.ShowToast(new ToastMessage
            {
                Title = "CRIMS Addin Update Complete",
                Body  = $"{outcome.UpdatedFiles.Count} DLL(s) updated successfully. You can restart CRIMS.",
                Type  = ToastType.Success
            });

            await ReportUpdateOutcomeToServerAsync(outcome, ct);
        }
        catch (TimeoutException tex)
        {
            _logger.LogWarning("CRIMS did not close: {Message}", tex.Message);
            _notificationRelay.ShowToast(new ToastMessage
            {
                Title = "CRIMS Update Deferred",
                Body  = "CRIMS did not close in time. Update has been rescheduled.",
                Type  = ToastType.Warning
            });
            await ReportUpdateOutcomeToServerAsync(new UpdateOutcome
            {
                QueueId       = entry.QueueId,
                Success       = false,
                FailureReason = $"CRIMS did not close: {tex.Message}",
                CompletedAt   = DateTimeOffset.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed for QueueId={QueueId}", entry.QueueId);
            _notificationRelay.ShowToast(new ToastMessage
            {
                Title = "CRIMS Update Failed",
                Body  = "The update failed. Please contact support.",
                Type  = ToastType.Critical
            });
            await ReportUpdateOutcomeToServerAsync(new UpdateOutcome
            {
                QueueId       = entry.QueueId,
                Success       = false,
                FailureReason = ex.Message,
                CompletedAt   = DateTimeOffset.UtcNow
            }, ct);
        }
    }

    // ── HTTP communication with server ────────────────────────────────────────

    private async Task<AddinManifest?> FetchManifestAsync(CancellationToken ct)
    {
        try
        {
            return await _serverClient.GetFromJsonAsync<AddinManifest>(
                "/api/v1/addin-health/manifest", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch addin manifest from server");
            return null;
        }
    }

    private async Task ReportScanResultToServerAsync(ScanResult result, CancellationToken ct)
    {
        try
        {
            var response = await _serverClient.PostAsJsonAsync(
                "/api/v1/addin-health/scan-results", result, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Server rejected scan result: {Status}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report scan result to server");
        }
    }

    private async Task ReportCrimsStatusToServerAsync(CrimsProcessStatus status, CancellationToken ct)
    {
        try
        {
            await _serverClient.PostAsJsonAsync(
                $"/api/v1/addin-health/crims-status/{status.QueueId}", status, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report CRIMS status to server");
        }
    }

    private async Task ReportUpdateOutcomeToServerAsync(UpdateOutcome outcome, CancellationToken ct)
    {
        try
        {
            await _serverClient.PostAsJsonAsync(
                $"/api/v1/addin-health/update-outcome/{outcome.QueueId}", outcome, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report update outcome to server");
        }
    }
}