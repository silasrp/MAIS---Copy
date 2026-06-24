using System.Collections.Generic;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MAIS.Core.Models;
using MAIS.Modules.IdaLogIngestion.Models;
using MAIS.Sidebar.Abstractions;

namespace MAIS.Modules.IdaLogIngestion.Sidebar;

public sealed partial class IngestionRateCardViewModel : ModuleCardViewModelBase
{
    [ObservableProperty] private string _rateSummary = "Connecting…";
    [ObservableProperty] private bool   _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingReviews))]
    private int _pendingReviewCount;

    public bool HasPendingReviews   => PendingReviewCount > 0;
    public string ServiceBaseUrl    { get; init; } = "";
    public IReadOnlyList<string> AppIds { get; init; } = [];

    private TemplateReviewPanel? _reviewPanel;

    public IRelayCommand OpenReviewPanelCommand { get; }

    public IngestionRateCardViewModel(IModuleControlClient client) : base(client)
    {
        OpenReviewPanelCommand = new RelayCommand(OpenReviewPanel);
    }

    public static IngestionRateCardViewModel FromDescriptor(
        ModuleDescriptor descriptor,
        IModuleControlClient client,
        string serviceBaseUrl,
        IReadOnlyList<string> appIds) =>
        new(client)
        {
            ServiceBaseUrl = serviceBaseUrl,
            AppIds         = appIds,
            ModuleId       = descriptor.Id,
            DisplayName    = descriptor.DisplayName,
            Description    = descriptor.Description,
            Version        = descriptor.Version,
            ModuleType     = descriptor.Type,
            Status         = descriptor.Status,
            StatusMessage  = descriptor.StatusMessage,
            LaunchUri      = descriptor.LaunchUri
        };

    /// <summary>Called from IdaRateRelayWorker — safe to call from any thread.</summary>
    public void AddBucket(IngestionRateBucket bucket)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ApplyBucket(bucket);
        else
            dispatcher.BeginInvoke(() => ApplyBucket(bucket));
    }

    /// <summary>Called from IdaRateRelayWorker — safe to call from any thread.</summary>
    public void SetConnected(bool connected)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            IsConnected = connected;
        else
            dispatcher.BeginInvoke(() => IsConnected = connected);
    }

    private void ApplyBucket(IngestionRateBucket bucket) =>
        RateSummary = bucket.Count > 0 ? $"{bucket.Count:N0} records/min" : "No activity";

    private void OpenReviewPanel()
    {
        if (_reviewPanel is null || !_reviewPanel.IsLoaded)
            _reviewPanel = new TemplateReviewPanel(ServiceBaseUrl, AppIds);
        _reviewPanel.Show();
        _reviewPanel.Activate();
    }
}
