using MAIS.Core.Events;
using MAIS.Core.Models;

namespace MAIS.Core.Abstractions;

/// <summary>
/// Central registry that tracks every module registered with the MAIS framework.
/// The orchestrator uses this to manage the full module lifecycle; the sidebar
/// queries it to build its display.
/// </summary>
public interface IModuleRegistry
{
    /// <summary>
    /// Registers a module with the framework. Throws <see cref="InvalidOperationException"/>
    /// if a module with the same <see cref="IModule.Id"/> is already registered.
    /// </summary>
    void Register(IModule module);

    /// <summary>
    /// Removes a module from the registry. If the module is running it must be
    /// stopped before unregistering; this method does not stop it automatically.
    /// </summary>
    void Unregister(string moduleId);

    /// <summary>Returns the live module instance, or null if not found.</summary>
    IModule? Get(string moduleId);

    /// <summary>Returns a read-only snapshot of all current module descriptors.</summary>
    IReadOnlyList<ModuleDescriptor> GetAll();

    /// <summary>Updates the recorded status for a module. Raises <see cref="ModuleStatusChanged"/>.</summary>
    void UpdateStatus(string moduleId, ModuleStatus status, string? message = null);

    /// <summary>Raised whenever a module's status transitions.</summary>
    event EventHandler<ModuleStatusChangedEventArgs> ModuleStatusChanged;
}
