using MAIS.Core.Abstractions;
using MAIS.Core.Contracts;
using MAIS.Core.Models;
using MAIS.Client.Service.Registries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MAIS.Client.Service.Api.Controllers;

/// <summary>
/// REST API for module discovery on the client service.
/// Exposes locally-available modules filtered by the current role's policy.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class ModulesController : ControllerBase
{
    private readonly IModuleRegistryService _registry;
    private readonly IPolicyProvider _policyProvider;
    private readonly ILogger<ModulesController> _logger;

    public ModulesController(
        IModuleRegistryService registry,
        IPolicyProvider policyProvider,
        ILogger<ModulesController> logger)
    {
        _registry = registry;
        _policyProvider = policyProvider;
        _logger = logger;
    }

    /// <summary>
    /// Get all modules available on this client, filtered by role policy.
    /// Returns only modules that are:
    /// - Loaded locally (HostType = Client or Both)
    /// - Enabled for this role (in policy.EnabledModules)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ModuleDto>>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<IReadOnlyList<ModuleDto>>> GetAll()
    {
        var policy = _policyProvider.GetCurrentPolicy();
        if (policy == null)
        {
            _logger.LogWarning("ModulesController.GetAll called but policy not yet available - service still initializing");
            // Return HTTP 503 (Service Unavailable) to indicate we're not ready yet
            // Sidebar will retry automatically
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiResponse<IReadOnlyList<ModuleDto>>
                {
                    Success = false,
                    Message = "Service initializing, policy not yet available. Retry shortly."
                });
        }

        var enabledModuleIds = policy.EnabledModules ?? [];
        var allModules = _registry.GetAllModules();

        var modules = allModules
            .Where(m => enabledModuleIds.Contains(m.Id))
            .Select(m => new ModuleDto
            {
                Id = m.Id,
                DisplayName = m.DisplayName,
                Description = m.Description,
                Version = m.Version,
                Type = m.Type.ToString(),
                Status = m.Status.ToString(),
                StatusMessage = m.StatusMessage,
                LaunchUri = m.LaunchUri?.ToString()
            })
            .ToList()
            .AsReadOnly();

        _logger.LogDebug("Returning {ModuleCount} modules for role policy", modules.Count);

        return Ok(new ApiResponse<IReadOnlyList<ModuleDto>>
        {
            Success = true,
            Data = modules
        });
    }

    /// <summary>
    /// Get a single module by ID (if enabled for this role).
    /// </summary>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(typeof(ApiResponse<ModuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public ActionResult<ApiResponse<ModuleDto>> GetModule(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        var policy = _policyProvider.GetCurrentPolicy();
        if (policy == null || !policy.EnabledModules.Contains(moduleId))
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Message = $"Module '{moduleId}' not found or not enabled for this role."
            });
        }

        var descriptor = _registry.GetAllModules().FirstOrDefault(m => m.Id == moduleId);
        if (descriptor == null)
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Message = $"Module '{moduleId}' not found."
            });
        }

        var dto = new ModuleDto
        {
            Id = descriptor.Id,
            DisplayName = descriptor.DisplayName,
            Description = descriptor.Description,
            Version = descriptor.Version,
            Type = descriptor.Type.ToString(),
            Status = descriptor.Status.ToString(),
            StatusMessage = descriptor.StatusMessage,
            LaunchUri = descriptor.LaunchUri?.ToString()
        };

        return Ok(new ApiResponse<ModuleDto>
        {
            Success = true,
            Data = dto
        });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────

/// <summary>DTO for module information (mirrors Server's ModuleDto).</summary>
public sealed class ModuleDto
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public string? StatusMessage { get; init; }
    public string? LaunchUri { get; init; }
}
