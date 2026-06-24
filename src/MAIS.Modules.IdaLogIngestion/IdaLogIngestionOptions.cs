using MAIS.Core.Models;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion;

public sealed class IdaLogIngestionOptions
{
    public const string SectionName = "Modules:IdaLogIngestion";

    public ModuleHostType HostType            { get; set; } = ModuleHostType.Both;
    public string ElasticsearchUrl            { get; set; } = "http://168.66.122.12:9200";
    public bool   CompatibilityMode           { get; set; } = true;
    public string IndexPrefix                 { get; set; } = "ida-logs";
    public string ReceiptBufferPath           { get; set; } = @"C:\ProgramData\MAIS\IdaLogIngestion\ReceiptBuffer";
    public string TemplateRegistryPath        { get; set; } = @"C:\ProgramData\MAIS\IdaLogIngestion\TemplateRegistry";
    public string PendingReviewPath           { get; set; } = @"C:\ProgramData\MAIS\IdaLogIngestion\PendingTemplateReviews";
    public int    MaxConcurrentIndexBatches   { get; set; } = 4;
    public string AgentExePath                { get; set; } = @"Agent\agent.exe";
    public int    AgentTimeoutSeconds         { get; set; } = 30;
    public List<string> SidebarVisibleRoles   { get; set; } = ["Support", "Admin"];

    // Client-only: machine identity stamped onto every log record.
    // Defaults to the OS machine name if not set explicitly in appsettings.
    public string AssetTag              { get; set; } = "";
    public string ServerApiUrl          { get; set; } = "";
    public string SpoolPath             { get; set; } = @"C:\ProgramData\MAIS\IdaLogIngestion\Spool";
    public string LocalRegistryCachePath { get; set; } = @"C:\ProgramData\MAIS\IdaLogIngestion\RegistryCache";

    // Fleet-wide source definitions, pulled by every client at runtime.
    public List<LogSourceDefinition> Sources  { get; set; } = [];
}
