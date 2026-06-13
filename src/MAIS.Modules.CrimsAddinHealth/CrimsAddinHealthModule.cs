using MAIS.Core.Abstractions;
using MAIS.Core.Models;

namespace MAIS.Modules.CrimsAddinHealth;

public sealed class CrimsAddinHealthModule : IModule
{
    private readonly CrimsAddinHealthOptions _options;

    public string         Id          => ModuleConstants.ModuleId;
    public string         DisplayName => "CRIMS Addin Health";
    public string         Description => "Automated CRIMS addin DLL version management with AI-assisted approval workflow";
    public string         Version     => "1.0.0";
    public ModuleType     Type        => ModuleType.BackgroundWorker;
    public Uri?           LaunchUri   => null;
    public ModuleHostType HostType    => _options.HostType;

    public CrimsAddinHealthModule(CrimsAddinHealthOptions options) => _options = options;

    public Task InitialiseAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartAsync(CancellationToken ct)      => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct)       => Task.CompletedTask;

    public Task<ModuleHealth> GetHealthAsync(CancellationToken ct) =>
        Task.FromResult(ModuleHealth.Healthy(Id, "CRIMS addin health module running"));
}