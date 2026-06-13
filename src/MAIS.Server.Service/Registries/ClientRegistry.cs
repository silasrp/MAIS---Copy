using System.Collections.Concurrent;
using MAIS.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MAIS.Server.Service.Registries;

/// <summary>
/// Server-side client registry interface.
/// Exposes methods for tracking connected clients and their module states.
/// </summary>
public interface IClientRegistry
{
    /// <summary>Register or update a connected client.</summary>
    void RegisterClient(ClientRegistrationRequest request);

    /// <summary>Update client module status report.</summary>
    void UpdateClientStatus(string clientId, List<ModuleStatusReport> modules);

    /// <summary>Get all connected clients.</summary>
    IReadOnlyCollection<ConnectedClient> GetAllClients();

    /// <summary>Get a specific client by ID.</summary>
    ConnectedClient? GetClient(string clientId);

    /// <summary>Unregister (remove) a client.</summary>
    void UnregisterClient(string clientId);
}

/// <summary>
/// In-memory, thread-safe client registry for server-side tracking.
/// </summary>
public sealed class ClientRegistry : IClientRegistry
{
    private readonly ILogger<ClientRegistry> _logger;
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private readonly Lock _writeLock = new();

    public ClientRegistry(ILogger<ClientRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void RegisterClient(ClientRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);

        lock (_writeLock)
        {
            var client = new ConnectedClient
            {
                ClientId = request.ClientId,
                MachineName = request.MachineName,
                UserRole = request.UserRole,
                ClientVersion = request.ClientVersion,
                RegisteredAt = request.RegisteredAt,
                LastReportedAt = DateTimeOffset.UtcNow,
                Modules = request.Modules ?? []
            };

            _clients[request.ClientId] = client;
        }

        _logger.LogInformation(
            "Client registered: {ClientId} ({MachineName}) [{Role}]",
            request.ClientId, request.MachineName, request.UserRole);
    }

    /// <inheritdoc/>
    public void UpdateClientStatus(string clientId, List<ModuleStatusReport> modules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        if (_clients.TryGetValue(clientId, out var client))
        {
            lock (_writeLock)
            {
                client.LastReportedAt = DateTimeOffset.UtcNow;
                client.Modules = modules ?? [];
            }

            _logger.LogDebug(
                "Client {ClientId} status updated: {ModuleCount} modules",
                clientId, modules?.Count ?? 0);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ConnectedClient> GetAllClients() =>
        _clients.Values.ToList().AsReadOnly();

    /// <inheritdoc/>
    public ConnectedClient? GetClient(string clientId) =>
        _clients.TryGetValue(clientId, out var client) ? client : null;

    /// <inheritdoc/>
    public void UnregisterClient(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        if (_clients.TryRemove(clientId, out var client))
        {
            _logger.LogInformation("Client unregistered: {ClientId} ({MachineName})",
                clientId, client.MachineName);
        }
    }
}

/// <summary>
/// Server-side representation of a connected client with its current state.
/// </summary>
public sealed class ConnectedClient
{
    public required string ClientId { get; set; }
    public required string MachineName { get; set; }
    public required string UserRole { get; set; }
    public string? ClientVersion { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastReportedAt { get; set; }
    public List<ModuleStatusReport> Modules { get; set; } = [];
}
