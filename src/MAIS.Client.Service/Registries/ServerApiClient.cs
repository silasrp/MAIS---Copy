using MAIS.Core.Contracts;

namespace MAIS.Client.Service.Registries;

/// <summary>
/// HTTP client for communication with MAIS Server.
/// Handles registration, policy fetching, and status reporting.
/// </summary>
public interface IServerApiClient
{
    /// <summary>Register this client on server startup.</summary>
    Task<ClientRegistrationResponse?> RegisterAsync(ClientRegistrationRequest request, CancellationToken ct);

    /// <summary>Fetch policy from server for this client.</summary>
    Task<ClientProfile?> GetPolicyAsync(string clientId, CancellationToken ct);

    /// <summary>Report module status and health to server.</summary>
    Task<bool> ReportStatusAsync(string clientId, List<ModuleStatusReport> modules, CancellationToken ct);

    /// <summary>Check if server is reachable.</summary>
    Task<bool> HealthCheckAsync(CancellationToken ct);
}

/// <summary>
/// Implementation of IServerApiClient using HttpClient.
/// </summary>
public sealed class ServerApiClient : IServerApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerApiClient> _logger;

    public ServerApiClient(
        HttpClient httpClient,
        ILogger<ServerApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ClientRegistrationResponse?> RegisterAsync(
        ClientRegistrationRequest request,
        CancellationToken ct)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "/api/v1/clients/register",
                content,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to register client: {StatusCode}", response.StatusCode);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var apiResponse = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<ClientRegistrationResponse>>(
                responseBody,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return apiResponse?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering client");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<ClientProfile?> GetPolicyAsync(string clientId, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/clients/{Uri.EscapeDataString(clientId)}/policy",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch policy: {StatusCode}", response.StatusCode);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var apiResponse = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<ClientProfile>>(
                responseBody,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return apiResponse?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching policy for client {ClientId}", clientId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ReportStatusAsync(
        string clientId,
        List<ModuleStatusReport> modules,
        CancellationToken ct)
    {
        try
        {
            var payload = new { modules };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/api/v1/clients/{Uri.EscapeDataString(clientId)}/status",
                content,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to report status: {StatusCode}", response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting status for client {ClientId}", clientId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HealthCheckAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Server health check failed");
            return false;
        }
    }
}
