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
    public static IServiceCollection AddCrimsAddinHealthModule(this IServiceCollection services,
        IConfiguration configuration,
        ModuleHostType hostRole)
    {
        services.Configure<CrimsAddinHealthOptions>(
            configuration.GetSection(CrimsAddinHealthOptions.SectionName));

        services.PostConfigure<CrimsAddinHealthOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ServerApiUrl))
                opts.ServerApiUrl = configuration["Client:ServerUrl"] ?? "http://localhost:5000";
            if (string.IsNullOrWhiteSpace(opts.MachineRole))
                opts.MachineRole = configuration["Client:UserRole"] ?? "Unknown";
        });

        var options = configuration
            .GetSection(CrimsAddinHealthOptions.SectionName)
            .Get<CrimsAddinHealthOptions>() ?? new CrimsAddinHealthOptions();

        services.AddSingleton<IModule>(new CrimsAddinHealthModule(options));

        if (hostRole is ModuleHostType.Server or ModuleHostType.Both)
            RegisterServerServices(services);

        if (hostRole == ModuleHostType.Client)
            services.AddSingleton<IAddinHealthMessageHandler, ClientApprovalHandler>();

        if (hostRole is ModuleHostType.Client or ModuleHostType.Both)
            RegisterClientServices(services);

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

    private static void RegisterClientServices(IServiceCollection services)
    {
        services.AddSingleton<LocalAddinScanner>();
        services.AddSingleton<ProcessDetector>();
        services.AddSingleton<FileUpdater>();
        services.AddSingleton<NotificationRelay>();
        services.AddHostedService<AddinScanWorker>();
        services.AddHostedService<ServerHubRelay>();
        services.AddHttpClient("AddinHealthServer")
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<CrimsAddinHealthOptions>>().Value;
                client.BaseAddress = new Uri(opts.ServerApiUrl);
                client.Timeout     = TimeSpan.FromSeconds(30);
            });
    }


    public static WebApplication UseCrimsAddinHealthModule(this WebApplication app)
    {
        app.MapHub<AddinHealthHub>(ModuleConstants.HubPath);
        return app;
    }
}