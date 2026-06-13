namespace MAIS.Sidebar.Abstractions;

/// <summary>
/// The minimal client interface that module card view models need to request
/// lifecycle operations from the MAIS service.
///
/// This is deliberately narrow — modules depend on this abstraction, not on
/// the full <c>IMaisServiceClient</c> which lives in MAIS.Sidebar.
/// </summary>
public interface IModuleControlClient
{
    Task RequestStartAsync(string moduleId, CancellationToken ct = default);
    Task RequestStopAsync(string moduleId, CancellationToken ct = default);
}
