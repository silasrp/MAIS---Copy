using MAIS.Client.Service.Configuration;
using MAIS.Client.Service.Registries;
using MAIS.Core.Abstractions;
using MAIS.Core.Contracts;
using MAIS.Core.Models;
using Microsoft.Extensions.Options;

namespace MAIS.Client.Service.Workers;

/// <summary>
/// Background worker that periodically reports module health and status to the server.
/// Runs on a configurable interval to keep server aware of client state.
/// </summary>
public sealed class HealthReporterWorker : BackgroundService
{
    private readonly IModuleRegistryService _registry;
    private readonly IServerApiClient _serverApiClient;
    private readonly ILogger<HealthReporterWorker> _logger;
    private readonly ClientOptions _clientOptions;
    private string? _clientId;

    public HealthReporterWorker(
        IModuleRegistryService registry,
        IServerApiClient serverApiClient,
        ILogger<HealthReporterWorker> logger,
        IOptions<ClientOptions> clientOptions)
    {
        _registry = registry;
        _serverApiClient = serverApiClient;
        _logger = logger;
        _clientOptions = clientOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for orchestrator to set up and start modules
        await Task.Delay(5000, stoppingToken);

        var reportInterval = TimeSpan.FromSeconds(
            Math.Max(_clientOptions.HealthReportIntervalSeconds, 30));

        _logger.LogInformation("Health reporter starting with interval: {Interval}", reportInterval);

        using var timer = new PeriodicTimer(reportInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReportHealthAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Health reporter cancelled");
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task ReportHealthAsync(CancellationToken ct)
    {
        try
        {
            // Get or generate client ID from environment
            _clientId ??= GenerateClientId();

            var modules = _registry.GetAllModules();
            var statusReports = new List<ModuleStatusReport>();

            foreach (var descriptor in modules)
            {
                var module = _registry.Get(descriptor.Id);
                if (module == null)
                    continue;

                // Get module health
                ModuleHealth health;
                try
                {
                    health = await module.GetHealthAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting health for module {ModuleId}", descriptor.Id);
                    health = new ModuleHealth
                    {
                        ModuleId = descriptor.Id,
                        Status = ModuleStatus.Unknown,
                        StatusMessage = ex.Message,
                        CheckedAt = DateTimeOffset.UtcNow,
                        CheckDuration = TimeSpan.Zero
                    };
                }

                statusReports.Add(new ModuleStatusReport
                {
                    ModuleId = descriptor.Id,
                    DisplayName = descriptor.DisplayName,
                    Status = health.Status,
                    Health = health,
                    ReportedAt = DateTimeOffset.UtcNow
                });
            }

            if (statusReports.Count == 0)
            {
                _logger.LogDebug("No modules to report health for");
                return;
            }

            // Report to server
            var success = await _serverApiClient.ReportStatusAsync(_clientId, statusReports, ct);
            if (success)
            {
                _logger.LogDebug("Health report submitted: {ModuleCount} modules", statusReports.Count);
            }
            else
            {
                _logger.LogWarning("Failed to submit health report");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in health reporter");
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
}
