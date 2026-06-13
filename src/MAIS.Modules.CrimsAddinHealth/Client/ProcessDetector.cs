using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsAddinHealth.Client;

/// <summary>
/// Detects whether CRIMS is running on the local machine.
/// Polls until CRIMS exits or the timeout elapses.
/// </summary>
public sealed class ProcessDetector
{
    private static readonly string[] CrimsProcessNames =
        ["CharlesRiverIMS", "CharlesRiverIMSSvc"];

    private readonly ILogger<ProcessDetector> _logger;

    public ProcessDetector(ILogger<ProcessDetector> logger) => _logger = logger;

    public bool IsCrimsRunning() =>
        CrimsProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);

    /// <summary>
    /// Polls every 5 seconds until CRIMS is no longer running or the timeout elapses.
    /// Throws <see cref="TimeoutException"/> if CRIMS is still running when the timeout expires.
    /// </summary>
    public async Task WaitForCrimsCloseAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        _logger.LogInformation("Waiting for CRIMS to close (timeout: {Timeout}s)", timeout.TotalSeconds);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                if (!IsCrimsRunning())
                {
                    _logger.LogInformation("CRIMS closed");
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"CRIMS did not close within the allowed window of {timeout.TotalSeconds}s.");
        }
    }
}