using MAIS.Core.Contracts;
using MAIS.Server.Service.Api.Dto;
using MAIS.Server.Service.Configuration;
using MAIS.Server.Service.Registries;
using ApiResponseHelper = MAIS.Server.Service.Api.Dto.ApiResponse;
using Microsoft.AspNetCore.Mvc;

namespace MAIS.Server.Service.Api.Controllers;

/// <summary>
/// REST API for client registration and policy management.
/// Clients call these endpoints to register, report status, and fetch policy.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class ClientsController : ControllerBase
{
    private readonly IClientRegistry _clientRegistry;
    private readonly IPolicyService _policyService;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(
        IClientRegistry clientRegistry,
        IPolicyService policyService,
        ILogger<ClientsController> logger)
    {
        _clientRegistry = clientRegistry;
        _policyService = policyService;
        _logger = logger;
    }

    /// <summary>
    /// Register a client on startup.
    /// Clients call this first before fetching policy or reporting status.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<ClientRegistrationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public IActionResult Register([FromBody] ClientRegistrationRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ClientId))
            return BadRequest(ApiResponseHelper.Fail<object>("ClientId is required."));

        _clientRegistry.RegisterClient(request);

        _logger.LogInformation(
            "Client registered via API: {ClientId} ({MachineName})",
            request.ClientId, request.MachineName);

        var response = new ClientRegistrationResponse
        {
            ClientId = request.ClientId,
            Registered = true,
            Timestamp = DateTimeOffset.UtcNow
        };

        return Ok(ApiResponseHelper.Ok(response));
    }

    /// <summary>
    /// Report client module status to server.
    /// Called periodically by the client runtime.
    /// </summary>
    [HttpPost("{clientId}/status")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public IActionResult ReportStatus(
        string clientId,
        [FromBody] List<ModuleStatusReport> modules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var client = _clientRegistry.GetClient(clientId);
        if (client == null)
            return NotFound(ApiResponseHelper.Fail<object>($"Client '{clientId}' not found. Please register first."));

        _clientRegistry.UpdateClientStatus(clientId, modules ?? []);

        _logger.LogDebug("Status update from client {ClientId}: {ModuleCount} modules",
            clientId, modules?.Count ?? 0);

        return Accepted(ApiResponseHelper.Ok(new { message = "Status received." }));
    }

    /// <summary>
    /// Fetch policy for this client.
    /// Used for role-based module filtering and sidebar enablement.
    /// </summary>
    [HttpGet("{clientId}/policy")]
    [ProducesResponseType(typeof(ApiResponse<ClientProfile>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public IActionResult GetPolicy(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var client = _clientRegistry.GetClient(clientId);
        if (client == null)
            return NotFound(ApiResponseHelper.Fail<object>($"Client '{clientId}' not found. Please register first."));

        // Resolve policy from configuration based on client role
        var policy = _policyService.GetPolicyForRole(client.UserRole, clientId);

        _logger.LogDebug("Policy fetched for client {ClientId} (role: {Role})",
            clientId, client.UserRole);

        return Ok(ApiResponseHelper.Ok(policy));
    }

    /// <summary>
    /// Get all connected clients.
    /// Server-side admin view.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<ClientStatusDto>>), StatusCodes.Status200OK)]
    public IActionResult GetAllClients()
    {
        var clients = _clientRegistry.GetAllClients()
            .Select(c => new ClientStatusDto
            {
                ClientId = c.ClientId,
                MachineName = c.MachineName,
                UserRole = c.UserRole,
                ClientVersion = c.ClientVersion,
                RegisteredAt = c.RegisteredAt,
                LastReportedAt = c.LastReportedAt,
                ModuleCount = c.Modules.Count
            })
            .ToList()
            .AsReadOnly();

        return Ok(ApiResponseHelper.Ok(clients));
    }

    /// <summary>
    /// Get details for a specific client.
    /// </summary>
    [HttpGet("{clientId}")]
    [ProducesResponseType(typeof(ApiResponse<ClientDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public IActionResult GetClient(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var client = _clientRegistry.GetClient(clientId);
        if (client == null)
            return NotFound(ApiResponseHelper.Fail<object>($"Client '{clientId}' not found."));

        var detail = new ClientDetailDto
        {
            ClientId = client.ClientId,
            MachineName = client.MachineName,
            UserRole = client.UserRole,
            ClientVersion = client.ClientVersion,
            RegisteredAt = client.RegisteredAt,
            LastReportedAt = client.LastReportedAt,
            Modules = client.Modules
                .Select(m => new ModuleReportDto
                {
                    ModuleId = m.ModuleId,
                    DisplayName = m.DisplayName,
                    Status = m.Status.ToString(),
                    ReportedAt = m.ReportedAt
                })
                .ToList()
        };

        return Ok(ApiResponseHelper.Ok(detail));
    }
}

// ── DTOs for Client API ───────────────────────────────────────────────────

/// <summary>Response from client registration endpoint.</summary>
public sealed class ClientRegistrationResponse
{
    public required string ClientId { get; init; }
    public required bool Registered { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>Summary of a connected client (for listing).</summary>
public sealed class ClientStatusDto
{
    public required string ClientId { get; init; }
    public required string MachineName { get; init; }
    public required string UserRole { get; init; }
    public string? ClientVersion { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
    public DateTimeOffset LastReportedAt { get; init; }
    public int ModuleCount { get; init; }
}

/// <summary>Detailed view of a connected client.</summary>
public sealed class ClientDetailDto
{
    public required string ClientId { get; init; }
    public required string MachineName { get; init; }
    public required string UserRole { get; init; }
    public string? ClientVersion { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
    public DateTimeOffset LastReportedAt { get; init; }
    public List<ModuleReportDto> Modules { get; init; } = [];
}

/// <summary>Module status as reported by client.</summary>
public sealed class ModuleReportDto
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset ReportedAt { get; init; }
}
