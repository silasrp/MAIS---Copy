using MAIS.Modules.CrimsAddinHealth.Sidebar;
using MAIS.Sidebar.Abstractions;

namespace MAIS.Modules.CrimsAddinHealth.Extensions;

public static class SidebarExtensions
{
    private static readonly Uri ResourceDictionaryUri = new Uri(
        "pack://application:,,,/MAIS.Modules.CrimsAddinHealth;component/Sidebar/CrimsAddinHealthResources.xaml",
        UriKind.Absolute);

    public static ModuleCardRegistry AddCrimsAddinHealthSidebarCard(
        this ModuleCardRegistry registry,
        string serviceBaseUrl)
    {
        registry.Register(
            moduleId:              ModuleConstants.ModuleId,
            factory:               (descriptor, client) =>
                CrimsAddinHealthCardViewModel.FromDescriptor(descriptor, client, serviceBaseUrl),
            resourceDictionaryUri: ResourceDictionaryUri);

        return registry;
    }
}