using MAIS.Core.Abstractions;
using MAIS.Core.Models;

namespace MAIS.Modules.CrimsSeverity
{
    /// <summary>
    /// MAIS module descriptor for the CRIMS log severity distribution panel.
    /// Registered with the MAIS.Service module registry by <see cref="Extensions.ServiceExtensions"/>.
    /// </summary>
    public sealed class CrimsSeverityModule : IModule
    {
        private readonly CrimsSeverityOptions _options;

        public string     Id          => ModuleConstants.ModuleId;
        public string     DisplayName => "CRIMS Severity";
        public string     Description => "Real-time log severity distribution from the CRIMS log aggregator";
        public string     Version     => "1.0.0";
        public ModuleType Type        => ModuleType.ExternalEndpoint;
        public Uri?       LaunchUri   => new Uri(_options.SourceApplicationUrl);

        public ModuleHostType HostType => _options.HostType;

        public CrimsSeverityModule(CrimsSeverityOptions options)
        {
            _options = options;
        }

        // External endpoint module — lifecycle is managed by CrimsSeverityWorker.
        public Task InitialiseAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StartAsync(CancellationToken ct)      => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct)       => Task.CompletedTask;

        public Task<ModuleHealth> GetHealthAsync(CancellationToken ct) =>
            Task.FromResult(ModuleHealth.Healthy(Id,
                $"Polling {_options.DataEndpointUrl} every {_options.PollingIntervalSeconds}s"));
    }

    /// <summary>Shared constants for the CrimsSeverity module, used by both service and sidebar sides.</summary>
    public static class ModuleConstants
    {
        public const string ModuleId = "mais.crims-severity";
    }
}
