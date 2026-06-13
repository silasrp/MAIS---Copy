using MAIS.Core.Models;

namespace MAIS.Modules.CrimsSeverity
{
    /// <summary>
    /// Configuration for the CRIMS severity monitor module.
    /// Bound from the "Modules:CrimsSeverity" section in appsettings.json.
    /// </summary>
    public sealed class CrimsSeverityOptions
    {
        public const string SectionName = "Modules:CrimsSeverity";

        /// <summary>
        /// Determines where this module runs: Server, Client, or Both.
        /// Configurable per environment via appsettings.json.
        /// </summary>
        public ModuleHostType HostType { get; set; } = ModuleHostType.Server;

        public string DataEndpointUrl       { get; set; } = "http://boswidad01:8080/api/dashboard/filterseverity";
        public string SourceApplicationUrl  { get; set; } = "http://boswidad01:8080";
        public int    PollingIntervalSeconds { get; set; } = 10;

        /// <summary>Number of polling cycles in the spike detection window (Y).</summary>
        public int SpikeWindowCycles         { get; set; } = 3;

        /// <summary>Critical-count rise that triggers a spike alert within the window (X).</summary>
        public int SpikeCriticalDeltaThreshold { get; set; } = 50;

        /// <summary>Seconds the red glow persists after a spike is detected.</summary>
        public int CooldownSeconds           { get; set; } = 120;

        public int RequestTimeoutSeconds     { get; set; } = 5;
    }
}

