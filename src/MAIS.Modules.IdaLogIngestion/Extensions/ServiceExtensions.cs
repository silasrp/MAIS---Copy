using System;
using System.Linq;
using System.Net.Http;
using MAIS.Core.Abstractions;
using MAIS.Core.Models;
using MAIS.Modules.IdaLogIngestion.Client;
using MAIS.Modules.IdaLogIngestion.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Modules.IdaLogIngestion.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddIdaLogIngestionModule(
        this IServiceCollection services,
        IConfiguration configuration,
        ModuleHostType hostRole)
    {
        services.Configure<IdaLogIngestionOptions>(
            configuration.GetSection(IdaLogIngestionOptions.SectionName));

        var options = configuration
            .GetSection(IdaLogIngestionOptions.SectionName)
            .Get<IdaLogIngestionOptions>() ?? new IdaLogIngestionOptions();

        services.AddSingleton<IModule>(new IdaLogIngestionModule(options));

        if (hostRole is ModuleHostType.Server or ModuleHostType.Both)
            RegisterServerServices(services);

        if (hostRole is ModuleHostType.Client or ModuleHostType.Both)
            RegisterClientServices(services);

        return services;
    }

    private static void RegisterServerServices(IServiceCollection services)
    {
        services.AddSingleton<TemplateRegistryService>();
        services.AddSingleton<DurableReceiptBuffer>();
        services.AddSingleton<IngestionRateTracker>();
        services.AddSingleton<IdaReviewAgent>();

        services.AddSingleton<ElasticsearchSink>(sp =>
        {
            var opts   = sp.GetRequiredService<IOptions<IdaLogIngestionOptions>>().Value;
            var http   = sp.GetRequiredService<IHttpClientFactory>().CreateClient("IdaLogIngestionElasticsearch");
            var logger = sp.GetRequiredService<ILogger<ElasticsearchSink>>();
            var effectivePrefix = opts.CompatibilityMode ? opts.ParityTestIndexPrefix : opts.IndexPrefix;
            return new ElasticsearchSink(http, effectivePrefix, logger);
        });

        services.AddHostedService<IndexerWorker>();
        services.AddHostedService<StatsIndexerWorker>();
        services.AddHostedService<NovelTemplateReviewService>();

        services.AddHttpClient("IdaLogIngestionElasticsearch")
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<IdaLogIngestionOptions>>().Value;
                client.BaseAddress = new Uri(opts.ElasticsearchUrl);
                client.Timeout     = TimeSpan.FromSeconds(30);
            });
    }

    private static void RegisterClientServices(IServiceCollection services)
    {
        services.AddHostedService<IdaClientWorker>();

        services.AddHttpClient("IdaLogIngestionServer")
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<IdaLogIngestionOptions>>().Value;
                client.BaseAddress = new Uri(opts.ServerApiUrl);
                client.Timeout     = TimeSpan.FromSeconds(30);
            });

        services.Configure<MvcOptions>(opts =>
            opts.Conventions.Add(new ExcludeServerControllersConvention()));
    }

    public static WebApplication UseIdaLogIngestionModule(this WebApplication app)
    {
        app.MapHub<IdaRateHub>(ModuleConstants.HubPath);
        return app;
    }

    private sealed class ExcludeServerControllersConvention : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            var controller = application.Controllers
                .FirstOrDefault(c => c.ControllerType.Name == "IngestionController");
            if (controller is not null)
                application.Controllers.Remove(controller);
        }
    }
}
