using MAIS.Modules.CrimsAddinHealth.Models;

namespace MAIS.Modules.CrimsAddinHealth.Agent;

public interface IAddinHealthAgent
{
    Task<AgentAnalysis> AnalyseScanResultAsync(ScanResult result, CancellationToken ct);
}