using MAIS.Core.Abstractions;
using MAIS.Core.Events;
using MAIS.Core.Models;

namespace MAIS.Client.Service.Registries;

/// <summary>
/// Client-side module registry service.
/// Tracks only client modules (filtering by HostType.Client or HostType.Both).
/// </summary>
public interface IModuleRegistryService : IModuleRegistry
{
    /// <summary>Returns the list of all module descriptors.</summary>
    IReadOnlyList<ModuleDescriptor> GetAllModules();
}

/// <summary>
/// In-memory, thread-safe client module registry.
/// Registers only modules that have HostType == Client or Both.
/// </summary>
public sealed class ClientModuleRegistry : IModuleRegistryService
{
    private readonly Dictionary<string, IModule> _modules = new();
    private readonly Dictionary<string, ModuleDescriptor> _descriptors = new();
    private readonly Lock _lock = new();
    private readonly ILogger<ClientModuleRegistry> _logger;

    public event EventHandler<ModuleStatusChangedEventArgs>? ModuleStatusChanged;

    public ClientModuleRegistry(ILogger<ClientModuleRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Register(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(module.Id);

        lock (_lock)
        {
            if (_modules.ContainsKey(module.Id))
                throw new InvalidOperationException($"Module '{module.Id}' already registered.");

            _modules[module.Id] = module;
            _descriptors[module.Id] = new ModuleDescriptor
            {
                Id = module.Id,
                DisplayName = module.DisplayName,
                Description = module.Description,
                Version = module.Version,
                Type = module.Type,
                LaunchUri = module.LaunchUri?.ToString(),
                Status = ModuleStatus.Unknown,
                RegisteredAt = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Module registered: {ModuleId} (HostType: {HostType})",
                module.Id, module.HostType);
        }
    }

    /// <inheritdoc/>
    public void Unregister(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        lock (_lock)
        {
            if (_modules.Remove(moduleId))
            {
                _descriptors.Remove(moduleId);
                _logger.LogInformation("Module unregistered: {ModuleId}", moduleId);
            }
        }
    }

    /// <inheritdoc/>
    public IModule? Get(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        lock (_lock)
            return _modules.TryGetValue(moduleId, out var module) ? module : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModuleDescriptor> GetAll()
    {
        lock (_lock)
            return _descriptors.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModuleDescriptor> GetAllModules()
    {
        return GetAll();
    }

    /// <inheritdoc/>
    public void UpdateStatus(string moduleId, ModuleStatus status, string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        lock (_lock)
        {
            if (_descriptors.TryGetValue(moduleId, out var descriptor))
            {
                var previousStatus = descriptor.Status;
                var updatedDescriptor = descriptor.WithStatus(status, message);
                _descriptors[moduleId] = updatedDescriptor;

                _logger.LogDebug("Module status updated: {ModuleId} → {Status}",
                    moduleId, status);

                ModuleStatusChanged?.Invoke(this, new ModuleStatusChangedEventArgs
                {
                    ModuleId = moduleId,
                    DisplayName = descriptor.DisplayName,
                    PreviousStatus = previousStatus,
                    NewStatus = status,
                    Message = message
                });
            }
        }
    }
}
