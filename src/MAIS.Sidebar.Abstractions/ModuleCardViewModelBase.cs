using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MAIS.Core.Models;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;

namespace MAIS.Sidebar.Abstractions;

/// <summary>
/// Base class for all MAIS sidebar module card view models.
///
/// Lives in MAIS.Sidebar.Abstractions so module projects can extend it
/// without referencing MAIS.Sidebar itself, avoiding a circular dependency.
///
/// Provides the common properties (module metadata, status) and the three
/// standard commands (Launch, Start, Stop) that every card exposes.
/// Derived classes add module-specific state on top.
/// </summary>
public abstract partial class ModuleCardViewModelBase : ObservableObject
{
    private readonly IModuleControlClient _controlClient;

    [ObservableProperty] private string _moduleId = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private ModuleType _moduleType;
    [ObservableProperty] private ModuleStatus _status = ModuleStatus.Unknown;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _launchUri;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private DateTimeOffset? _lastUpdated;

    // ── Derived display properties ─────────────────────────────────────────

    public string StatusLabel => Status switch
    {
        ModuleStatus.Running => "Running",
        ModuleStatus.Degraded => "Degraded",
        ModuleStatus.Stopped => "Stopped",
        ModuleStatus.Faulted => "Faulted",
        ModuleStatus.Starting => "Starting…",
        ModuleStatus.Stopping => "Stopping…",
        _ => "Unknown"
    };

    public string TypeLabel => ModuleType switch
    {
        ModuleType.InProcess => "In-process",
        ModuleType.ContainerisedService => "Container",
        ModuleType.ExternalEndpoint => "External",
        ModuleType.BackgroundWorker => "Worker",
        ModuleType.MessageQueueConsumer => "Queue",
        ModuleType.PythonWorker => "Python",
        _ => ModuleType.ToString()
    };

    public bool CanLaunch => !string.IsNullOrWhiteSpace(LaunchUri);
    public bool CanStart => Status is ModuleStatus.Stopped or ModuleStatus.Faulted or ModuleStatus.Unknown;
    public bool CanStop => Status is ModuleStatus.Running or ModuleStatus.Degraded;

    protected ModuleCardViewModelBase(IModuleControlClient controlClient)
    {
        _controlClient = controlClient;
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch()
    {
        if (string.IsNullOrWhiteSpace(LaunchUri)) return;
        try { Process.Start(new ProcessStartInfo(LaunchUri) { UseShellExecute = true }); }
        catch { /* swallow — best effort */ }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsBusy = true;
        try { await _controlClient.RequestStartAsync(ModuleId); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        IsBusy = true;
        try { await _controlClient.RequestStopAsync(ModuleId); }
        finally { IsBusy = false; }
    }

    // ── Status update ─────────────────────────────────────────────────────

    /// <summary>
    /// Called when the service pushes a status change via SignalR.
    /// Updates status and re-evaluates all derived properties and command states.
    /// </summary>
    public virtual void ApplyStatusUpdate(ModuleStatus newStatus, string? message, DateTimeOffset timestamp)
    {
        Status = newStatus;
        StatusMessage = message;
        LastUpdated = timestamp;

        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));

        LaunchCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
