using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Sidebar;

public sealed partial class NovelTemplateReviewItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isProcessing;

    public string TemplateId     { get; }
    public string AppId          { get; }
    public string TokenDisplay   { get; }
    public string SampleMessage  { get; }
    public string AiDescription  { get; }
    public string AiSuggestion   { get; }
    public string AiRationale    { get; }
    public int    SeenOnMachines { get; }

    public bool HasAiEnrichment => !string.IsNullOrEmpty(AiDescription);

    public NovelTemplateReviewItemViewModel(NovelTemplateReviewItem item)
    {
        TemplateId     = item.TemplateId;
        AppId          = item.AppId;
        TokenDisplay   = string.Join(" ", item.TokenPattern.Select(t => t == "*" ? "[•]" : t));
        SampleMessage  = item.SampleMessages.Length > 0 ? item.SampleMessages[0] : "—";
        AiDescription  = item.AiHumanReadableDescription;
        AiSuggestion   = item.AiSuggestedClassification?.ToString() ?? "—";
        AiRationale    = item.AiRationale;
        SeenOnMachines = item.SeenOnMachineCount;
    }
}
