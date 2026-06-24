using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.IdaLogIngestion.Server;

/// <summary>
/// Spawns agent.exe as a child process and communicates via JSON stdin/stdout IPC.
/// Returns null on any failure — NovelTemplateReviewService handles graceful degradation.
/// Agent contract: read one JSON line from stdin, write one JSON line to stdout, exit 0.
/// </summary>
public sealed class IdaReviewAgent
{
    private readonly IdaLogIngestionOptions     _options;
    private readonly ILogger<IdaReviewAgent>    _logger;

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public IdaReviewAgent(IOptions<IdaLogIngestionOptions> options, ILogger<IdaReviewAgent> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<TemplateReviewResult?> ReviewTemplateAsync(NovelTemplateReviewItem item, CancellationToken ct)
    {
        string agentPath;
        try
        {
            agentPath = GetAgentPath();
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex.Message);
            return null;
        }

        var input = new
        {
            action         = "review_template",
            templateId     = item.TemplateId,
            tokenPattern   = item.TokenPattern,
            sampleMessages = item.SampleMessages
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

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start IdaReviewAgent process");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.AgentTimeoutSeconds));

            var inputJson = JsonSerializer.Serialize(input, _json);
            await process.StandardInput.WriteLineAsync(inputJson);
            process.StandardInput.Close();

            var outputJson  = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorOutput = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (!string.IsNullOrWhiteSpace(errorOutput))
                _logger.LogDebug("Agent stderr for {Id}: {Error}", item.TemplateId, errorOutput);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(outputJson))
            {
                _logger.LogWarning("Agent exited {Code} for template {Id}. Stderr: {Error}",
                    process.ExitCode, item.TemplateId, errorOutput);
                return null;
            }

            var response = JsonSerializer.Deserialize<AgentResponse>(outputJson.Trim(), _json);
            if (response is null)
            {
                _logger.LogWarning("Agent output could not be deserialised for {Id}", item.TemplateId);
                return null;
            }

            ClassificationAction? suggested = null;
            if (Enum.TryParse<ClassificationAction>(response.SuggestedClassification, ignoreCase: true, out var parsed))
                suggested = parsed;

            _logger.LogInformation(
                "Agent reviewed template {Id}: suggested={Suggestion}",
                item.TemplateId, response.SuggestedClassification);

            return new TemplateReviewResult
            {
                HumanReadableDescription    = response.HumanReadableDescription,
                SuggestedClassification     = suggested,
                Rationale                   = response.Rationale,
                SuggestedExtractionFields   = response.SuggestedExtractionFields
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Agent timed out reviewing template {Id}", item.TemplateId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent failed reviewing template {Id}", item.TemplateId);
            return null;
        }
    }

    private string GetAgentPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, _options.AgentExePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"IdaReviewAgent executable not found at: {path}");
        return path;
    }

    // ── Deserialization target for the raw agent stdout ───────────────────────

    private sealed class AgentResponse
    {
        [JsonPropertyName("humanReadableDescription")]
        public string HumanReadableDescription { get; init; } = "";

        [JsonPropertyName("suggestedClassification")]
        public string SuggestedClassification { get; init; } = "";

        [JsonPropertyName("rationale")]
        public string Rationale { get; init; } = "";

        [JsonPropertyName("suggestedExtractionFields")]
        public string[] SuggestedExtractionFields { get; init; } = [];
    }
}

/// <summary>Parsed result from IdaReviewAgent — ready to be stored on NovelTemplateReviewItem.</summary>
public sealed class TemplateReviewResult
{
    public string                HumanReadableDescription  { get; init; } = "";
    public ClassificationAction? SuggestedClassification   { get; init; }
    public string                Rationale                 { get; init; } = "";
    public string[]              SuggestedExtractionFields { get; init; } = [];
}
