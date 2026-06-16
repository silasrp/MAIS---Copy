using System.Collections.Concurrent;
using MAIS.Modules.CrimsAddinHealth.Agent;
using MAIS.Modules.CrimsAddinHealth.Hubs;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.CrimsAddinHealth.Server;

/// <summary>
/// Server-side orchestrator — drives the update state machine.
///
/// State machine per QueueEntry:
///   Waiting/Scheduled
///     → Dequeue
///   → CheckCrimsStatus (server sends command to client group, waits for response)
///       CrimsNotRunning → InitiateUpdate
///       CrimsRunning    → notify client to close CRIMS, wait (timeout → defer to end-of-day)
///   → InitiateUpdate (server sends QueueEntry to client)
///       Client reports UpdateOutcome via REST POST
///         Success → write audit, mark Complete, notify approvers
///         Failure → mark Failed, notify support, write audit
///
/// Inbound responses from clients (CRIMS status, update outcomes) arrive via
/// IAddinHealthMessageHandler implemented here, driven by REST endpoints in AddinHealthController.
/// </summary>
public sealed class UpdateOrchestrator : BackgroundService, IAddinHealthMessageHandler
{
    private readonly UpdateQueue     _queue;
    private readonly ManifestService _manifest;
    private readonly AuditLogger     _audit;
    private readonly IAddinHealthAgent _agent;
    private readonly IHubContext<AddinHealthHub, IAddinHealthHubClient> _hub;
    private readonly CrimsAddinHealthOptions _options;
    private readonly ILogger<UpdateOrchestrator> _logger;

    // Pending CRIMS-status and update-outcome waiters keyed by queueId
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CrimsProcessStatus>> _crimsWaiters = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<UpdateOutcome>>       _outcomeWaiters = new();

    // Pending approval waiters keyed by requestId
    private readonly ConcurrentDictionary<string, TaskCompletionSource<UpdateApproval>> _approvalWaiters = new();

    // In-flight requests waiting to be approved (used for scan-result ingestion)
    private readonly ConcurrentDictionary<string, UpdateRequest> _pendingRequests = new();

    public UpdateOrchestrator(
        UpdateQueue queue,
        ManifestService manifest,
        AuditLogger audit,
        IAddinHealthAgent agent,
        IHubContext<AddinHealthHub, IAddinHealthHubClient> hub,
        IOptions<CrimsAddinHealthOptions> options,
        ILogger<UpdateOrchestrator> logger)
    {
        _queue    = queue;
        _manifest = manifest;
        _audit    = audit;
        _agent    = agent;
        _hub      = hub;
        _options  = options.Value;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UpdateOrchestrator started");

        await _manifest.InitialiseAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entry = await _queue.DequeueAsync(stoppingToken);
                if (entry is null) continue;

                // Process each entry without blocking the queue drain
                _ = Task.Run(() => ProcessEntryAsync(entry, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in orchestrator loop");
            }
        }
    }

    // ── IAddinHealthMessageHandler — inbound from client REST calls ───────────

    public async Task HandleScanResultAsync(ScanResult result, CancellationToken ct)
    {
        if (!result.HasMismatches)
        {
            _logger.LogDebug("Clean scan from {Machine} — no action needed", result.MachineName);
            return;
        }

        _logger.LogInformation("Scan result from {Machine}: {Count} mismatch(es)",
            result.MachineName, result.Mismatches.Count);

        // Run AI analysis (with fallback)
        var analysis = await _agent.AnalyseScanResultAsync(result, ct);

        var request = new UpdateRequest
        {
            ScanResult    = result,
            AgentAnalysis = analysis
        };

        _pendingRequests[request.RequestId] = request;

        // Alert approvers (support/admin) via SignalR
        await _hub.Clients.Group(ModuleConstants.ApproversGroup)
            .NewUpdateRequestAlert(request);

        _logger.LogInformation("Update request {RequestId} created and sent to approvers", request.RequestId);
    }

    public Task HandleCrimsStatusAsync(string queueId, CrimsProcessStatus status, CancellationToken ct)
    {
        if (_crimsWaiters.TryGetValue(queueId, out var tcs))
            tcs.TrySetResult(status);
        return Task.CompletedTask;
    }

    public Task HandleUpdateOutcomeAsync(string queueId, UpdateOutcome outcome, CancellationToken ct)
    {
        if (_outcomeWaiters.TryGetValue(queueId, out var tcs))
            tcs.TrySetResult(outcome);
        return Task.CompletedTask;
    }

    public async Task HandleApprovalAsync(UpdateApproval approval, CancellationToken ct)
    {
        if (_approvalWaiters.TryGetValue(approval.RequestId, out var tcs))
        {
            tcs.TrySetResult(approval);
            return;
        }

        // Approval arrived before orchestrator was waiting (race condition) — enqueue directly
        if (!_pendingRequests.TryRemove(approval.RequestId, out var request)) return;

        request.Status = approval.IsApproved ? UpdateRequestStatus.Approved : UpdateRequestStatus.Deferred;

        if (!approval.IsApproved)
        {
            await _hub.Clients.Group(ModuleConstants.ApproversGroup)
                .UpdateRequestResolved(approval.RequestId);
            return;
        }

        var entry = new QueueEntry
        {
            Request           = request,
            Approval          = approval,
            ScheduledFor      = approval.DeferUntil,
            RepositoryUncPath = _options.RepositoryUncPath
        };

        if (approval.DeferUntil.HasValue)
            _queue.Schedule(entry);
        else
            _queue.Enqueue(entry);

        await _hub.Clients.Group(ModuleConstants.ApproversGroup)
            .UpdateRequestResolved(approval.RequestId);
    }

    // ── State machine ─────────────────────────────────────────────────────────

    private async Task ProcessEntryAsync(QueueEntry entry, CancellationToken ct)
    {
        var clientId    = entry.Request.ScanResult.ClientId;
        var clientGroup = ModuleConstants.ClientGroupPrefix + clientId;

        _logger.LogInformation("Processing QueueId={QueueId} for {Machine}",
            entry.QueueId, entry.Request.ScanResult.MachineName);

        await BroadcastQueueStatusAsync(entry);

        // Step 1 — check whether CRIMS is running on the target client
        var crimsStatus = await CheckCrimsStatusAsync(entry, clientGroup, ct);
        if (crimsStatus is null)
        {
            await FailEntryAsync(entry, "Timed out waiting for CRIMS status from client");
            return;
        }

        // Step 2 — if CRIMS is running, wait for it to close (with timeout)
        if (crimsStatus.IsRunning)
        {
            _logger.LogInformation("CRIMS is running on {Machine} — asking user to close it", entry.Request.ScanResult.MachineName);

            var closed = await WaitForCrimsCloseAsync(entry, clientGroup, ct);
            if (!closed)
            {
                // Defer to end of today
                var deferUntil = EndOfDay();
                _logger.LogWarning("CRIMS did not close — deferring QueueId={QueueId} to {Time}", entry.QueueId, deferUntil);

                entry.ScheduledFor = deferUntil;
                _queue.Schedule(entry);

                await _hub.Clients.Group(ModuleConstants.ApproversGroup)
                    .UpdateStatusChanged(entry);
                return;
            }
        }

        // Step 3 — send update command to client
        _logger.LogInformation("Initiating update for QueueId={QueueId}", entry.QueueId);
        entry.Status    = QueueEntryStatus.InProgress;
        entry.StartedAt = DateTimeOffset.UtcNow;
        await BroadcastQueueStatusAsync(entry);

        await _hub.Clients.Group(clientGroup).InitiateUpdate(entry);

        // Step 4 — wait for outcome
        var outcome = await WaitForUpdateOutcomeAsync(entry, ct);
        entry.CompletedAt = DateTimeOffset.UtcNow;

        if (outcome is null)
        {
            await FailEntryAsync(entry, "Timed out waiting for update outcome from client");
            return;
        }

        if (!outcome.Success)
        {
            await FailEntryAsync(entry, outcome.FailureReason ?? "Client reported update failure");
            return;
        }

        // Step 5 — write audit, mark complete
        entry.Status = QueueEntryStatus.Completed;
        _queue.Complete(entry.QueueId, success: true);

        await WriteAuditRecordAsync(entry, outcome);
        await BroadcastQueueStatusAsync(entry);

        _logger.LogInformation("Update complete for QueueId={QueueId} on {Machine}",
            entry.QueueId, entry.Request.ScanResult.MachineName);
    }

    private async Task<CrimsProcessStatus?> CheckCrimsStatusAsync(
        QueueEntry entry, string clientGroup, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<CrimsProcessStatus>();
        _crimsWaiters[entry.QueueId] = tcs;

        try
        {
            await _hub.Clients.Group(clientGroup).CheckCrimsStatus(entry.QueueId);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out waiting for CRIMS status from {ClientId}", entry.Request.ScanResult.ClientId);
            return null;
        }
        finally
        {
            _crimsWaiters.TryRemove(entry.QueueId, out _);
        }
    }

    private async Task<bool> WaitForCrimsCloseAsync(
        QueueEntry entry, string clientGroup, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_options.CrimsCloseTimeoutSeconds);
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);

            var status = await CheckCrimsStatusAsync(entry, clientGroup, ct);
            if (status is null) return false;
            if (!status.IsRunning) return true;
        }

        return false;
    }

    private async Task<UpdateOutcome?> WaitForUpdateOutcomeAsync(QueueEntry entry, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<UpdateOutcome>();
        _outcomeWaiters[entry.QueueId] = tcs;

        try
        {
            var timeout = TimeSpan.FromSeconds(_options.UpdateResponseTimeoutSeconds);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out waiting for update outcome from {ClientId}", entry.Request.ScanResult.ClientId);
            return null;
        }
        finally
        {
            _outcomeWaiters.TryRemove(entry.QueueId, out _);
        }
    }

    private async Task FailEntryAsync(QueueEntry entry, string reason)
    {
        _logger.LogError("QueueId={QueueId} failed: {Reason}", entry.QueueId, reason);

        entry.Status        = QueueEntryStatus.Failed;
        entry.FailureReason = reason;
        entry.CompletedAt   = DateTimeOffset.UtcNow;

        _queue.Complete(entry.QueueId, success: false, failureReason: reason);
        await BroadcastQueueStatusAsync(entry);

        await _audit.WriteAsync(new AuditRecord
        {
            ClientId             = entry.Request.ScanResult.ClientId,
            MachineName          = entry.Request.ScanResult.MachineName,
            AssetTag             = entry.Request.ScanResult.AssetTag,
            CrimsUserId          = entry.Request.ScanResult.CrimsUserId,
            UpdatedDlls          = [],
            DllVersionsBefore    = [],
            DllVersionsAfter     = [],
            ApprovedByMachineName = entry.Approval.ApprovedByMachineName,
            ApprovedByMachineIp  = entry.Approval.ApprovedByMachineIp,
            ApprovedByUserId     = entry.Approval.ApprovedByUserId,
            ApprovedAt           = entry.Approval.DecisionAt,
            UpdateStartedAt      = entry.StartedAt ?? entry.EnqueuedAt,
            UpdateCompletedAt    = DateTimeOffset.UtcNow,
            Success              = false,
            FailureReason        = reason
        });
    }

    private async Task WriteAuditRecordAsync(QueueEntry entry, UpdateOutcome outcome)
    {
        var scan = entry.Request.ScanResult;

        await _audit.WriteAsync(new AuditRecord
        {
            ClientId             = scan.ClientId,
            MachineName          = scan.MachineName,
            AssetTag             = scan.AssetTag,
            CrimsUserId          = scan.CrimsUserId,
            UpdatedDlls          = outcome.UpdatedFiles,
            DllVersionsBefore    = scan.Mismatches.Select(m => $"{m.FileName}:{m.InstalledVersion}").ToList(),
            DllVersionsAfter     = scan.Mismatches.Select(m => $"{m.FileName}:{m.ExpectedVersion}").ToList(),
            ApprovedByMachineName = entry.Approval.ApprovedByMachineName,
            ApprovedByMachineIp  = entry.Approval.ApprovedByMachineIp,
            ApprovedByUserId     = entry.Approval.ApprovedByUserId,
            ApprovedAt           = entry.Approval.DecisionAt,
            UpdateStartedAt      = entry.StartedAt ?? entry.EnqueuedAt,
            UpdateCompletedAt    = outcome.CompletedAt,
            Success              = true
        });
    }

    private async Task BroadcastQueueStatusAsync(QueueEntry entry)
    {
        await _hub.Clients.Group(ModuleConstants.ApproversGroup).UpdateStatusChanged(entry);
    }

    private static DateTimeOffset EndOfDay()
    {
        var today = DateTime.UtcNow.Date;
        return new DateTimeOffset(today.Year, today.Month, today.Day, 17, 0, 0, TimeSpan.Zero);
    }
}