using MAIS.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace MAIS.Server.Service.Api.Hubs;

/// <summary>
/// SignalR hub that pushes real-time module status updates to connected sidebar clients
/// and (future) to connected client runtimes.
/// The sidebar connects to this hub on startup and keeps a persistent connection.
/// </summary>
public sealed class StatusHub : Hub<IStatusHubClient>
{
    private readonly ILogger<StatusHub> _logger;

    public StatusHub(ILogger<StatusHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected to StatusHub: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
            _logger.LogWarning(exception,
                "Client disconnected from StatusHub with error: {ConnectionId}", Context.ConnectionId);
        else
            _logger.LogInformation(
                "Client disconnected from StatusHub: {ConnectionId}", Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Strongly-typed interface for messages the StatusHub pushes to clients.
/// Using a typed client interface prevents string-literal method name bugs.
/// </summary>
public interface IStatusHubClient
{
    /// <summary>Pushed whenever a module's status changes.</summary>
    Task ModuleStatusUpdated(ModuleStatusUpdate update);

    /// <summary>Pushed when a new module is registered at runtime.</summary>
    Task ModuleRegistered(ModuleRegisteredNotification notification);

    /// <summary>Pushed when a module is removed from the registry.</summary>
    Task ModuleUnregistered(string moduleId);
}

/// <summary>Status change payload sent over SignalR.</summary>
public sealed class ModuleStatusUpdate
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required ModuleStatus Status { get; init; }
    public string? StatusMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Registration notification payload sent over SignalR.</summary>
public sealed class ModuleRegisteredNotification
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required ModuleType Type { get; init; }
    public string? LaunchUri { get; init; }
}
