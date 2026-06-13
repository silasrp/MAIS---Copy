using MAIS.Core.Models;
using MAIS.Sidebar.Abstractions;

namespace MAIS.Sidebar.ViewModels;

public class ModuleCardViewModel : ModuleCardViewModelBase
{
    public ModuleCardViewModel(IModuleControlClient client) : base(client) { }

    public static ModuleCardViewModel FromDescriptor(
        ModuleDescriptor descriptor,
        IModuleControlClient client) =>
        new(client)
        {
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