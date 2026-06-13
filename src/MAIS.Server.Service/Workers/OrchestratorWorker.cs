using MAIS.Core.Abstractions;
using MAIS.Core.Events;
using MAIS.Core.Models;
using MAIS.Server.Service.Configuration;
using MAIS.Server.Service.Registry;
using Microsoft.Extensions.Options;

namespace MAIS.Server.Service.Workers;

/// <summary>
/// The primary orchestration worker for the server. Responsible for:
/// <list type="bullet">
///   <item>Starting all registered server modules on host startup.</item>
///   <item>Stopping all modules gracefully on host shutdown.</item>
///   <item>Optionally restarting faulted modules based on configuration policy.</item>
///   <item>Launching the sidebar process if configured to do so.</item>
/// </list>
/// </summary>
public sealed class OrchestratorWorker : BackgroundService
{
    private readonly IModuleRegistryService _registry;
    private readonly IEventBus _eventBus;
    private readonly IOptions<MaisOptions> _options;
    private readonly ILogger<OrchestratorWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _serviceProvider;

    private System.Diagnostics.Process? _sidebarProcess;

    public OrchestratorWorker(
        IServiceProvider serviceProvider,
        IModuleRegistryService registry,
        IEventBus eventBus,
        IOptions<MaisOptions> options,
        ILogger<OrchestratorWorker> logger,
        IHostApplicationLifetime lifetime)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _eventBus = eventBus;
        _options = options;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until the host has fully started before doing anything.
        await WaitForHostStartAsync(stoppingToken);

        // Auto-discover and register server modules from DI (filter by HostType)
        var allModules = _serviceProvider.GetServices<IModule>();
        var serverModules = allModules
            .Where(m => m.HostType == ModuleHostType.Server || m.HostType == ModuleHostType.Both)
            .ToList();

        foreach (var module in serverModules)
        {
            _registry.Register(module);
        }

        _logger.LogInformation("Orchestrator discovered {ModuleCount} server modules", serverModules.Count);

        _logger.LogInformation("Orchestrator starting all registered modules");
        await StartAllModulesAsync(stoppingToken);

        if (_options.Value.LaunchSidebarOnStart)
            LaunchSidebar();

        // The orchestrator sits idle while modules run.
        // Future: could process a command channel here.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _logger.LogInformation("Orchestrator received stop signal — stopping all modules");
        await StopAllModulesAsync(CancellationToken.None); // Don't cancel during shutdown

        KillSidebar();
    }

    // ── Module lifecycle ──────────────────────────────────────────────────────

    private async Task StartAllModulesAsync(CancellationToken cancellationToken)
    {
        var modules = _registry.GetAllModules();

        if (modules.Count == 0)
        {
            _logger.LogInformation("No modules registered — nothing to start");
            return;
        }

        foreach (var module in modules)
        {
            await StartModuleAsync(module, cancellationToken);
        }
    }

    private async Task StartModuleAsync(IModule module, CancellationToken cancellationToken)
    {
        _registry.UpdateStatus(module.Id, ModuleStatus.Starting);

        var timeout = TimeSpan.FromSeconds(_options.Value.ModuleStartTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            _logger.LogInformation("Initialising module {ModuleId}", module.Id);
            await module.InitialiseAsync(cts.Token);

            _logger.LogInformation("Starting module {ModuleId}", module.Id);
            await module.StartAsync(cts.Token);

            _registry.UpdateStatus(module.Id, ModuleStatus.Running);

            await _eventBus.PublishAsync(new ModuleStatusChangedEvent(
                module.Id, module.DisplayName,
                ModuleStatus.Starting, ModuleStatus.Running, null),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Module {ModuleId} failed to start within {Timeout}s", module.Id, timeout.TotalSeconds);
            _registry.UpdateStatus(module.Id, ModuleStatus.Faulted, "Start timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {ModuleId} failed to start", module.Id);
            _registry.UpdateStatus(module.Id, ModuleStatus.Faulted, ex.Message);

            await _eventBus.PublishAsync(new ModuleStatusChangedEvent(
                module.Id, module.DisplayName,
                ModuleStatus.Starting, ModuleStatus.Faulted, ex.Message),
                cancellationToken);
        }
    }

    private async Task StopAllModulesAsync(CancellationToken cancellationToken)
    {
        var modules = _registry.GetAllModules();
        var timeout = TimeSpan.FromSeconds(_options.Value.ModuleStopTimeoutSeconds);

        var stopTasks = modules.Select(m => StopModuleAsync(m, timeout, cancellationToken));
        await Task.WhenAll(stopTasks);
    }

    private async Task StopModuleAsync(IModule module, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _registry.UpdateStatus(module.Id, ModuleStatus.Stopping);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await module.StopAsync(cts.Token);
            _registry.UpdateStatus(module.Id, ModuleStatus.Stopped);
            _logger.LogInformation("Module {ModuleId} stopped cleanly", module.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Module {ModuleId} did not stop cleanly", module.Id);
            _registry.UpdateStatus(module.Id, ModuleStatus.Faulted, ex.Message);
        }
    }

    // ── Sidebar process management ────────────────────────────────────────────

    private void LaunchSidebar()
    {
        var path = _options.Value.SidebarExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _logger.LogWarning(
                "Sidebar executable not found at '{Path}'. Skipping sidebar launch.", path);
            return;
        }

        try
        {
            _sidebarProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });

            _logger.LogInformation("Sidebar launched (PID {Pid})", _sidebarProcess?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch sidebar");
        }
    }

    private void KillSidebar()
    {
        if (_sidebarProcess is null || _sidebarProcess.HasExited) return;

        try
        {
            _sidebarProcess.CloseMainWindow();
            if (!_sidebarProcess.WaitForExit(3000))
                _sidebarProcess.Kill();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing sidebar process");
        }
        finally
        {
            _sidebarProcess.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task WaitForHostStartAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        using var reg = _lifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
        using var cReg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        await tcs.Task;
    }
}
