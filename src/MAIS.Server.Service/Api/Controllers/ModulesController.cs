using MAIS.Core.Abstractions;
using MAIS.Core.Contracts;
using MAIS.Core.Models;
using MAIS.Server.Service.Api.Dto;
using MAIS.Server.Service.Registry;
using ApiResponseHelper = MAIS.Server.Service.Api.Dto.ApiResponse;
using Microsoft.AspNetCore.Mvc;

namespace MAIS.Server.Service.Api.Controllers;

/// <summary>
/// REST API for querying and managing MAIS server modules.
/// All endpoints return consistent <see cref="ApiResponse{T}"/> wrappers.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class ModulesController : ControllerBase
{
    private readonly IModuleRegistryService _registry;
    private readonly ILogger<ModulesController> _logger;

    public ModulesController(
        IModuleRegistryService registry,
        ILogger<ModulesController> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>Returns all registered server modules.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ModuleDto>>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<IReadOnlyList<ModuleDto>>> GetAll()
    {
        var modules = _registry.GetAll()
            .Select(ModuleDto.FromDescriptor)
            .ToList()
            .AsReadOnly();

        return Ok(ApiResponseHelper.Ok(modules));
    }

    /// <summary>Returns a single module by id.</summary>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(typeof(ApiResponse<ModuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public ActionResult<ApiResponse<ModuleDto>> GetById(string moduleId)
    {
        var descriptor = _registry.GetAll().FirstOrDefault(d => d.Id == moduleId);
        if (descriptor is null)
            return NotFound(ApiResponseHelper.Fail<object>($"Module '{moduleId}' not found."));

        return Ok(ApiResponseHelper.Ok(ModuleDto.FromDescriptor(descriptor)));
    }

    /// <summary>Requests a module to start.</summary>
    [HttpPost("{moduleId}/start")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(string moduleId, CancellationToken cancellationToken)
    {
        var module = _registry.Get(moduleId);
        if (module is null)
            return NotFound(ApiResponseHelper.Fail<object>($"Module '{moduleId}' not found."));

        var current = _registry.GetAll().FirstOrDefault(d => d.Id == moduleId);
        if (current?.Status is ModuleStatus.Running or ModuleStatus.Starting)
            return Conflict(ApiResponseHelper.Fail<object>($"Module '{moduleId}' is already {current.Status}."));

        _logger.LogInformation("Start requested for module {ModuleId} via API", moduleId);
        _registry.UpdateStatus(moduleId, ModuleStatus.Starting);

        // Fire-and-forget — the orchestrator owns the full lifecycle;
        // here we do a direct start for operational convenience.
        _ = Task.Run(async () =>
        {
            try
            {
                await module.InitialiseAsync(CancellationToken.None);
                await module.StartAsync(CancellationToken.None);
                _registry.UpdateStatus(moduleId, ModuleStatus.Running);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start module {ModuleId} via API", moduleId);
                _registry.UpdateStatus(moduleId, ModuleStatus.Faulted, ex.Message);
            }
        }, cancellationToken);

        return Accepted(ApiResponseHelper.Ok(new { message = $"Start requested for module '{moduleId}'." }));
    }

    /// <summary>Requests a module to stop.</summary>
    [HttpPost("{moduleId}/stop")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stop(string moduleId, CancellationToken cancellationToken)
    {
        var module = _registry.Get(moduleId);
        if (module is null)
            return NotFound(ApiResponseHelper.Fail<object>($"Module '{moduleId}' not found."));

        _logger.LogInformation("Stop requested for module {ModuleId} via API", moduleId);
        _registry.UpdateStatus(moduleId, ModuleStatus.Stopping);

        _ = Task.Run(async () =>
        {
            try
            {
                await module.StopAsync(CancellationToken.None);
                _registry.UpdateStatus(moduleId, ModuleStatus.Stopped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop module {ModuleId} via API", moduleId);
                _registry.UpdateStatus(moduleId, ModuleStatus.Faulted, ex.Message);
            }
        }, cancellationToken);

        return Accepted(ApiResponseHelper.Ok(new { message = $"Stop requested for module '{moduleId}'." }));
    }

    /// <summary>Returns the latest health snapshot for a module.</summary>
    [HttpGet("{moduleId}/health")]
    [ProducesResponseType(typeof(ApiResponse<HealthDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHealth(string moduleId, CancellationToken cancellationToken)
    {
        var module = _registry.Get(moduleId);
        if (module is null)
            return NotFound(ApiResponseHelper.Fail<object>($"Module '{moduleId}' not found."));

        var health = await module.GetHealthAsync(cancellationToken);
        return Ok(ApiResponseHelper.Ok(HealthDto.FromHealth(health)));
    }
}
