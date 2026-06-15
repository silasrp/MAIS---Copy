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
        var localHubUrl = serviceBaseUrl.TrimEnd('/') + ModuleConstants.HubPath;

        registry.Register(
            moduleId:             ModuleConstants.ModuleId,
            factory: (descriptor, client) =>
            {
                System.Diagnostics.Debug.WriteLine($"[CAH] Factory invoked. Thread={Environment.CurrentManagedThreadId}");
                var vm        = CrimsAddinHealthCardViewModel.FromDescriptor(descriptor, client, serviceBaseUrl);
                System.Diagnostics.Debug.WriteLine($"[CAH] VM created.");
                var hubClient = new CrimsAddinHealthHubClient(localHubUrl);
                System.Diagnostics.Debug.WriteLine($"[CAH] HubClient constructed.");

                vm.SetHubClient(hubClient);

                hubClient.AlertReceived += (_, req) =>
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        () => vm.OnNewAlert(req));

                hubClient.AlertResolved += (_, id) =>
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        () => vm.OnAlertResolved(id));

                System.Diagnostics.Debug.WriteLine($"[CAH] Firing hubClient.StartAsync fire-and-forget.");
                _ = hubClient.StartAsync();
                System.Diagnostics.Debug.WriteLine($"[CAH] Factory returning vm.");
                return vm;
            },
            resourceDictionaryUri: ResourceDictionaryUri);
        return registry;   
    }
}
