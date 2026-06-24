using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using MAIS.Modules.IdaLogIngestion.Sidebar;
using MAIS.Sidebar.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace MAIS.Modules.IdaLogIngestion.Extensions;

public static class SidebarExtensions
{
    private static readonly Uri ResourceDictionaryUri = new(
        "pack://application:,,,/MAIS.Modules.IdaLogIngestion;component/Sidebar/IdaResources.xaml",
        UriKind.Absolute);

    public static ModuleCardRegistry AddIdaLogIngestionSidebarCard(
        this ModuleCardRegistry registry,
        string serviceBaseUrl,
        IReadOnlyList<string> appIds)
    {
        var hubUrl = serviceBaseUrl.TrimEnd('/') + ModuleConstants.HubPath;

        registry.Register(
            moduleId: ModuleConstants.ModuleId,
            factory: (descriptor, client) =>
            {
                var vm = IngestionRateCardViewModel.FromDescriptor(descriptor, client, serviceBaseUrl, appIds);

                var http   = new HttpClient { BaseAddress = new Uri(serviceBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
                var logger = NullLogger<IdaRateRelayWorker>.Instance;
                var worker = new IdaRateRelayWorker(vm, http, hubUrl, logger);
                _ = worker.StartAsync(CancellationToken.None);

                return vm;
            },
            resourceDictionaryUri: ResourceDictionaryUri);

        return registry;
    }
}
