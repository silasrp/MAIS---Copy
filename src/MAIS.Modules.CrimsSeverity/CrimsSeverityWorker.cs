using System.Net.Http;
using System.Net.Http.Json;
using MAIS.Core.Models;
using MAIS.Core.Abstractions;
using MAIS.Modules.CrimsSeverity.Reporters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.CrimsSeverity
{
    /// <summary>
    /// Polls the CRIMS log aggregator severity endpoint on a fixed interval.
    /// Reports data via ISeverityReporter abstraction, allowing server-side (SignalR)
    /// or client-side (logging) implementations based on deployment environment.
    /// One poller regardless of how many connected clients (on server) or instances (on client).
    /// </summary>
    public sealed class CrimsSeverityWorker : BackgroundService
    {
        private readonly ISeverityReporter _reporter;
        private readonly IModuleRegistry _registry;
        private readonly CrimsSeverityOptions _options;
        private readonly ILogger<CrimsSeverityWorker> _logger;
        private readonly HttpClient _http;

        private readonly Queue<int> _criticalHistory = new();
        private DateTimeOffset? _spikeTriggeredAt;
        private int _previousCriticalCount;
        private bool _endpointAvailable = true;

        public CrimsSeverityWorker(
            ISeverityReporter reporter,
            IModuleRegistry registry,
            IOptions<CrimsSeverityOptions> options,
            ILogger<CrimsSeverityWorker> logger)
        {
            _reporter = reporter;
            _registry = registry;
            _options  = options.Value;
            _logger   = logger;
            _http     = new HttpClient { Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds) };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "CrimsSeverity worker started. Endpoint: {Url} | Interval: {Interval}s",
                _options.DataEndpointUrl, _options.PollingIntervalSeconds);

            using var timer = new PeriodicTimer(
                TimeSpan.FromSeconds(_options.PollingIntervalSeconds));

            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PollAndBroadcastAsync(stoppingToken);
        }

        private async Task PollAndBroadcastAsync(CancellationToken ct)
        {
            List<SeverityEntry>? entries;
            try
            {
                entries = await _http.GetFromJsonAsync<List<SeverityEntry>>(
                    _options.DataEndpointUrl, ct);

                if (entries is null || entries.Count == 0) return;

                if (!_endpointAvailable)
                {
                    _endpointAvailable = true;
                    _registry.UpdateStatus(ModuleConstants.ModuleId, ModuleStatus.Running);
                    _logger.LogInformation("CrimsSeverity endpoint back online");
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (_endpointAvailable)
                {
                    _endpointAvailable = false;
                    _registry.UpdateStatus(ModuleConstants.ModuleId, ModuleStatus.Degraded, ex.Message);
                    await _reporter.ReportEndpointUnavailableAsync(ex.Message, ct);
                    _logger.LogWarning("CrimsSeverity endpoint unreachable: {Message}", ex.Message);
                }
                return;
            }

            // ── Spike detection ───────────────────────────────────────────────
            var current = entries
                .FirstOrDefault(e => string.Equals(e.Key, "CRITICAL", StringComparison.OrdinalIgnoreCase))
                ?.Count ?? 0;

            var delta = current - _previousCriticalCount;
            _previousCriticalCount = current;

            _criticalHistory.Enqueue(current);
            while (_criticalHistory.Count > _options.SpikeWindowCycles)
                _criticalHistory.Dequeue();

            var windowDelta = current - _criticalHistory.Min();

            if (windowDelta > _options.SpikeCriticalDeltaThreshold && _spikeTriggeredAt is null)
            {
                _spikeTriggeredAt = DateTimeOffset.UtcNow;
                _logger.LogWarning(
                    "Critical spike: +{Delta} in {Window} cycles (threshold: {Threshold})",
                    windowDelta, _options.SpikeWindowCycles, _options.SpikeCriticalDeltaThreshold);
                _registry.UpdateStatus(ModuleConstants.ModuleId, ModuleStatus.Degraded,
                    $"Critical spike: +{windowDelta}");
            }

            var isSpikeActive    = false;
            var cooldownRemaining = 0;

            if (_spikeTriggeredAt.HasValue)
            {
                var elapsed  = DateTimeOffset.UtcNow - _spikeTriggeredAt.Value;
                var cooldown = TimeSpan.FromSeconds(_options.CooldownSeconds);

                if (elapsed < cooldown)
                {
                    isSpikeActive    = true;
                    cooldownRemaining = (int)(cooldown - elapsed).TotalSeconds;
                }
                else
                {
                    _spikeTriggeredAt = null;
                    _logger.LogInformation("CrimsSeverity spike cooldown expired");
                    _registry.UpdateStatus(ModuleConstants.ModuleId, ModuleStatus.Running, null);
                }
            }

            await _reporter.ReportSeverityDataAsync(
                new SeverityDataUpdate
                {
                    Data                     = entries.AsReadOnly(),
                    IsSpikeActive            = isSpikeActive,
                    CooldownRemainingSeconds = cooldownRemaining,
                    CriticalDelta            = delta,
                    SourceApplicationUrl     = _options.SourceApplicationUrl
                },
                ct);
        }

        public override void Dispose() 
        { 
            _http.Dispose(); 
            base.Dispose(); 
        }
    }
}

