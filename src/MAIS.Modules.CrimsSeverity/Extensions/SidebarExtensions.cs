using MAIS.Modules.CrimsSeverity.Sidebar;
using MAIS.Sidebar.Abstractions;

namespace MAIS.Modules.CrimsSeverity.Extensions
{
    /// <summary>
    /// Extension methods for registering the CrimsSeverity module card with MAIS.Sidebar.
    ///
    /// MAIS.Sidebar calls this one method and has no other knowledge of this module:
    /// <code>
    /// // In App.xaml.cs:
    /// var registry = services.BuildServiceProvider().GetRequiredService&lt;ModuleCardRegistry&gt;();
    /// registry.AddCrimsSeveritySidebarCard();
    /// </code>
    /// </summary>
    public static class SidebarExtensions
    {
        private static readonly Uri ResourceDictionaryUri = new Uri(
            "pack://application:,,,/MAIS.Modules.CrimsSeverity;component/Sidebar/CrimsSeverityResources.xaml",
            UriKind.Absolute);

        /// <summary>
        /// Registers the CrimsSeverity card ViewModel factory and its WPF
        /// ResourceDictionary with the sidebar's <see cref="ModuleCardRegistry"/>.
        ///
        /// After calling this, the sidebar will automatically render a
        /// <see cref="CrimsSeverityCard"/> for any module with id "mais.crims-severity".
        /// </summary>
        public static ModuleCardRegistry AddCrimsSeveritySidebarCard(
            this ModuleCardRegistry registry,
            string serviceBaseUrl)
        {
            registry.Register(
                moduleId: ModuleConstants.ModuleId,
                factory: (descriptor, client) =>
                    CrimsSeverityCardViewModel.FromDescriptor(descriptor, client, serviceBaseUrl),
                resourceDictionaryUri: ResourceDictionaryUri);

            return registry;
        }
    }
}
