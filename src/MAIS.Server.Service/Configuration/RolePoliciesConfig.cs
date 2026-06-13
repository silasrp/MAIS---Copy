namespace MAIS.Server.Service.Configuration;

/// <summary>
/// Configuration for a single role's policy.
/// Defines what modules and features are available for users with this role.
/// </summary>
public class RolePolicyConfig
{
    /// <summary>Role name (e.g., "Support", "Trader", "Admin").</summary>
    public required string Role { get; set; }

    /// <summary>Whether users with this role can launch the sidebar.</summary>
    public bool EnableSidebar { get; set; } = false;

    /// <summary>List of module IDs enabled for this role.</summary>
    public List<string> EnabledModules { get; set; } = new();

    /// <summary>Optional description of this role.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Container for all role policies.
/// Bound from the "RolePolicies" section in appsettings.json.
/// </summary>
public class RolePoliciesConfig
{
    public const string SectionName = "RolePolicies";

    /// <summary>Collection of all role policies indexed by role name.</summary>
    public Dictionary<string, RolePolicyConfig> Roles { get; set; } = new();

    /// <summary>Default policy for unknown roles.</summary>
    public RolePolicyConfig? Default { get; set; }

    /// <summary>Get policy for a specific role, or default if not found.</summary>
    public RolePolicyConfig GetPolicyForRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return Default ?? CreateEmptyPolicy("Default");

        var normalized = role.ToLowerInvariant();

        if (Roles.TryGetValue(normalized, out var policy))
            return policy;

        return Default ?? CreateEmptyPolicy(role);
    }

    private static RolePolicyConfig CreateEmptyPolicy(string role) =>
        new()
        {
            Role = role,
            EnableSidebar = false,
            EnabledModules = [],
            Description = "Fallback policy (no permissions)"
        };
}
