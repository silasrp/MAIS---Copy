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
        string serviceBaseUrl,
        Action<string, string>? showNotification = null)
    {
        var localHubUrl = serviceBaseUrl.TrimEnd('/') + ModuleConstants.HubPath;

        registry.Register(
            moduleId:             ModuleConstants.ModuleId,
            factory: (descriptor, client) =>
            {
                var vm        = CrimsAddinHealthCardViewModel.FromDescriptor(descriptor, client, serviceBaseUrl);
                var hubClient = new CrimsAddinHealthHubClient(localHubUrl);

                vm.SetHubClient(hubClient);

                hubClient.AlertReceived += (_, req) =>
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        () => vm.OnNewAlert(req));

                hubClient.AlertResolved += (_, id) =>
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        () => vm.OnAlertResolved(id));

                hubClient.ToastReceived += (_, msg) =>
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        () => showNotification?.Invoke(msg.Title, msg.Body));

                _ = hubClient.StartAsync();
                return vm;
            },
            resourceDictionaryUri: ResourceDictionaryUri);
        return registry;
    }

}
