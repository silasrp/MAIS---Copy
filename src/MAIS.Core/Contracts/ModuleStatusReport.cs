namespace MAIS.Core.Contracts;

using MAIS.Core.Models;

/// <summary>
/// Client-side status report for a single module.
/// Sent to server for aggregation, monitoring, and governance decisions.
/// </summary>
public class ModuleStatusReport
{
    /// <summary>Unique module identifier.</summary>
    public required string ModuleId { get; set; }

    /// <summary>Human-readable module name.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Current operational status of the module.</summary>
    public ModuleStatus Status { get; set; }

    /// <summary>Health and diagnostic information from the module. Null if not yet checked.</summary>
    public ModuleHealth? Health { get; set; }

    /// <summary>Timestamp when this report was generated.</summary>
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
}
