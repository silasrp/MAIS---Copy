using MAIS.Client.Service.Configuration;
using MAIS.Client.Service.Registries;
using MAIS.Core.Abstractions;
using MAIS.Core.Contracts;
using MAIS.Core.Models;
using Microsoft.Extensions.Options;

namespace MAIS.Client.Service.Workers;

/// <summary>
/// Primary orchestration worker for the client service.
/// Responsible for:
/// - Auto-discovering client modules from DI
/// - Registering with server on startup
/// - Fetching and caching policy
/// - Starting modules allowed by policy
/// - Graceful shutdown
/// </summary>
public sealed class ClientOrchestratorWorker : BackgroundService, IPolicyProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IModuleRegistryService _registry;
    private readonly IServerApiClient _serverApiClient;
    private readonly ILogger<ClientOrchestratorWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ClientOptions _clientOptions;

    private string? _clientId;
    private ClientProfile? _cachedPolicy;
    public ClientProfile? GetCurrentPolicy() => _cachedPolicy;

    public ClientOrchestratorWorker(
        IServiceProvider serviceProvider,
        IModuleRegistryService registry,
        IServerApiClient serverApiClient,
        ILogger<ClientOrchestratorWorker> logger,
        IHostApplicationLifetime lifetime,
        IOptions<ClientOptions> clientOptions)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _serverApiClient = serverApiClient;
        _logger = logger;
        _lifetime = lifetime;
        _clientOptions = clientOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait until the host has fully started
            await WaitForHostStartAsync(stoppingToken);

            // Generate or use configured ClientId
            _clientId = GenerateClientId();
            _logger.LogInformation("Client ID: {ClientId}", _clientId);

            // Auto-discover and register all IModule instances
            var allModules = _serviceProvider.GetServices<IModule>();
            var clientModules = allModules
                .Where(m => m.HostType == ModuleHostType.Client || m.HostType == ModuleHostType.Both)
                .ToList();

            foreach (var module in clientModules)
                _registry.Register(module);

            _logger.LogInformation("Discovered {ModuleCount} client modules", clientModules.Count);

            // Register with server
            var registered = await RegisterWithServerAsync(stoppingToken);
            if (!registered)
            {
                _logger.LogWarning("Failed to register with server; proceeding with cached policy if available");
            }

            // Fetch policy from server
            var policy = await FetchPolicyAsync(stoppingToken);
            if (policy == null)
            {
                _logger.LogWarning("No policy received from server; using default (no modules enabled)");
                _cachedPolicy = new ClientProfile
                {
                    ClientId = _clientId,
                    Role = _clientOptions.UserRole,
                    EnableSidebar = false,
                    EnabledModules = []
                };
            }
            else
            {
                _cachedPolicy = policy;
            }

            // Start only allowed modules
            await StartAllowedModulesAsync(stoppingToken);

            // Launch sidebar if configured and policy allows
            if (_cachedPolicy.EnableSidebar && _clientOptions.LaunchSidebarOnStart)
                LaunchSidebar();

            _logger.LogInformation("Client orchestrator ready");

            // Run until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client orchestrator cancelled");
        }
        finally
        {
            _logger.LogInformation("Client orchestrator stopping — shutting down modules");
            await StopAllModulesAsync(CancellationToken.None);
        }
    }

    private async Task<bool> RegisterWithServerAsync(CancellationToken ct)
    {
        try
        {
            var modules = _registry.GetAllModules();
            var request = new ClientRegistrationRequest
            {
                ClientId = _clientId!,
                MachineName = Environment.MachineName,
                UserRole = _clientOptions.UserRole,
                ClientVersion = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString() ?? "1.0.0",
                Modules = modules
                    .Select(m => new ModuleStatusReport
                    {
                        ModuleId = m.Id,
                        DisplayName = m.DisplayName,
                        Status = m.Status,
                        ReportedAt = DateTimeOffset.UtcNow
                    })
                    .ToList()
            };

            var response = await _serverApiClient.RegisterAsync(request, ct);
            if (response != null && response.Registered)
            {
                _logger.LogInformation("Successfully registered with server");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering with server");
            return false;
        }
    }

    private async Task<ClientProfile?> FetchPolicyAsync(CancellationToken ct)
    {
        try
        {
            var policy = await _serverApiClient.GetPolicyAsync(_clientId!, ct);
            if (policy != null)
            {
                _logger.LogInformation("Fetched policy from server: Role={Role}, EnableSidebar={EnableSidebar}, ModuleCount={ModuleCount}",
                    policy.Role, policy.EnableSidebar, policy.EnabledModules.Count);
            }
            return policy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching policy from server");
            return null;
        }
    }

    private async Task StartAllowedModulesAsync(CancellationToken cancellationToken)
    {
        var modules = _registry.GetAllModules();

        if (modules.Count == 0)
        {
            _logger.LogWarning("No modules registered");
            return;
        }

        var allowedModuleIds = _cachedPolicy?.EnabledModules ?? [];

        foreach (var descriptor in modules)
        {
            if (!allowedModuleIds.Contains(descriptor.Id))
            {
                _logger.LogDebug("Module {ModuleId} not in policy; skipping", descriptor.Id);
                continue;
            }

            var module = _registry.Get(descriptor.Id);
            if (module == null)
            {
                _logger.LogError("Module {ModuleId} registered but not found", descriptor.Id);
                continue;
            }

            try
            {
                _registry.UpdateStatus(descriptor.Id, ModuleStatus.Starting);
                _logger.LogInformation("Initialising module: {ModuleId}", descriptor.Id);
                await module.InitialiseAsync(cancellationToken);

                _logger.LogInformation("Starting module: {ModuleId}", descriptor.Id);
                await module.StartAsync(cancellationToken);

                _registry.UpdateStatus(descriptor.Id, ModuleStatus.Running);
                _logger.LogInformation("Module started: {ModuleId}", descriptor.Id);
            }
            catch (Exception ex)
            {
                _registry.UpdateStatus(descriptor.Id, ModuleStatus.Faulted, ex.Message);
                _logger.LogError(ex, "Failed to start module {ModuleId}", descriptor.Id);
            }
        }
    }

    private async Task StopAllModulesAsync(CancellationToken cancellationToken)
    {
        var modules = _registry.GetAllModules();

        foreach (var descriptor in modules)
        {
            if (descriptor.Status == ModuleStatus.Stopped || descriptor.Status == ModuleStatus.Faulted)
                continue;

            var module = _registry.Get(descriptor.Id);
            if (module == null)
                continue;

            try
            {
                _registry.UpdateStatus(descriptor.Id, ModuleStatus.Stopping);
                _logger.LogInformation("Stopping module: {ModuleId}", descriptor.Id);
                await module.StopAsync(cancellationToken);

                _registry.UpdateStatus(descriptor.Id, ModuleStatus.Stopped);
                _logger.LogInformation("Module stopped: {ModuleId}", descriptor.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping module {ModuleId}", descriptor.Id);
            }
        }
    }

    private void LaunchSidebar()
    {
        try
        {
            var sidebarPath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "MAIS.Sidebar",
                "bin",
                "Release",
                "net10.0-windows",
                "win-x64",
                "MAIS.Sidebar.exe");

            if (System.IO.File.Exists(sidebarPath))
            {
                System.Diagnostics.Process.Start(sidebarPath);
                _logger.LogInformation("Sidebar launched");
            }
            else
            {
                _logger.LogWarning("Sidebar executable not found at {Path}", sidebarPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error launching sidebar");
        }
    }

    private string GenerateClientId()
    {
        if (!string.IsNullOrWhiteSpace(_clientOptions.ClientId))
            return _clientOptions.ClientId;

        var machineId = Environment.MachineName;
        var uniqueId = Guid.NewGuid().ToString()[..8];
        return $"{machineId}-{uniqueId}";
    }

    private static async Task WaitForHostStartAsync(CancellationToken ct)
    {
        await Task.Delay(100, ct);
    }
}
