using MAIS.Modules.CrimsSeverity;
using MAIS.Modules.CrimsSeverity.Extensions;
using MAIS.Modules.CrimsAddinHealth.Extensions;
using MAIS.Modules.IdaLogIngestion;
using MAIS.Modules.IdaLogIngestion.Extensions;
using System.Linq;
using MAIS.Sidebar.Abstractions;
using MAIS.Sidebar.Configuration;
using MAIS.Sidebar.Services;
using MAIS.Sidebar.ViewModels;
using MAIS.Sidebar.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using System.IO;
using System.Windows;

namespace MAIS.Sidebar;

/// <summary>
/// Application entry point. Configures the DI container, wires up Serilog,
/// and launches the sidebar window and tray icon.
///
/// The application uses <c>ShutdownMode="OnExplicitShutdown"</c> so the sidebar
/// can be hidden without terminating the process — it lives in the system tray.
/// </summary>
public partial class App : System.Windows.Application
{
    private IServiceProvider? _services;
    private SystemTrayService? _trayService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var isFullMode = !e.Args.Contains("--tray");

        ConfigureSerilog();
        _services = ConfigureServices();

        // Always register module cards — this creates hub clients
        // that receive toast notifications regardless of role.
        // In tray mode the module cards simply aren't shown in the window.
        RegisterModuleCards(_services);

        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("MAIS Sidebar starting (mode: {Mode})", isFullMode ? "full" : "tray");

        try
        {
            var window = _services.GetRequiredService<SidebarWindow>();
            _trayService = _services.GetRequiredService<SystemTrayService>();
            _trayService.Initialise(window);

            // Admin/Support: show immediately.
            // Trader: stay in tray — toast notifications still work via hub.
            if (isFullMode)
                window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "MAIS Sidebar failed to start");
            System.Windows.MessageBox.Show(
                $"MAIS Sidebar failed to start:\n\n{ex.Message}",
                "MAIS — Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();

        if (_services is IDisposable disposable)
            disposable.Dispose();

        Log.Information("MAIS Sidebar exiting");
        Log.CloseAndFlush();

        base.OnExit(e);
    }

    // ── DI configuration ─────────────────────────────────────────────────

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        services.Configure<ServiceConnectionOptions>(
            configuration.GetSection(ServiceConnectionOptions.SectionName));

        services.AddSingleton<IConfiguration>(configuration);

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Services
        services.AddSingleton<IMaisServiceClient, MaisServiceClient>();
        services.AddSingleton<SystemTrayService>();

        // View models
        services.AddSingleton<SidebarViewModel>();

        // Views — transient so they can be GC'd if closed
        services.AddSingleton<SidebarWindow>();

        // Module registry
        services.AddSingleton<ModuleCardRegistry>();
        services.AddSingleton<IModuleControlClient>(
            sp => sp.GetRequiredService<IMaisServiceClient>());

        return services.BuildServiceProvider();
    }

    // After services are built, register module cards
    private static void RegisterModuleCards(IServiceProvider services)
    {
        var registry    = services.GetRequiredService<ModuleCardRegistry>();
        var tray        = services.GetRequiredService<SystemTrayService>();
        var baseUrl     = services.GetRequiredService<IOptions<ServiceConnectionOptions>>().Value.ApiBaseUrl;

        registry.AddCrimsSeveritySidebarCard(baseUrl);
        registry.AddCrimsAddinHealthSidebarCard(baseUrl, showNotification: (title, body) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => tray.ShowBalloonTip(title, body)));

        var idaOpts    = services.GetRequiredService<IConfiguration>()
                             .GetSection(IdaLogIngestionOptions.SectionName)
                             .Get<IdaLogIngestionOptions>() ?? new();
        var idaBaseUrl = string.IsNullOrEmpty(idaOpts.ServerApiUrl) ? baseUrl : idaOpts.ServerApiUrl;
        var idaAppIds  = idaOpts.Sources.Select(s => s.AppId).ToList();
        registry.AddIdaLogIngestionSidebarCard(idaBaseUrl, idaAppIds);

        foreach (var uri in registry.GetResourceDictionaryUris())
        {
            Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = uri });
        }
    }


    // ── Logging ───────────────────────────────────────────────────────────

    private static void ConfigureSerilog()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MAIS", "Logs", "mais-sidebar-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
