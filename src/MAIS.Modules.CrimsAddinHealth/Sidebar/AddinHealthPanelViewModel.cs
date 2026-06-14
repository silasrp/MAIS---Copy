using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MAIS.Modules.CrimsAddinHealth.Models;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.IO;

namespace MAIS.Modules.CrimsAddinHealth.Sidebar;

// ── Sub-ViewModels ────────────────────────────────────────────────────────────

public sealed partial class UpdateRequestViewModel : ObservableObject
{
    public string         RequestId      { get; init; } = "";
    public string         MachineName    { get; init; } = "";
    public string         AssetTag       { get; init; } = "";
    public string         CrimsUserId    { get; init; } = "";
    public string         MachineRole    { get; init; } = "";
    public string         RiskLevel      { get; init; } = "Normal";
    public string         ApprovalMessage { get; init; } = "";
    public string         RecommendedTiming { get; init; } = "";
    public IReadOnlyList<DllMismatch> Mismatches { get; init; } = [];

    [ObservableProperty] private bool _isDllListExpanded;

    public string DllToggleLabel  => IsDllListExpanded ? "▲ Hide DLLs" : "▼ Show DLLs";
    public IRelayCommand ToggleDllListCommand { get; }

    public UpdateRequestViewModel()
    {
        ToggleDllListCommand = new RelayCommand(() =>
        {
            IsDllListExpanded = !IsDllListExpanded;
            OnPropertyChanged(nameof(DllToggleLabel));
        });
    }

    public string RiskBadgeColor => RiskLevel switch
    {
        "High"   => "#F87171",
        "Low"    => "#34D399",
        _        => "#FB923C"
    };

    public static UpdateRequestViewModel FromRequest(UpdateRequest r) => new()
    {
        RequestId       = r.RequestId,
        MachineName     = r.ScanResult.MachineName,
        AssetTag        = r.ScanResult.AssetTag,
        CrimsUserId     = r.ScanResult.CrimsUserId,
        MachineRole     = r.ScanResult.MachineRole,
        RiskLevel       = r.AgentAnalysis.RiskLevel,
        ApprovalMessage = r.AgentAnalysis.ApprovalMessage,
        RecommendedTiming = r.AgentAnalysis.RecommendedTiming,
        Mismatches      = r.ScanResult.Mismatches
    };
}

public sealed class QueueEntryViewModel
{
    public string         QueueId       { get; init; } = "";
    public string         MachineName   { get; init; } = "";
    public string         StatusLabel   { get; init; } = "";
    public DateTimeOffset? ScheduledFor { get; init; }
    public IReadOnlyList<DllMismatch> Mismatches { get; init; } = [];

    public string CountdownLabel => ScheduledFor.HasValue
        ? $"Scheduled for {ScheduledFor.Value.ToLocalTime():HH:mm dd/MM}"
        : "Queued";
}

public sealed class AuditRecordViewModel
{
    public DateTimeOffset Date        { get; init; }
    public string         MachineName { get; init; } = "";
    public string         CrimsUser   { get; init; } = "";
    public int            DllCount    { get; init; }
    public string         ApprovedBy  { get; init; } = "";
    public string         Status      { get; init; } = "";

    public static AuditRecordViewModel FromRecord(AuditRecord r) => new()
    {
        Date        = r.UpdateCompletedAt,
        MachineName = r.MachineName,
        CrimsUser   = r.CrimsUserId,
        DllCount    = r.UpdatedDlls.Count,
        ApprovedBy  = r.ApprovedByUserId,
        Status      = r.Success ? "✓ Success" : "✗ Failed"
    };
}

// ── Main Panel ViewModel ──────────────────────────────────────────────────────

public sealed partial class AddinHealthPanelViewModel : ObservableObject
{
    private readonly HttpClient _serviceClient;
    private readonly string     _localClientId;
    private readonly string     _localMachineName;
    private readonly string     _localUserId;

    public ObservableCollection<UpdateRequestViewModel> PendingApprovals { get; } = [];
    public ObservableCollection<QueueEntryViewModel>   ScheduledUpdates  { get; } = [];
    public ObservableCollection<AuditRecordViewModel>  AuditRecords      { get; } = [];

    [ObservableProperty] private string _selectedTab   = "Pending";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _statusMessage = "";

    public bool HasNoPending   => PendingApprovals.Count == 0;
    public bool HasNoScheduled => ScheduledUpdates.Count == 0;

    public string PendingTabHeader => PendingApprovals.Count == 0
        ? "Pending Approvals"
        : $"Pending Approvals ({PendingApprovals.Count})";

    public IAsyncRelayCommand<UpdateRequestViewModel?> ApproveCommand { get; }
    public IAsyncRelayCommand<UpdateRequestViewModel?> DeferCommand   { get; }
    public IRelayCommand<UpdateRequestViewModel?>      DismissCommand { get; }
    public IAsyncRelayCommand                         LoadAuditCommand { get; }
    public IRelayCommand                              ExportAuditCommand { get; }

    public AddinHealthPanelViewModel(HttpClient serviceClient, string localClientId, string localMachineName, string localUserId)
    {
        _serviceClient    = serviceClient;
        _localClientId    = localClientId;
        _localMachineName = localMachineName;
        _localUserId      = localUserId;

        ApproveCommand     = new AsyncRelayCommand<UpdateRequestViewModel?>(ApproveAsync);
        DeferCommand       = new AsyncRelayCommand<UpdateRequestViewModel?>(DeferAsync);
        DismissCommand     = new RelayCommand<UpdateRequestViewModel?>(Dismiss);
        LoadAuditCommand   = new AsyncRelayCommand(LoadAuditAsync);
        ExportAuditCommand = new RelayCommand(ExportAudit);

        PendingApprovals.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoPending));
            OnPropertyChanged(nameof(PendingTabHeader));
        };
        ScheduledUpdates.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasNoScheduled));
    }

    public void OnNewAlert(UpdateRequest request)
    {
        var vm = UpdateRequestViewModel.FromRequest(request);
        // Insert high-risk alerts at the top
        if (vm.RiskLevel == "High")
            PendingApprovals.Insert(0, vm);
        else
            PendingApprovals.Add(vm);
    }

    public void OnAlertResolved(string requestId)
    {
        var vm = PendingApprovals.FirstOrDefault(v => v.RequestId == requestId);
        if (vm is not null) PendingApprovals.Remove(vm);
    }

    public void OnScheduledUpdatesChanged(IReadOnlyList<QueueEntry> scheduled)
    {
        ScheduledUpdates.Clear();
        foreach (var entry in scheduled)
        {
            ScheduledUpdates.Add(new QueueEntryViewModel
            {
                QueueId      = entry.QueueId,
                MachineName  = entry.Request.ScanResult.MachineName,
                StatusLabel  = entry.Status.ToString(),
                ScheduledFor = entry.ScheduledFor,
                Mismatches   = entry.Request.ScanResult.Mismatches
            });
        }
    }

    private async Task ApproveAsync(UpdateRequestViewModel? vm)
    {
        if (vm is null) return;
        IsBusy = true;
        try
        {
            var approval = BuildApproval(vm.RequestId, isApproved: true, deferUntil: null);
            await PostApprovalAsync(approval);
            PendingApprovals.Remove(vm);
            StatusMessage = $"Approved update for {vm.MachineName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to approve: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task DeferAsync(UpdateRequestViewModel? vm)
    {
        if (vm is null) return;

        // Default defer: end of business day
        var deferUntil = new DateTimeOffset(
            DateTime.Today.Add(TimeSpan.FromHours(17)), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));

        IsBusy = true;
        try
        {
            var approval = BuildApproval(vm.RequestId, isApproved: true, deferUntil: deferUntil);
            await PostApprovalAsync(approval);
            PendingApprovals.Remove(vm);
            StatusMessage = $"Deferred update for {vm.MachineName} to {deferUntil:HH:mm}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to defer: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private void Dismiss(UpdateRequestViewModel? vm)
    {
        if (vm is null) return;
        PendingApprovals.Remove(vm);
    }

    private async Task LoadAuditAsync()
    {
        IsBusy = true;
        try
        {
            var records = await _serviceClient.GetFromJsonAsync<List<AuditRecord>>(
                "/api/v1/addin-health/audit?days=30");

            AuditRecords.Clear();
            foreach (var r in records ?? [])
                AuditRecords.Add(AuditRecordViewModel.FromRecord(r));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load audit: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private void ExportAudit()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Date,Machine,CRIMS User,DLLs Updated,Approved By,Status");
            foreach (var r in AuditRecords)
                sb.AppendLine($"{r.Date:yyyy-MM-dd HH:mm},{r.MachineName},{r.CrimsUser},{r.DllCount},{r.ApprovedBy},{r.Status}");

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"mais-addin-audit-{DateTime.Today:yyyy-MM-dd}.csv");

            File.WriteAllText(path, sb.ToString());
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private UpdateApproval BuildApproval(string requestId, bool isApproved, DateTimeOffset? deferUntil) =>
        new()
        {
            RequestId             = requestId,
            IsApproved            = isApproved,
            DeferUntil            = deferUntil,
            ApprovedByClientId    = _localClientId,
            ApprovedByMachineName = _localMachineName,
            ApprovedByMachineIp   = GetLocalIp(),
            ApprovedByUserId      = _localUserId,
            DecisionAt            = DateTimeOffset.UtcNow
        };

    private async Task PostApprovalAsync(UpdateApproval approval)
    {
        var json     = JsonSerializer.Serialize(approval);
        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _serviceClient.PostAsync("/api/v1/addin-health/approvals", content);
        response.EnsureSuccessStatusCode();
    }

    private static string GetLocalIp()
    {
        try
        {
            return System.Net.Dns.GetHostAddresses(Environment.MachineName)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.ToString() ?? "unknown";
        }
        catch { return "unknown"; }
    }
}