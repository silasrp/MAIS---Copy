using MAIS.Client.Service.Configuration;
using MAIS.Client.Service.Registries;
using MAIS.Client.Service.Workers;
using MAIS.Core.Abstractions;
using MAIS.Infrastructure.Extensions;
using MAIS.Modules.CrimsSeverity.Extensions;
using MAIS.Modules.CrimsAddinHealth.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// ── Bootstrap logger (active until full Serilog is configured) ──────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("MAIS Client Service starting up");

    var builder = WebApplication.CreateBuilder(args);

    // ── Windows Service hosting ──────────────────────────────────────────────
    builder.Host.UseWindowsService(options =>
        options.ServiceName = "MAIS Client - Multi Agentic Intelligent Suite");

    // ── Serilog (replaces Microsoft logging entirely) ────────────────────────
    builder.Host.UseSerilog((context, services, config) =>
        config.ReadFrom.Configuration(context.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .Enrich.WithMachineName()
              .Enrich.WithThreadId());

    // ── Configuration ────────────────────────────────────────────────────
    builder.Services.Configure<ClientOptions>(
        builder.Configuration.GetSection(ClientOptions.SectionName));

    // ── Infrastructure ───────────────────────────────────────────────────
    builder.Services.AddMaisInfrastructure();

    // ── Client-side module registry (local only) ────────────────────────
    builder.Services.AddSingleton<IModuleRegistryService, ClientModuleRegistry>();
    builder.Services.AddSingleton<IModuleRegistry>(
        sp => sp.GetRequiredService<IModuleRegistryService>());

    // ── Server communication ─────────────────────────────────────────────
    var httpClientBuilder = builder.Services.AddHttpClient<IServerApiClient, ServerApiClient>()
        .ConfigureHttpClient((sp, client) =>
        {
            var clientOptions = sp.GetRequiredService<IOptions<ClientOptions>>();
            client.BaseAddress = new Uri(clientOptions.Value.ServerUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

    // In development, allow self-signed certificates
    if (!builder.Environment.IsProduction())
    {
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) => true
            });
    }

    // ── API Controllers ──────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.MaximumReceiveMessageSize = 32 * 1024; // 32 KB
    });
    builder.Services.AddEndpointsApiExplorer();

    // ── Policy Provider ──────────────────────────────────────────────────
    builder.Services.AddSingleton<ClientOrchestratorWorker>();
    builder.Services.AddSingleton<IPolicyProvider>(sp =>
        sp.GetRequiredService<ClientOrchestratorWorker>());

    // ── Background workers ───────────────────────────────────────────────
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ClientOrchestratorWorker>());
    builder.Services.AddHostedService<HealthReporterWorker>();

    // ── Module registrations ─────────────────────────────────────────────
    builder.Services.AddCrimsSeverityModule(builder.Configuration);
    builder.Services.AddCrimsAddinHealthModule(builder.Configuration);

    // ── Build and Map ────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Module setup (maps hub and static files) ─────────────────────────
    app.UseCrimsSeverityModule();
    app.UseCrimsAddinHealthModule();

    app.MapControllers();

    await app.RunAsync();

    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException && ex is not HostAbortedException)
{
    Log.Fatal(ex, "MAIS Client Service terminated unexpectedly");
    return 1;
}
finally
{
    Log.Information("MAIS Client Service shut down complete");
    await Log.CloseAndFlushAsync();
}
