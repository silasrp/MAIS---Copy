using MAIS.Core.Abstractions;
using MAIS.Core.Models;

namespace MAIS.Modules.IdaLogIngestion;

public sealed class IdaLogIngestionModule : IModule
{
    private readonly IdaLogIngestionOptions _options;

    public string         Id          => ModuleConstants.ModuleId;
    public string         DisplayName => "IDA Log Ingestion";
    public string         Description => "Replaces Filebeat and Logstash: client-side log tailing with template-based classification and server-side Elasticsearch indexing";
    public string         Version     => "1.0.0";
    public ModuleType     Type        => ModuleType.BackgroundWorker;
    public Uri?           LaunchUri   => null;
    public ModuleHostType HostType    => _options.HostType;

    public IdaLogIngestionModule(IdaLogIngestionOptions options) => _options = options;

    public Task InitialiseAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartAsync(CancellationToken ct)      => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct)       => Task.CompletedTask;

    public Task<ModuleHealth> GetHealthAsync(CancellationToken ct) =>
        Task.FromResult(ModuleHealth.Healthy(Id, "IDA log ingestion module running"));
}
