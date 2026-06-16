using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MAIS.Core.Models;
using MAIS.Modules.CrimsAddinHealth.Models;
using MAIS.Sidebar.Abstractions;
using System.Collections.ObjectModel;

namespace MAIS.Modules.CrimsAddinHealth.Sidebar;

public sealed partial class CrimsAddinHealthCardViewModel : ModuleCardViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingAlerts))]
    private int _pendingAlertCount;

    [ObservableProperty] private bool   _hasHighRiskAlerts;
    [ObservableProperty] private string _alertSummary = "All CRIMS addins up to date";

    public bool HasPendingAlerts => PendingAlertCount > 0;

    public string ServiceBaseUrl { get; init; } = "http://localhost:5002";

    private readonly ObservableCollection<UpdateRequest> _pendingAlerts = [];

    private CrimsAddinHealthHubClient? _hubClient;
    private AddinHealthPanel?          _panel;

    public void SetHubClient(CrimsAddinHealthHubClient hubClient) => _hubClient = hubClient;

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
        try
        {
            _pendingAlerts.Add(request);
            RefreshAlertState();
            _panel?.ViewModel.OnNewAlert(request);
        }
        catch { }
    }

    public void OnAlertResolved(string requestId)
    {
        try
        {
            var item = _pendingAlerts.FirstOrDefault(r => r.RequestId == requestId);
            if (item is not null) _pendingAlerts.Remove(item);
            RefreshAlertState();
            _panel?.ViewModel.OnAlertResolved(requestId);
        }
        catch { }
    }

    private void RefreshAlertState()
    {
        PendingAlertCount = _pendingAlerts.Count;
        HasHighRiskAlerts = _pendingAlerts.Any(r => r.AgentAnalysis?.RiskLevel == "High");

        AlertSummary = PendingAlertCount switch
        {
            0 => "All CRIMS addins up to date",
            1 => "1 machine needs an update",
            _ => $"{PendingAlertCount} machines need updates"
        };
    }

    private void OpenPanel()
    {
        if (_panel is null || !_panel.IsLoaded)
        {
            Func<UpdateApproval, Task>? submit = _hubClient is not null
                ? approval => _hubClient.SubmitApprovalAsync(approval)
                : null;

            _panel = new AddinHealthPanel(ServiceBaseUrl, submit);

            foreach (var alert in _pendingAlerts)
                _panel.ViewModel.OnNewAlert(alert);
        }
        _panel.Show();
    }
}