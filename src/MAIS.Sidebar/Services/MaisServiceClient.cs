using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MAIS.Core.Contracts;
using MAIS.Core.Models;
using MAIS.Sidebar.Configuration;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MAIS.Sidebar.Services;

/// <summary>Contract for the sidebar's communication with MAIS.Service.</summary>
public interface IMaisServiceClient : IAsyncDisposable, MAIS.Sidebar.Abstractions.IModuleControlClient
{
    bool IsConnected { get; }

    Task<IReadOnlyList<ModuleDescriptor>> GetModulesAsync(CancellationToken ct = default);

    /// <summary>Fired on the UI thread whenever a module's status changes.</summary>
    event EventHandler<ModuleStatusChangedArgs> ModuleStatusChanged;

    /// <summary>Fired when the connection to the service is established or lost.</summary>
    event EventHandler<ConnectionStateChangedArgs> ConnectionStateChanged;

    Task StartAsync(CancellationToken ct = default);
}

// ── Implementation ────────────────────────────────────────────────────────

public sealed class MaisServiceClient : IMaisServiceClient
{
    private readonly string _apiBaseUrl;
    private readonly string _signalRUrl;
    private readonly HttpClient _http;
    private readonly HubConnection _hub;
    private readonly ILogger<MaisServiceClient> _logger;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    private bool _disposed;

    public bool IsConnected => _hub.State == HubConnectionState.Connected;

    public event EventHandler<ModuleStatusChangedArgs>? ModuleStatusChanged;
    public event EventHandler<ConnectionStateChangedArgs>? ConnectionStateChanged;

    public MaisServiceClient(
        IOptions<ServiceConnectionOptions> options,
        ILogger<MaisServiceClient> logger)
    {
        var config = options.Value;
        _apiBaseUrl = config.ApiBaseUrl;
        _signalRUrl = config.SignalRUrl;
        _logger = logger;
        _dispatcher = System.Windows.Application.Current.Dispatcher;

        var handler = new HttpClientHandler();
#if DEBUG
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
#endif

        _http = new HttpClient(handler) 
        { 
            BaseAddress = new Uri(_apiBaseUrl), 
            Timeout = TimeSpan.FromSeconds(config.Timeout) 
        };

        _hub = new HubConnectionBuilder()
            .WithUrl(_signalRUrl, options =>
            {
#if DEBUG
                options.HttpMessageHandlerFactory = _ => handler;
#endif
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) })
            .Build();

        RegisterHubHandlers();

        _hub.Closed    += OnHubClosed;
        _hub.Reconnecting += OnHubReconnecting;
        _hub.Reconnected  += OnHubReconnected;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Connecting to MAIS Service - API: {ApiBaseUrl}, SignalR: {SignalRUrl}", 
            _apiBaseUrl, _signalRUrl);

        // Try once with timeout, then background retry will kick in
        try
        {
            await TryConnectWithRetryAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Initial connection attempt timed out; background reconnection enabled");
        }
    }

    // ── REST operations ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<ModuleDescriptor>> GetModulesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<ApiResponse<List<ModuleDto>>>(
                "api/v1/modules", ct);

            return response?.Data?
                .Select(d => new ModuleDescriptor
                {
                    Id = d.Id,
                    DisplayName = d.DisplayName,
                    Description = d.Description,
                    Version = d.Version,
                    Type = Enum.Parse<ModuleType>(d.Type),
                    Status = Enum.Parse<ModuleStatus>(d.Status),
                    StatusMessage = d.StatusMessage,
                    LaunchUri = d.LaunchUri
                })
                .ToList()
                .AsReadOnly()
                ?? (IReadOnlyList<ModuleDescriptor>)Array.Empty<ModuleDescriptor>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch modules from MAIS Service");
            return Array.Empty<ModuleDescriptor>();
        }
    }

    public async Task RequestStartAsync(string moduleId, CancellationToken ct = default)
    {
        await _http.PostAsync($"api/v1/modules/{moduleId}/start", null, ct);
    }

    public async Task RequestStopAsync(string moduleId, CancellationToken ct = default)
    {
        await _http.PostAsync($"api/v1/modules/{moduleId}/stop", null, ct);
    }

    // ── SignalR handlers ──────────────────────────────────────────────────

    private void RegisterHubHandlers()
    {
        _hub.On<StatusUpdateMessage>("ModuleStatusUpdated", update =>
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (!Enum.TryParse<ModuleStatus>(update.Status, out var status)) return;

                ModuleStatusChanged?.Invoke(this, new ModuleStatusChangedArgs
                {
                    ModuleId = update.ModuleId,
                    DisplayName = update.DisplayName,
                    NewStatus = status,
                    StatusMessage = update.StatusMessage,
                    Timestamp = update.Timestamp
                });
            });
        });
    }

    // ── Connection lifecycle ──────────────────────────────────────────────

    private async Task TryConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _hub.StartAsync(ct);
                _logger.LogInformation("Connected to MAIS Service SignalR hub");
                RaiseConnectionState(true);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to MAIS Service. Retrying in 5s");
                RaiseConnectionState(false, ex.Message);
                await Task.Delay(5000, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private Task OnHubClosed(Exception? ex)
    {
        _logger.LogWarning("SignalR hub connection closed: {Message}", ex?.Message);
        RaiseConnectionState(false, ex?.Message);
        return Task.CompletedTask;
    }

    private Task OnHubReconnecting(Exception? ex)
    {
        _logger.LogWarning("SignalR hub reconnecting: {Message}", ex?.Message);
        RaiseConnectionState(false, "Reconnecting…");
        return Task.CompletedTask;
    }

    private Task OnHubReconnected(string? connectionId)
    {
        _logger.LogInformation("SignalR hub reconnected: {ConnectionId}", connectionId);
        RaiseConnectionState(true);
        return Task.CompletedTask;
    }

    private void RaiseConnectionState(bool connected, string? message = null) =>
        _dispatcher.BeginInvoke(() =>
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedArgs
            {
                IsConnected = connected,
                Message = message
            }));

    // ── IAsyncDisposable ──────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _hub.DisposeAsync();
        _http.Dispose();
    }

    // ── Private DTOs (mirror the service's shape) ─────────────────────────

    private sealed class ApiEnvelope<T> { public T? Data { get; set; } }

    private sealed class ModuleDto
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public string? StatusMessage { get; set; }
        public string? LaunchUri { get; set; }
    }

    private sealed class StatusUpdateMessage
    {
        public string ModuleId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string? StatusMessage { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}

// ── Event arg types ───────────────────────────────────────────────────────

public sealed class ModuleStatusChangedArgs : EventArgs
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required ModuleStatus NewStatus { get; init; }
    public string? StatusMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public sealed class ConnectionStateChangedArgs : EventArgs
{
    public required bool IsConnected { get; init; }
    public string? Message { get; init; }
}

/// <summary>DTO for module data from the API (mirrors ServerService ModuleDto).</summary>
public sealed class ModuleDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; init; }

    [JsonPropertyName("launchUri")]
    public string? LaunchUri { get; init; }
}
