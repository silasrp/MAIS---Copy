using MAIS.Core.Contracts;

namespace MAIS.Client.Service.Registries;

/// <summary>
/// Provides access to the current role policy for this client.
/// The policy is fetched from the server and cached locally.
/// </summary>
public interface IPolicyProvider
{
    /// <summary>Get the current cached policy for this client's role.</summary>
    ClientProfile? GetCurrentPolicy();
}
