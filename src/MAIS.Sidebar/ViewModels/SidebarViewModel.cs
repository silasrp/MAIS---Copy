using MAIS.Sidebar.Abstractions;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MAIS.Sidebar.Services;
using MAIS.Sidebar.ViewModels.Base;
using Microsoft.Extensions.Logging;

namespace MAIS.Sidebar.ViewModels;

/// <summary>
/// Root view model for the MAIS sidebar window.
/// Owns the module list and the connection to MAIS.Service.
/// </summary>
public sealed partial class SidebarViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IMaisServiceClient _serviceClient;
    private readonly ILogger<SidebarViewModel> _logger;
    private readonly ModuleCardRegistry _cardRegistry;

    [ObservableProperty] private bool _isServiceConnected;
    [ObservableProperty] private string _connectionStatus = "Connecting…";
    [ObservableProperty] private string? _connectionStatusDetail;
    [ObservableProperty] private bool _isLoadingModules = true;

    public ObservableCollection<ModuleCardViewModelBase> Modules { get; } = [];

    public string RunningCount => Modules.Count(m => m.Status == MAIS.Core.Models.ModuleStatus.Running).ToString();

    public string DegradedCount => Modules.Count(m => m.Status == MAIS.Core.Models.ModuleStatus.Degraded).ToString();

    public string FaultedCount => Modules.Count(m => m.Status == MAIS.Core.Models.ModuleStatus.Faulted).ToString();

    public int TotalModules     => Modules.Count;

    public SidebarViewModel(IMaisServiceClient serviceClient, ModuleCardRegistry cardRegistry, ILogger<SidebarViewModel> logger)
    {
        _serviceClient = serviceClient;
        _logger = logger;
        _cardRegistry = cardRegistry;

        _serviceClient.ModuleStatusChanged    += OnModuleStatusChanged;
        _serviceClient.ConnectionStateChanged += OnConnectionStateChanged;
    }

    // ── Initialisation ────────────────────────────────────────────────────

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        IsLoadingModules = true;
        ConnectionStatus = "Connecting…";

        try
        {
            // Use a timeout so we don't block if service is unavailable
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5)); // 5-second timeout

            try
            {
                await _serviceClient.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Service didn't respond in time — that's OK, continue anyway
                ConnectionStatus = "Connection timeout — retrying…";
                _logger.LogWarning("MAIS Service connection timed out");
            }

            // Refresh modules (also with timeout)
            await RefreshModulesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialise sidebar");
            ConnectionStatus = "Connection failed";
        }
        finally
        {
            IsBusy = false;
            IsLoadingModules = false;
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshModulesAsync(CancellationToken ct = default)
    {
        IsLoadingModules = true;
        try
        {
            _logger.LogInformation("RefreshModules: fetching module list");
            var descriptors = await _serviceClient.GetModulesAsync(ct);
            _logger.LogInformation("RefreshModules: got {Count} modules", descriptors.Count);

            var incoming = descriptors.ToList();
            var toRemove = Modules
                .Where(m => !incoming.Any(d => d.Id == m.ModuleId))
                .ToList();

            foreach (var vm in toRemove)
                Modules.Remove(vm);

            foreach (var descriptor in incoming)
            {
                _logger.LogInformation("RefreshModules: creating VM for {Id}", descriptor.Id);
                var existing = Modules.FirstOrDefault(m => m.ModuleId == descriptor.Id);
                if (existing is not null)
                {
                    existing.ApplyStatusUpdate(descriptor.Status, descriptor.StatusMessage, DateTimeOffset.UtcNow);
                }
                else
                {
                    Modules.Add(_cardRegistry.CreateViewModel(descriptor, _serviceClient, (d, c) => ModuleCardViewModel.FromDescriptor(d, c)));
                }
                _logger.LogInformation("RefreshModules: done {Id}", descriptor.Id);
            }

            UpdateCounts();
            _logger.LogInformation("RefreshModules: complete");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh module list");
        }
        finally
        {
            IsLoadingModules = false;
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void OnModuleStatusChanged(object? sender, ModuleStatusChangedArgs args)
    {
        var card = Modules.FirstOrDefault(m => m.ModuleId == args.ModuleId);
        if (card is not null)
        {
            card.ApplyStatusUpdate(args.NewStatus, args.StatusMessage, args.Timestamp);
            UpdateCounts();
        }
        else
        {
            // New module appeared — do a full refresh to pick up its metadata
            _ = RefreshModulesAsync();
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedArgs args)
    {
        IsServiceConnected = args.IsConnected;
        ConnectionStatus = args.IsConnected ? "Connected" : "Disconnected";
        ConnectionStatusDetail = args.IsConnected ? null : args.Message;

        if (args.IsConnected)
            _ = RefreshModulesAsync();
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(DegradedCount));
        OnPropertyChanged(nameof(FaultedCount));
        OnPropertyChanged(nameof(TotalModules));
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _serviceClient.ModuleStatusChanged    -= OnModuleStatusChanged;
        _serviceClient.ConnectionStateChanged -= OnConnectionStateChanged;
        await _serviceClient.DisposeAsync();
    }
}
