using MAIS.Core.Contracts;
using Microsoft.Extensions.Options;

namespace MAIS.Server.Service.Configuration;

/// <summary>
/// Service for resolving role-based policies.
/// Reads from RolePoliciesConfig and returns ClientProfile based on role.
/// </summary>
public interface IPolicyService
{
    /// <summary>Get policy for a specific role.</summary>
    ClientProfile GetPolicyForRole(string? role, string clientId);
}

/// <summary>
/// Configuration-based policy service.
/// Loads role policies from appsettings.json at startup.
/// </summary>
public sealed class ConfigurationPolicyService : IPolicyService
{
    private readonly RolePoliciesConfig _config;
    private readonly ILogger<ConfigurationPolicyService> _logger;

    public ConfigurationPolicyService(
        IOptions<RolePoliciesConfig> options,
        ILogger<ConfigurationPolicyService> logger)
    {
        _config = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public ClientProfile GetPolicyForRole(string? role, string clientId)
    {
        var rolePolicy = _config.GetPolicyForRole(role);

        _logger.LogInformation(
            "Policy resolved for role {Role}: {ModuleCount} modules, Sidebar={EnableSidebar}",
            rolePolicy.Role,
            rolePolicy.EnabledModules.Count,
            rolePolicy.EnableSidebar);

        return new ClientProfile
        {
            ClientId = clientId,
            Role = rolePolicy.Role,
            EnableSidebar = rolePolicy.EnableSidebar,
            EnabledModules = rolePolicy.EnabledModules,
            FetchedAt = DateTimeOffset.UtcNow
        };
    }
}
