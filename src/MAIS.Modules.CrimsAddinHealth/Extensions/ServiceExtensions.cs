using MAIS.Core.Abstractions;
using MAIS.Core.Models;
using MAIS.Modules.CrimsAddinHealth.Agent;
using MAIS.Modules.CrimsAddinHealth.Client;
using MAIS.Modules.CrimsAddinHealth.Hubs;
using MAIS.Modules.CrimsAddinHealth.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MAIS.Modules.CrimsAddinHealth.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddCrimsAddinHealthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CrimsAddinHealthOptions>(
            configuration.GetSection(CrimsAddinHealthOptions.SectionName));

        var options = configuration
            .GetSection(CrimsAddinHealthOptions.SectionName)
            .Get<CrimsAddinHealthOptions>() ?? new CrimsAddinHealthOptions();

        services.AddSingleton<IModule>(new CrimsAddinHealthModule(options));

        if (options.HostType is ModuleHostType.Server or ModuleHostType.Both)
            RegisterServerServices(services);

        if (options.HostType is ModuleHostType.Client or ModuleHostType.Both)
            RegisterClientServices(services, options);

        return services;
    }

    private static void RegisterServerServices(IServiceCollection services)
    {
        services.AddSingleton<ManifestService>();
        services.AddSingleton<UpdateQueue>();
        services.AddSingleton<AuditLogger>();
        services.AddSingleton<IAddinHealthAgent, AddinHealthAgent>();

        // Register UpdateOrchestrator as a singleton so we can expose it under
        // multiple interfaces (IHostedService + IAddinHealthMessageHandler).
        services.AddSingleton<UpdateOrchestrator>();
        services.AddSingleton<IAddinHealthMessageHandler>(
            sp => sp.GetRequiredService<UpdateOrchestrator>());
        services.AddHostedService(
            sp => sp.GetRequiredService<UpdateOrchestrator>());
    }

    private static void RegisterClientServices(IServiceCollection services, CrimsAddinHealthOptions options)
    {
        services.AddSingleton<LocalAddinScanner>();
        services.AddSingleton<ProcessDetector>();
        services.AddSingleton<FileUpdater>();
        services.AddSingleton<NotificationRelay>();
        services.AddSingleton<IAddinHealthMessageHandler, NullAddinHealthMessageHandler>();
        services.AddHostedService<AddinScanWorker>();

        // Named HttpClient for calling the server's addin-health REST API
        services.AddHttpClient("AddinHealthServer", client =>
        {
            client.BaseAddress = new Uri(options.ServerApiUrl);
            client.Timeout     = TimeSpan.FromSeconds(30);
        });
    }

    public static WebApplication UseCrimsAddinHealthModule(this WebApplication app)
    {
        app.MapHub<AddinHealthHub>(ModuleConstants.HubPath);
        return app;
    }
}