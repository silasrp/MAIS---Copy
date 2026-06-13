using MAIS.Core.Abstractions;
using MAIS.Core.Models;
using MAIS.Modules.CrimsSeverity.Reporters;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;


namespace MAIS.Modules.CrimsSeverity.Extensions;

public static class ServiceExtensions
{
    /// <summary>
    /// Registers the CrimsSeverity module with appropriate reporter based on environment.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration to bind CrimsSeverityOptions</param>
    /// <param name="isServerEnvironment">
    /// True if registering in MAIS.Server.Service (uses SignalR reporter).
    /// False if registering in MAIS.Client.Service (uses HTTP reporter).
    /// </param>
public static IServiceCollection AddCrimsSeverityModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<CrimsSeverityOptions>(opts =>
        configuration.GetSection(CrimsSeverityOptions.SectionName).Bind(opts));

    var options = configuration
        .GetSection(CrimsSeverityOptions.SectionName)
        .Get<CrimsSeverityOptions>() ?? new CrimsSeverityOptions();

    services.AddSingleton<ISeverityReporter, SignalRSeverityReporter>();
    services.AddSingleton<IModule>(new CrimsSeverityModule(options));
    services.AddHostedService<CrimsSeverityWorker>();

    return services;
}

    /// <summary>
    /// Configures CrimsSeverity module middleware (server-side only).
    /// Maps the SignalR hub and serves embedded static assets.
    /// </summary>
    public static WebApplication UseCrimsSeverityModule(this WebApplication app)
    {
        app.MapHub<CrimsSeverityHub>("/hubs/severity");

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new ManifestEmbeddedFileProvider(
                typeof(ServiceExtensions).Assembly, "wwwroot"),
        });

        return app;
    }
}
