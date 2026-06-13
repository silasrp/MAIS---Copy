using System.Diagnostics;
using System.Text.Json;
using MAIS.Modules.CrimsAddinHealth.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.CrimsAddinHealth.Agent;

/// <summary>
/// Spawns the Python AI agent as a child process and communicates via JSON stdin/stdout.
/// Falls back to a deterministic analysis if the agent fails, times out, or returns invalid JSON.
/// Azure OpenAI credentials are fetched by the agent at startup via CyberArk — nothing sensitive
/// is passed from C# to the process.
/// </summary>
public sealed class AddinHealthAgent : IAddinHealthAgent
{
    private readonly CrimsAddinHealthOptions    _options;
    private readonly ILogger<AddinHealthAgent>  _logger;

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AddinHealthAgent(IOptions<CrimsAddinHealthOptions> options, ILogger<AddinHealthAgent> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<AgentAnalysis> AnalyseScanResultAsync(ScanResult result, CancellationToken ct)
    {
        try
        {
            var agentPath = GetAgentPath();
            var input = new
            {
                action     = "analyze_scan",
                scanResult = result
            };

            var psi = new ProcessStartInfo
            {
                FileName               = agentPath,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start AI agent process.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.AgentTimeoutSeconds));

            var inputJson = JsonSerializer.Serialize(input, _json);
            await process.StandardInput.WriteLineAsync(inputJson);
            process.StandardInput.Close();

            var outputJson  = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorOutput = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (!string.IsNullOrWhiteSpace(errorOutput))
                _logger.LogWarning("Agent stderr: {Error}", errorOutput);

            if (string.IsNullOrWhiteSpace(outputJson))
            {
                _logger.LogWarning("Agent returned empty output for {Machine}", result.MachineName);
                return BuildFallback(result);
            }

            var analysis = JsonSerializer.Deserialize<AgentAnalysis>(outputJson.Trim(), _json);
            if (analysis is null)
            {
                _logger.LogWarning("Agent output could not be deserialised");
                return BuildFallback(result);
            }

            _logger.LogInformation("Agent analysis for {Machine}: Risk={Risk}, Timing={Timing}",
                result.MachineName, analysis.RiskLevel, analysis.RecommendedTiming);

            return analysis;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Agent timed out for {Machine}", result.MachineName);
            return BuildFallback(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent failed for {Machine}", result.MachineName);
            return BuildFallback(result);
        }
    }

    private AgentAnalysis BuildFallback(ScanResult result) => new()
    {
        ApprovalMessage        = $"Machine {result.MachineName} (user: {result.CrimsUserId}) " +
                                 $"requires updates to {result.Mismatches.Count} CRIMS addin(s). " +
                                 "Please review and approve.",
        RiskLevel              = result.MachineRole == "Trader" ? "High" : "Normal",
        RiskRationale          = "Fallback assessment — AI agent unavailable.",
        RecommendedTiming      = "EndOfDay",
        TimingRationale        = "Default recommendation when AI agent is unavailable.",
        EstimatedDurationSeconds = result.Mismatches.Count * 60
    };

    private string GetAgentPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, _options.AgentExePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"AI agent executable not found at: {path}");
        return path;
    }
}