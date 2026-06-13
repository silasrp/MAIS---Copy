using MAIS.Core.Models;
using MAIS.Sidebar.Abstractions;

namespace MAIS.Modules.CrimsSeverity.Sidebar
{
    /// <summary>
    /// View model for the CRIMS severity distribution card.
    /// Extends <see cref="ModuleCardViewModelBase"/> — no additional state is
    /// needed beyond what the base provides, since all live data flows through
    /// the WebView2 panel via SignalR directly from MAIS.Service.
    /// </summary>
    public sealed class CrimsSeverityCardViewModel : ModuleCardViewModelBase
    {
        /// <summary>Base URL of the local MAIS service hosting this module's panel + hub.</summary>
        public string ServiceBaseUrl { get; init; } = "http://localhost:5002";

        /// <summary>URL of the severity panel served by the local MAIS service.</summary>
        public string PanelUrl => $"{ServiceBaseUrl}/severity-panel.html";

        /// <summary>URL of the severity SignalR hub served by the local MAIS service.</summary>
        public string HubUrl => $"{ServiceBaseUrl}/hubs/severity";

        public CrimsSeverityCardViewModel(IModuleControlClient client) : base(client) { }

        public static CrimsSeverityCardViewModel FromDescriptor(
            ModuleDescriptor descriptor,
            IModuleControlClient client,
            string serviceBaseUrl) =>
            new(client)
            {
                ServiceBaseUrl = serviceBaseUrl,
                ModuleId = descriptor.Id,
                DisplayName = descriptor.DisplayName,
                Description = descriptor.Description,
                Version = descriptor.Version,
                ModuleType = descriptor.Type,
                Status = descriptor.Status,
                StatusMessage = descriptor.StatusMessage,
                LaunchUri = descriptor.LaunchUri
            };
    }
}
