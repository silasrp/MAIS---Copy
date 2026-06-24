using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.IdaLogIngestion.Sidebar;

public sealed partial class TemplateReviewPanelViewModel : ObservableObject
{
    private readonly HttpClient              _http;
    private readonly IReadOnlyList<string>   _appIds;
    private readonly ILogger<TemplateReviewPanelViewModel> _logger;

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<NovelTemplateReviewItemViewModel> Items { get; } = [];
    public bool HasItems => Items.Count > 0;

    public IAsyncRelayCommand                                   RefreshCommand          { get; }
    public IAsyncRelayCommand<NovelTemplateReviewItemViewModel> ApproveIngestCommand    { get; }
    public IAsyncRelayCommand<NovelTemplateReviewItemViewModel> ApproveStatsOnlyCommand { get; }
    public IAsyncRelayCommand<NovelTemplateReviewItemViewModel> DiscardCommand          { get; }

    public TemplateReviewPanelViewModel(
        HttpClient http,
        IReadOnlyList<string> appIds,
        ILogger<TemplateReviewPanelViewModel> logger)
    {
        _http   = http;
        _appIds = appIds;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);

        ApproveIngestCommand    = new AsyncRelayCommand<NovelTemplateReviewItemViewModel>(
            item => DoApproveAsync(item!, ClassificationAction.Ingest));
        ApproveStatsOnlyCommand = new AsyncRelayCommand<NovelTemplateReviewItemViewModel>(
            item => DoApproveAsync(item!, ClassificationAction.StatsOnly));
        DiscardCommand          = new AsyncRelayCommand<NovelTemplateReviewItemViewModel>(
            item => DoApproveAsync(item!, ClassificationAction.Discard));

        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItems));
    }

    public async Task LoadAsync()
    {
        IsLoading     = true;
        StatusMessage = "";
        try
        {
            Items.Clear();
            foreach (var appId in _appIds)
            {
                var items = await _http.GetFromJsonAsync<IReadOnlyList<NovelTemplateReviewItem>>(
                    $"/api/v1/ida/templates/pending?appId={Uri.EscapeDataString(appId)}");
                if (items is null) continue;
                foreach (var item in items)
                    Items.Add(new NovelTemplateReviewItemViewModel(item));
            }

            StatusMessage = Items.Count == 0 ? "No templates pending review" : "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pending templates");
            StatusMessage = "Could not reach server — check connectivity";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DoApproveAsync(NovelTemplateReviewItemViewModel item, ClassificationAction action)
    {
        item.IsProcessing = true;
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"/api/v1/ida/templates/{Uri.EscapeDataString(item.TemplateId)}/approve",
                new ApproveDto(action));
            resp.EnsureSuccessStatusCode();
            Items.Remove(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Approve failed for template {Id}", item.TemplateId);
        }
        finally
        {
            item.IsProcessing = false;
        }
    }

    private sealed record ApproveDto(ClassificationAction Action);
}
