using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MAIS.Core.Models;
using MAIS.Modules.CrimsAddinHealth.Models;
using MAIS.Sidebar.Abstractions;
using System.Collections.ObjectModel;

namespace MAIS.Modules.CrimsAddinHealth.Sidebar;

public sealed partial class CrimsAddinHealthCardViewModel : ModuleCardViewModelBase
{
    [ObservableProperty] private int    _pendingAlertCount;
    [ObservableProperty] private bool   _hasHighRiskAlerts;
    [ObservableProperty] private string _alertSummary = "Checking…";

    public string ServiceBaseUrl { get; init; } = "http://localhost:5002";

    private readonly ObservableCollection<UpdateRequest> _pendingAlerts = [];

    public IRelayCommand OpenPanelCommand { get; }

    public CrimsAddinHealthCardViewModel(IModuleControlClient client) : base(client)
    {
        OpenPanelCommand = new RelayCommand(OpenPanel);
    }

    public static CrimsAddinHealthCardViewModel FromDescriptor(
        ModuleDescriptor descriptor,
        IModuleControlClient client,
        string serviceBaseUrl) =>
        new(client)
        {
            ServiceBaseUrl = serviceBaseUrl,
            ModuleId       = descriptor.Id,
            DisplayName    = descriptor.DisplayName,
            Description    = descriptor.Description,
            Version        = descriptor.Version,
            ModuleType     = descriptor.Type,
            Status         = descriptor.Status,
            StatusMessage  = descriptor.StatusMessage,
            LaunchUri      = descriptor.LaunchUri
        };

    public void OnNewAlert(UpdateRequest request)
    {
        _pendingAlerts.Add(request);
        RefreshAlertState();
    }

    public void OnAlertResolved(string requestId)
    {
        var item = _pendingAlerts.FirstOrDefault(r => r.RequestId == requestId);
        if (item is not null)
            _pendingAlerts.Remove(item);
        RefreshAlertState();
    }

    private void RefreshAlertState()
    {
        PendingAlertCount = _pendingAlerts.Count;
        HasHighRiskAlerts = _pendingAlerts.Any(r => r.AgentAnalysis.RiskLevel == "High");

        AlertSummary = PendingAlertCount switch
        {
            0 => "All CRIMS addins up to date",
            1 => "1 machine needs an update",
            _ => $"{PendingAlertCount} machines need updates"
        };
    }

    private void OpenPanel()
    {
        var panel = new AddinHealthPanel(ServiceBaseUrl);
        panel.Show();
    }
}