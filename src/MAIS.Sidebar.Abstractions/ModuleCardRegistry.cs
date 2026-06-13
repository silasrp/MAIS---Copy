using MAIS.Core.Models;

namespace MAIS.Sidebar.Abstractions;

/// <summary>
/// Central registry where sidebar modules announce their card ViewModel factory
/// and WPF resource dictionary URI.
///
/// MAIS.Sidebar resolves all registered factories to build module cards.
/// Module projects call <see cref="Register"/> in their sidebar extension method.
/// MAIS.Sidebar itself has no compile-time knowledge of any specific module.
/// </summary>
public sealed class ModuleCardRegistry
{
    private sealed record ModuleRegistration(
        Func<ModuleDescriptor, IModuleControlClient, ModuleCardViewModelBase> ViewModelFactory,
        Uri? ResourceDictionaryUri
    );

    private readonly Dictionary<string, ModuleRegistration> _registrations = new();

    /// <summary>
    /// Registers a module's card factory and its WPF ResourceDictionary.
    ///
    /// <paramref name="resourceDictionaryUri"/> should be a pack URI pointing at
    /// the ResourceDictionary inside the module assembly that defines a
    /// <c>DataTemplate</c> with <c>DataType</c> set to the concrete ViewModel type.
    /// WPF will then auto-apply it without any explicit DataTemplateSelector.
    ///
    /// Example:
    /// <code>
    /// pack://application:,,,/MAIS.Modules.CrimsSeverity;component/Sidebar/CrimsSeverityResources.xaml
    /// </code>
    /// </summary>
    public void Register<TViewModel>(
        string moduleId,
        Func<ModuleDescriptor, IModuleControlClient, TViewModel> factory,
        Uri? resourceDictionaryUri = null)
        where TViewModel : ModuleCardViewModelBase
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(factory);

        if (_registrations.ContainsKey(moduleId))
            throw new InvalidOperationException(
                $"A sidebar card is already registered for module '{moduleId}'.");

        _registrations[moduleId] = new ModuleRegistration(
            (descriptor, client) => factory(descriptor, client),
            resourceDictionaryUri);
    }

    /// <summary>
    /// Creates the card ViewModel for the given descriptor.
    /// Falls back to a default factory if the module has no registered card.
    /// </summary>
    public ModuleCardViewModelBase CreateViewModel(
        ModuleDescriptor descriptor,
        IModuleControlClient client,
        Func<ModuleDescriptor, IModuleControlClient, ModuleCardViewModelBase> defaultFactory)
    {
        if (_registrations.TryGetValue(descriptor.Id, out var reg))
            return reg.ViewModelFactory(descriptor, client);

        return defaultFactory(descriptor, client);
    }

    /// <summary>
    /// Returns all registered ResourceDictionary URIs so the sidebar host
    /// can merge them into <c>Application.Resources</c> at startup.
    /// </summary>
    public IReadOnlyList<Uri> GetResourceDictionaryUris() =>
        _registrations.Values
            .Select(r => r.ResourceDictionaryUri)
            .Where(u => u is not null)
            .Select(u => u!)
            .ToList()
            .AsReadOnly();
}
