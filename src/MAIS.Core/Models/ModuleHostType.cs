namespace MAIS.Core.Models;

/// <summary>
/// Defines where a module runs: client, server, or both.
/// Used for filtering modules during service startup based on deployment model.
/// </summary>
public enum ModuleHostType
{
    /// <summary>Module runs only on client (MAIS.Client.Service).</summary>
    Client,

    /// <summary>Module runs only on server (MAIS.Server.Service).</summary>
    Server,

    /// <summary>Module runs on both client and server with shared lifecycle.</summary>
    Both
}
