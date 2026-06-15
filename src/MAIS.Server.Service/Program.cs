using MAIS.Core.Models;
using MAIS.Modules.CrimsSeverity.Extensions;
using MAIS.Modules.CrimsAddinHealth.Extensions;
using MAIS.Infrastructure.Extensions;
using MAIS.Server.Service.Configuration;
using MAIS.Server.Service.Registry;
using MAIS.Server.Service.Registries;
using MAIS.Server.Service.Workers;
using Serilog;
using Serilog.Events;

// ── Bootstrap logger (active until full Serilog is configured) ──────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("MAIS Server Service starting up");

    var builder = WebApplication.CreateBuilder(args);

    // ── Windows Service hosting ──────────────────────────────────────────────
    builder.Host.UseWindowsService(options =>
        options.ServiceName = "MAIS Server - Multi Agentic Intelligent Suite");

    // ── Serilog (replaces Microsoft logging entirely) ────────────────────────
    builder.Host.UseSerilog((context, services, config) =>
        config.ReadFrom.Configuration(context.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .Enrich.WithMachineName()
              .Enrich.WithThreadId());

    // ── Configuration ────────────────────────────────────────────────────────
    builder.Services.Configure<MaisOptions>(
        builder.Configuration.GetSection(MaisOptions.SectionName));

    builder.Services.Configure<RolePoliciesConfig>(
        builder.Configuration.GetSection(RolePoliciesConfig.SectionName));

    // ── Infrastructure ───────────────────────────────────────────────────────
    builder.Services.AddMaisInfrastructure();

    // ── Module registry ──────────────────────────────────────────────────────
    builder.Services.AddSingleton<IModuleRegistryService, ModuleRegistry>();
    builder.Services.AddSingleton<MAIS.Core.Abstractions.IModuleRegistry>(
        sp => sp.GetRequiredService<IModuleRegistryService>());

    // ── Client registry (server-side tracking of connected clients) ──────────
    builder.Services.AddSingleton<IClientRegistry, ClientRegistry>();

    // ── Policy service (role-based configuration) ────────────────────────────
    builder.Services.AddSingleton<IPolicyService, ConfigurationPolicyService>();

    // ── Background workers ───────────────────────────────────────────────────
    builder.Services.AddHostedService<OrchestratorWorker>();
    builder.Services.AddHostedService<HealthMonitorWorker>();

    // ── ASP.NET Core: controllers, SignalR, OpenAPI ─────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.MaximumReceiveMessageSize = 32 * 1024; // 32 KB
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title = "MAIS Server API",
            Version = "v1",
            Description = "Multi Agentic Intelligent Suite — server control plane"
        });
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
    });

    // ── CORS: allow sidebar and clients (localhost only) ───────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("SidebarPolicy", policy =>
            policy.WithOrigins("http://localhost", "https://localhost")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials());
    });

    // ── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── Module registrations ────────────────────────────────────────────────
    builder.Services.AddCrimsSeverityModule(builder.Configuration);
    builder.Services.AddCrimsAddinHealthModule(builder.Configuration, ModuleHostType.Server);

    var app = builder.Build();

    // ── Middleware pipeline ──────────────────────────────────────────────────
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
        options.GetLevel = (ctx, elapsed, ex) =>
            ex != null || ctx.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : LogEventLevel.Debug;
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MAIS Server API v1"));
    }

    // ── Module setup (includes static files) ──────────────────────────────────
    app.UseCrimsSeverityModule();
    app.UseCrimsAddinHealthModule();

    app.UseCors("SidebarPolicy");
    app.UseRouting();
    app.UseAuthorization();

    // ── Endpoint mappings ────────────────────────────────────────────────────
    app.MapControllers();
    app.MapHealthChecks("/health");

    Log.Information("MAIS Server configured. Starting application");
    await app.RunAsync();

    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException && ex is not HostAbortedException)
{
    Log.Fatal(ex, "MAIS Server terminated unexpectedly");
    return 1;
}
finally
{
    Log.Information("MAIS Server shut down complete");
    await Log.CloseAndFlushAsync();
}
