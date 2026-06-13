using MAIS.Core.Abstractions;
using MAIS.Core.Events;
using MAIS.Core.Models;
using MAIS.Server.Service.Api.Hubs;
using MAIS.Server.Service.Configuration;
using MAIS.Server.Service.Registry;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MAIS.Server.Service.Workers;

/// <summary>
/// Polls all registered modules at a configurable interval, updates the registry
/// with each module's reported health, and pushes status changes to all connected
/// sidebar clients via SignalR.
///
/// Failure isolation: one module's health check timing out or throwing never
/// prevents other modules from being checked.
/// </summary>
public sealed class HealthMonitorWorker : BackgroundService
{
    private readonly IModuleRegistryService _registry;
    private readonly IEventBus _eventBus;
    private readonly IHubContext<StatusHub, IStatusHubClient> _hub;
    private readonly IOptions<MaisOptions> _options;
    private readonly ILogger<HealthMonitorWorker> _logger;

    // Tracks consecutive failures per module to detect persistent degradation
    private readonly Dictionary<string, int> _consecutiveFailures = [];

    public HealthMonitorWorker(
        IModuleRegistryService registry,
        IEventBus eventBus,
        IHubContext<StatusHub, IStatusHubClient> hub,
        IOptions<MaisOptions> options,
        ILogger<HealthMonitorWorker> logger)
    {
        _registry = registry;
        _eventBus = eventBus;
        _hub = hub;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Health monitor started. Interval: {IntervalSeconds}s",
            _options.Value.HealthCheckIntervalSeconds);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.Value.HealthCheckIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckAllModulesAsync(stoppingToken);
        }
    }

    private async Task CheckAllModulesAsync(CancellationToken cancellationToken)
    {
        var modules = _registry.GetAllModules();
        if (modules.Count == 0) return;

        _logger.LogDebug("Running health checks on {Count} module(s)", modules.Count);

        var checkTasks = modules
            .Where(m => IsCheckable(_registry.GetAll()
                .FirstOrDefault(d => d.Id == m.Id)?.Status))
            .Select(m => CheckModuleAsync(m, cancellationToken));

        await Task.WhenAll(checkTasks);
    }

    private async Task CheckModuleAsync(IModule module, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ModuleHealth health;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10)); // Per-check timeout

            health = await module.GetHealthAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            health = ModuleHealth.Faulted(module.Id,
                new TimeoutException("Health check timed out after 10 seconds"));
        }
        catch (Exception ex)
        {
            health = ModuleHealth.Faulted(module.Id, ex);
        }

        sw.Stop();
        health = health with { CheckDuration = sw.Elapsed };

        await ProcessHealthResultAsync(module, health, cancellationToken);
    }

    private async Task ProcessHealthResultAsync(
        IModule module,
        ModuleHealth health,
        CancellationToken cancellationToken)
    {
        var current = _registry.GetAll().FirstOrDefault(d => d.Id == module.Id);
        if (current is null) return;

        // Track consecutive failures
        if (health.Status is ModuleStatus.Faulted or ModuleStatus.Degraded)
        {
            _consecutiveFailures.TryGetValue(module.Id, out var count);
            _consecutiveFailures[module.Id] = count + 1;
        }
        else
        {
            _consecutiveFailures[module.Id] = 0;
        }

        // Update registry if status has changed
        if (current.Status != health.Status)
        {
            _registry.UpdateStatus(module.Id, health.Status, health.StatusMessage);

            // Push real-time update to all sidebar clients
            await _hub.Clients.All.ModuleStatusUpdated(new ModuleStatusUpdate
            {
                ModuleId = module.Id,
                DisplayName = module.DisplayName,
                Status = health.Status,
                StatusMessage = health.StatusMessage,
                Timestamp = health.CheckedAt
            });

            // Publish to internal event bus for other subscribers
            await _eventBus.PublishAsync(new ModuleStatusChangedEvent(
                module.Id, module.DisplayName,
                current.Status, health.Status, health.StatusMessage),
                cancellationToken);
        }

        // Raise alert if consecutively failing
        var maxFailures = _options.Value.MaxConsecutiveHealthFailures;
        if (_consecutiveFailures.GetValueOrDefault(module.Id) == maxFailures)
        {
            _logger.LogWarning(
                "Module {ModuleId} has failed {Count} consecutive health checks. Status: {Status}",
                module.Id, maxFailures, health.Status);

            await _eventBus.PublishAsync(new ModuleHealthAlertEvent(
                module.Id, health.Status,
                health.StatusMessage ?? "Repeated health check failure",
                health.Diagnostics),
                cancellationToken);
        }

        _logger.LogDebug(
            "Health check {ModuleId}: {Status} in {Ms}ms",
            module.Id, health.Status, health.CheckDuration.TotalMilliseconds);
    }

    private static bool IsCheckable(ModuleStatus? status) =>
        status is ModuleStatus.Running or ModuleStatus.Degraded;
}
