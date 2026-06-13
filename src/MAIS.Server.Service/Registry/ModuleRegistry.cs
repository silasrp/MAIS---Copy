using System.Collections.Concurrent;
using MAIS.Core.Abstractions;
using MAIS.Core.Events;
using MAIS.Core.Models;
using Microsoft.Extensions.Logging;

namespace MAIS.Server.Service.Registry;

/// <summary>
/// Internal extension of IModuleRegistry that exposes methods needed by the server layer
/// but not part of the public framework contract.
/// </summary>
public interface IModuleRegistryService : IModuleRegistry
{
    /// <summary>Returns all live module instances (not snapshots).</summary>
    IReadOnlyList<IModule> GetAllModules();
}

/// <summary>
/// Thread-safe module registry for server-side modules. All mutating operations are synchronised via a lock.
/// Read operations against the snapshot dictionary are lock-free.
///
/// Status is maintained separately from the module instances so it can be updated
/// by the health monitor without touching the module's own state.
/// </summary>
public sealed class ModuleRegistry : IModuleRegistryService
{
    private readonly ILogger<ModuleRegistry> _logger;
    private readonly ConcurrentDictionary<string, IModule> _modules = new();
    private readonly ConcurrentDictionary<string, ModuleDescriptor> _descriptors = new();
    private readonly Lock _writeLock = new();

    public event EventHandler<ModuleStatusChangedEventArgs>? ModuleStatusChanged;

    public ModuleRegistry(ILogger<ModuleRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Register(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        lock (_writeLock)
        {
            if (_modules.ContainsKey(module.Id))
                throw new InvalidOperationException(
                    $"A module with id '{module.Id}' is already registered.");

            var descriptor = new ModuleDescriptor
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

            _modules[module.Id] = module;
            _descriptors[module.Id] = descriptor;
        }

        _logger.LogInformation(
            "Module registered: {ModuleId} ({DisplayName}) v{Version} [{Type}]",
            module.Id, module.DisplayName, module.Version, module.Type);
    }

    /// <inheritdoc/>
    public void Unregister(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        lock (_writeLock)
        {
            _modules.TryRemove(moduleId, out _);
            _descriptors.TryRemove(moduleId, out _);
        }

        _logger.LogInformation("Module unregistered: {ModuleId}", moduleId);
    }

    /// <inheritdoc/>
    public IModule? Get(string moduleId) =>
        _modules.TryGetValue(moduleId, out var m) ? m : null;

    /// <inheritdoc/>
    public IReadOnlyList<ModuleDescriptor> GetAll() =>
        _descriptors.Values.OrderBy(d => d.DisplayName).ToList().AsReadOnly();

    /// <inheritdoc/>
    public IReadOnlyList<IModule> GetAllModules() =>
        _modules.Values.ToList().AsReadOnly();

    /// <inheritdoc/>
    public void UpdateStatus(string moduleId, ModuleStatus status, string? message = null)
    {
        if (!_descriptors.TryGetValue(moduleId, out var current))
        {
            _logger.LogWarning("UpdateStatus called for unknown module {ModuleId}", moduleId);
            return;
        }

        if (current.Status == status) return; // No change — avoid noise

        var previous = current.Status;
        var updated = current.WithStatus(status, message);
        _descriptors[moduleId] = updated;

        _logger.LogInformation(
            "Module {ModuleId} status: {Previous} → {New}{Message}",
            moduleId, previous, status,
            message != null ? $" ({message})" : string.Empty);

        ModuleStatusChanged?.Invoke(this, new ModuleStatusChangedEventArgs
        {
            ModuleId = moduleId,
            DisplayName = updated.DisplayName,
            PreviousStatus = previous,
            NewStatus = status,
            Message = message
        });
    }
}
